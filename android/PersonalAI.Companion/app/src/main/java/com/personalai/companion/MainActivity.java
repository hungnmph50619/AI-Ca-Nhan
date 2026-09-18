package com.personalai.companion;

import android.app.Activity;
import android.os.Bundle;
import android.view.View;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class MainActivity extends Activity {
    private final ExecutorService executor =
            Executors.newSingleThreadExecutor();
    private final JSONArray conversation =
            new JSONArray();
    private final StringBuilder transcript =
            new StringBuilder();

    private SecureTokenStore tokenStore;

    private LinearLayout pairingSection;
    private LinearLayout connectedSection;
    private EditText serverUrlInput;
    private EditText deviceNameInput;
    private EditText pairingCodeInput;
    private CheckBox allowHttpCheck;
    private Button pairButton;
    private TextView connectionStatus;
    private TextView chatTranscript;
    private EditText messageInput;
    private Button sendButton;
    private TextView tasksText;
    private TextView errorText;
    private Button refreshButton;
    private Button disconnectButton;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        tokenStore = new SecureTokenStore(this);
        bindViews();

        pairButton.setOnClickListener(view -> pair());
        sendButton.setOnClickListener(view -> sendMessage());
        refreshButton.setOnClickListener(view -> refreshConnectedData());
        disconnectButton.setOnClickListener(view -> disconnectLocal());

        hydratePairingFields();
        updateMode();

        if (tokenStore.isPaired()) {
            refreshConnectedData();
        }
    }

    @Override
    protected void onDestroy() {
        executor.shutdownNow();
        super.onDestroy();
    }

    private void bindViews() {
        pairingSection = findViewById(R.id.pairingSection);
        connectedSection = findViewById(R.id.connectedSection);
        serverUrlInput = findViewById(R.id.serverUrlInput);
        deviceNameInput = findViewById(R.id.deviceNameInput);
        pairingCodeInput = findViewById(R.id.pairingCodeInput);
        allowHttpCheck = findViewById(R.id.allowHttpCheck);
        pairButton = findViewById(R.id.pairButton);
        connectionStatus = findViewById(R.id.connectionStatus);
        chatTranscript = findViewById(R.id.chatTranscript);
        messageInput = findViewById(R.id.messageInput);
        sendButton = findViewById(R.id.sendButton);
        tasksText = findViewById(R.id.tasksText);
        errorText = findViewById(R.id.errorText);
        refreshButton = findViewById(R.id.refreshButton);
        disconnectButton = findViewById(R.id.disconnectButton);
    }

    private void hydratePairingFields() {
        if (!tokenStore.getServerUrl().isEmpty()) {
            serverUrlInput.setText(tokenStore.getServerUrl());
        }
        if (!tokenStore.getDeviceName().isEmpty()) {
            deviceNameInput.setText(tokenStore.getDeviceName());
        } else {
            deviceNameInput.setText(android.os.Build.MODEL);
        }
        allowHttpCheck.setChecked(tokenStore.getAllowHttp());
    }

    private void updateMode() {
        boolean paired = tokenStore.isPaired();
        pairingSection.setVisibility(
                paired ? View.GONE : View.VISIBLE);
        connectedSection.setVisibility(
                paired ? View.VISIBLE : View.GONE);

        if (paired) {
            connectionStatus.setText(
                    "Đã ghép nối · "
                            + tokenStore.getDeviceName()
                            + " · workspace "
                            + tokenStore.getWorkspaceId());
        }
    }

    private void pair() {
        clearError();

        String server = ApiClient.normalizeServerUrl(
                serverUrlInput.getText().toString());
        String deviceName =
                deviceNameInput.getText().toString().trim();
        String code = pairingCodeInput
                .getText()
                .toString()
                .trim()
                .replace("-", "")
                .replace(" ", "")
                .toUpperCase(Locale.ROOT);
        boolean allowHttp = allowHttpCheck.isChecked();

        if (!validateServer(server, allowHttp)) {
            return;
        }
        if (deviceName.length() < 2) {
            showError("Hãy nhập tên thiết bị.");
            return;
        }
        if (code.length() != 12) {
            showError("Mã ghép nối phải có 12 ký tự.");
            return;
        }

        setBusy(true);
        executor.execute(() -> {
            try {
                ApiClient api = new ApiClient(server);
                ApiClient.PairResult result =
                        api.pair(code, deviceName);
                tokenStore.save(
                        server,
                        allowHttp,
                        result.deviceName,
                        result.workspaceId,
                        result.token);

                runOnUiThread(() -> {
                    pairingCodeInput.setText("");
                    transcript.setLength(0);
                    conversation.clear();
                    updateMode();
                    setBusy(false);
                    Toast.makeText(
                            this,
                            "Ghép nối thành công.",
                            Toast.LENGTH_SHORT).show();
                    refreshConnectedData();
                });
            } catch (Exception exception) {
                runOnUiThread(() -> {
                    setBusy(false);
                    showError(safeMessage(exception));
                });
            }
        });
    }

    private boolean validateServer(
            String server,
            boolean allowHttp) {
        if (server.startsWith("https://")) {
            return true;
        }

        if (server.startsWith("http://") && allowHttp) {
            return true;
        }

        showError(
                "Mặc định Companion yêu cầu HTTPS. "
                + "Nếu dùng HTTP trên LAN tin cậy, hãy bật cả checkbox trên Android "
                + "và Companion:AllowInsecureHttp=true ở backend.");
        return false;
    }

    private void refreshConnectedData() {
        if (!tokenStore.isPaired()) {
            updateMode();
            return;
        }

        clearError();
        setBusy(true);

        String server = tokenStore.getServerUrl();
        String token = tokenStore.getToken();

        executor.execute(() -> {
            try {
                ApiClient api = new ApiClient(server);
                JSONObject me = api.me(token);
                JSONObject core = api.core(token);
                JSONObject tasks = api.tasks(token);

                String status = formatStatus(me, core);
                String taskText = formatTasks(tasks);

                runOnUiThread(() -> {
                    connectionStatus.setText(status);
                    tasksText.setText(taskText);
                    setBusy(false);
                });
            } catch (Exception exception) {
                runOnUiThread(() -> {
                    setBusy(false);
                    showError(safeMessage(exception));
                    if (safeMessage(exception).toLowerCase(Locale.ROOT)
                            .contains("token")) {
                        connectionStatus.setText(
                                "Token không còn hợp lệ. Hãy ghép nối lại.");
                    }
                });
            }
        });
    }

    private void sendMessage() {
        String message =
                messageInput.getText().toString().trim();
        if (message.isEmpty() || !tokenStore.isPaired()) {
            return;
        }

        clearError();
        messageInput.setText("");
        appendTranscript("Bạn", message);

        try {
            JSONObject userMessage = new JSONObject();
            userMessage.put("role", "user");
            userMessage.put("content", message);
            conversation.put(userMessage);
        } catch (Exception exception) {
            showError(safeMessage(exception));
            return;
        }

        trimConversation();
        setBusy(true);

        String server = tokenStore.getServerUrl();
        String token = tokenStore.getToken();

        executor.execute(() -> {
            try {
                ApiClient api = new ApiClient(server);
                JSONObject response =
                        api.chat(token, conversation);
                String answer = response.optString(
                        "message",
                        "Không có phản hồi.");

                JSONObject assistantMessage = new JSONObject();
                assistantMessage.put("role", "assistant");
                assistantMessage.put("content", answer);
                conversation.put(assistantMessage);
                trimConversation();

                runOnUiThread(() -> {
                    appendTranscript("PersonalAI", answer);
                    setBusy(false);
                });
            } catch (Exception exception) {
                runOnUiThread(() -> {
                    appendTranscript(
                            "Hệ thống",
                            "Không gửi được tin nhắn.");
                    setBusy(false);
                    showError(safeMessage(exception));
                });
            }
        });
    }

    private void trimConversation() {
        while (conversation.length() > 20) {
            conversation.remove(0);
        }
    }

    private void appendTranscript(
            String speaker,
            String message) {
        if (transcript.length() > 0) {
            transcript.append("\n\n");
        }
        transcript.append(speaker)
                .append(": ")
                .append(message);
        chatTranscript.setText(transcript.toString());
    }

    private String formatStatus(
            JSONObject me,
            JSONObject core) {
        JSONObject device =
                me.optJSONObject("device");
        String deviceName = device == null
                ? tokenStore.getDeviceName()
                : device.optString(
                        "name",
                        tokenStore.getDeviceName());

        return "Đã ghép nối · "
                + deviceName
                + "\nWorkspace: "
                + core.optString(
                        "workspaceName",
                        core.optString(
                                "workspaceId",
                                tokenStore.getWorkspaceId()))
                + "\nAI: "
                + core.optString("provider", "—")
                + " / "
                + core.optString("model", "—")
                + (core.optBoolean("aiConfigured", false)
                    ? " · configured"
                    : " · chưa cấu hình");
    }

    private String formatTasks(JSONObject response) {
        JSONArray tasks = response.optJSONArray("tasks");
        if (tasks == null || tasks.length() == 0) {
            return "Không có task trong workspace đã ghép nối.";
        }

        StringBuilder builder = new StringBuilder();
        int limit = Math.min(tasks.length(), 20);
        for (int i = 0; i < limit; i++) {
            JSONObject task = tasks.optJSONObject(i);
            if (task == null) {
                continue;
            }

            if (builder.length() > 0) {
                builder.append("\n\n");
            }

            JSONArray steps = task.optJSONArray("steps");
            int currentStep = task.optInt("currentStep", 0);
            builder.append("• ")
                    .append(task.optString("goal", "Task"))
                    .append("\n  ")
                    .append(task.optString("status", "unknown"));

            if (steps != null && steps.length() > 0) {
                builder.append(" · bước ")
                        .append(Math.min(
                                currentStep + 1,
                                steps.length()))
                        .append("/")
                        .append(steps.length());
            }
        }

        if (tasks.length() > limit) {
            builder.append("\n\n… còn ")
                    .append(tasks.length() - limit)
                    .append(" task.");
        }

        return builder.toString();
    }

    private void disconnectLocal() {
        tokenStore.clear();
        conversation.clear();
        transcript.setLength(0);
        chatTranscript.setText("Chưa có tin nhắn.");
        tasksText.setText("Chưa tải tasks.");
        hydratePairingFields();
        updateMode();
        Toast.makeText(
                this,
                "Đã xóa token khỏi điện thoại. Thu hồi server-side trong giao diện desktop nếu cần.",
                Toast.LENGTH_LONG).show();
    }

    private void setBusy(boolean busy) {
        pairButton.setEnabled(!busy);
        sendButton.setEnabled(!busy);
        refreshButton.setEnabled(!busy);
    }

    private void clearError() {
        errorText.setText("");
    }

    private void showError(String message) {
        errorText.setText(message);
    }

    private static String safeMessage(Exception exception) {
        String message = exception.getMessage();
        return message == null || message.trim().isEmpty()
                ? exception.getClass().getSimpleName()
                : message;
    }
}

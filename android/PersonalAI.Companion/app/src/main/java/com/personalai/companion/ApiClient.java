package com.personalai.companion;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

final class ApiClient {
    static final class PairResult {
        final String token;
        final String deviceName;
        final String workspaceId;

        PairResult(String token, String deviceName, String workspaceId) {
            this.token = token;
            this.deviceName = deviceName;
            this.workspaceId = workspaceId;
        }
    }

    private final String serverUrl;

    ApiClient(String serverUrl) {
        this.serverUrl = normalizeServerUrl(serverUrl);
    }

    PairResult pair(String code, String deviceName) throws Exception {
        JSONObject body = new JSONObject();
        body.put("code", code);
        body.put("deviceName", deviceName);

        JSONObject response = request(
                "POST",
                "/api/companion/pairing/claim",
                body,
                "",
                30_000);

        JSONObject device = response.getJSONObject("device");
        return new PairResult(
                response.getString("token"),
                device.getString("name"),
                device.getString("workspaceId"));
    }

    JSONObject me(String token) throws Exception {
        return request(
                "GET",
                "/api/companion/client/me",
                null,
                token,
                20_000);
    }

    JSONObject core(String token) throws Exception {
        return request(
                "GET",
                "/api/companion/client/core",
                null,
                token,
                20_000);
    }

    JSONObject tasks(String token) throws Exception {
        return request(
                "GET",
                "/api/companion/client/tasks",
                null,
                token,
                20_000);
    }

    JSONObject chat(
            String token,
            JSONArray messages) throws Exception {
        JSONObject body = new JSONObject();
        body.put("messages", messages);
        body.put("useKnowledge", true);
        body.put("knowledgeMode", "normal");
        body.put("useMemory", true);
        body.put("useTools", false);
        body.put("useTaskContext", true);

        return request(
                "POST",
                "/api/companion/client/chat",
                body,
                token,
                120_000);
    }

    private JSONObject request(
            String method,
            String path,
            JSONObject body,
            String token,
            int timeoutMs) throws Exception {
        HttpURLConnection connection = null;
        try {
            URL url = new URL(serverUrl + path);
            connection = (HttpURLConnection) url.openConnection();
            connection.setRequestMethod(method);
            connection.setConnectTimeout(Math.min(timeoutMs, 20_000));
            connection.setReadTimeout(timeoutMs);
            connection.setRequestProperty("Accept", "application/json");

            if (token != null && !token.isEmpty()) {
                connection.setRequestProperty(
                        "Authorization",
                        "Bearer " + token);
            }

            if (body != null) {
                connection.setDoOutput(true);
                connection.setRequestProperty(
                        "Content-Type",
                        "application/json; charset=utf-8");
                byte[] payload = body.toString()
                        .getBytes(StandardCharsets.UTF_8);
                connection.getOutputStream().write(payload);
            }

            int code = connection.getResponseCode();
            InputStream stream = code >= 200 && code < 300
                    ? connection.getInputStream()
                    : connection.getErrorStream();
            String payload = readAll(stream);

            if (code < 200 || code >= 300) {
                String message = "HTTP " + code;
                if (!payload.isEmpty()) {
                    try {
                        JSONObject error = new JSONObject(payload);
                        message = error.optString("error", message);
                    } catch (Exception ignored) {
                        message = payload.length() > 300
                                ? payload.substring(0, 300)
                                : payload;
                    }
                }
                throw new IOException(message);
            }

            return payload.isEmpty()
                    ? new JSONObject()
                    : new JSONObject(payload);
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    private static String readAll(InputStream stream) throws Exception {
        if (stream == null) {
            return "";
        }

        try (BufferedReader reader = new BufferedReader(
                new InputStreamReader(
                        stream,
                        StandardCharsets.UTF_8))) {
            StringBuilder builder = new StringBuilder();
            char[] buffer = new char[4096];
            int read;
            while ((read = reader.read(buffer)) >= 0) {
                builder.append(buffer, 0, read);
                if (builder.length() > 1_000_000) {
                    throw new IOException("Response quá lớn.");
                }
            }
            return builder.toString();
        }
    }

    static String normalizeServerUrl(String value) {
        String normalized = value == null ? "" : value.trim();
        while (normalized.endsWith("/")) {
            normalized = normalized.substring(
                    0,
                    normalized.length() - 1);
        }
        return normalized;
    }
}

package com.personalai.companion;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

final class SecureTokenStore {
    private static final String PREFS = "personalai_companion";
    private static final String KEY_ALIAS = "PersonalAI.Companion.DeviceToken";
    private static final String PREF_TOKEN = "device_token";
    private static final String PREF_SERVER = "server_url";
    private static final String PREF_ALLOW_HTTP = "allow_http";
    private static final String PREF_DEVICE = "device_name";
    private static final String PREF_WORKSPACE = "workspace_id";

    private final SharedPreferences preferences;

    SecureTokenStore(Context context) {
        preferences = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    void save(
            String serverUrl,
            boolean allowHttp,
            String deviceName,
            String workspaceId,
            String token) throws Exception {
        preferences.edit()
                .putString(PREF_SERVER, serverUrl)
                .putBoolean(PREF_ALLOW_HTTP, allowHttp)
                .putString(PREF_DEVICE, deviceName)
                .putString(PREF_WORKSPACE, workspaceId)
                .putString(PREF_TOKEN, encrypt(token))
                .apply();
    }

    String getServerUrl() {
        return preferences.getString(PREF_SERVER, "");
    }

    boolean getAllowHttp() {
        return preferences.getBoolean(PREF_ALLOW_HTTP, false);
    }

    String getDeviceName() {
        return preferences.getString(PREF_DEVICE, "");
    }

    String getWorkspaceId() {
        return preferences.getString(PREF_WORKSPACE, "");
    }

    String getToken() {
        String encrypted = preferences.getString(PREF_TOKEN, "");
        if (encrypted == null || encrypted.isEmpty()) {
            return "";
        }

        try {
            return decrypt(encrypted);
        } catch (Exception ignored) {
            return "";
        }
    }

    boolean isPaired() {
        return !getServerUrl().isEmpty() && !getToken().isEmpty();
    }

    void clear() {
        preferences.edit().clear().apply();
    }

    private String encrypt(String plaintext) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey());
        byte[] ciphertext = cipher.doFinal(
                plaintext.getBytes(StandardCharsets.UTF_8));
        return Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP)
                + "."
                + Base64.encodeToString(ciphertext, Base64.NO_WRAP);
    }

    private String decrypt(String encoded) throws Exception {
        String[] parts = encoded.split("\\.", 2);
        if (parts.length != 2) {
            throw new IllegalArgumentException("Invalid encrypted token.");
        }

        byte[] iv = Base64.decode(parts[0], Base64.NO_WRAP);
        byte[] ciphertext = Base64.decode(parts[1], Base64.NO_WRAP);

        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(
                Cipher.DECRYPT_MODE,
                getOrCreateKey(),
                new GCMParameterSpec(128, iv));
        return new String(
                cipher.doFinal(ciphertext),
                StandardCharsets.UTF_8);
    }

    private SecretKey getOrCreateKey() throws Exception {
        KeyStore keyStore = KeyStore.getInstance("AndroidKeyStore");
        keyStore.load(null);

        if (keyStore.containsAlias(KEY_ALIAS)) {
            return ((KeyStore.SecretKeyEntry)
                    keyStore.getEntry(KEY_ALIAS, null)).getSecretKey();
        }

        KeyGenerator generator = KeyGenerator.getInstance(
                KeyProperties.KEY_ALGORITHM_AES,
                "AndroidKeyStore");
        generator.init(
                new KeyGenParameterSpec.Builder(
                        KEY_ALIAS,
                        KeyProperties.PURPOSE_ENCRYPT
                                | KeyProperties.PURPOSE_DECRYPT)
                        .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                        .setEncryptionPaddings(
                                KeyProperties.ENCRYPTION_PADDING_NONE)
                        .setKeySize(256)
                        .build());
        return generator.generateKey();
    }
}

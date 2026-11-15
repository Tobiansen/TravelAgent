package com.travelagent.ble;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import org.json.JSONObject;
import java.io.*;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.util.Arrays;
import java.util.Base64;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;

/** Only the gateway URL/session bearer, encrypted as one record; never an Exa API key or board-owner token. */
public final class GatewaySettingsBridge {
    private static final String ALIAS = "TravelAgentGatewaySettingsV1";
    private static final String PREFERENCES = "travelagent_gateway_settings_v1";
    private static final String ENTRY = "sealed";
    private GatewaySettingsBridge() { }
    public static synchronized String load(Context context) {
        try {
            String sealed = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE).getString(ENTRY, "");
            JSONObject result = new JSONObject();
            if (sealed == null || sealed.isEmpty()) return result.toString();
            Settings pair = Codec.open(sealed, key(false), aad(context));
            BleBridge.put(result, "url", pair.url); BleBridge.put(result, "token", pair.token); return result.toString();
        } catch (Exception unavailable) {
            JSONObject result = new JSONObject(); BleBridge.put(result, "error", "Saved gateway settings could not be read. Enter the current gateway address and session token again."); return result.toString();
        }
    }
    public static synchronized boolean save(Context context, String url, String token) {
        try {
            Validation.require(url, token);
            String sealed = Codec.seal(url, token, key(true), aad(context));
            return context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE).edit().putString(ENTRY, sealed).commit();
        } catch (Exception unavailable) { return false; }
    }
    public static synchronized boolean clear(Context context) {
        try {
            if (!context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE).edit().remove(ENTRY).commit()) return false;
            KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null);
            if (store.containsAlias(ALIAS)) store.deleteEntry(ALIAS);
            return true;
        } catch (Exception unavailable) { return false; }
    }
    private static byte[] aad(Context context) { return (context.getPackageName() + ":gateway-settings:v1").getBytes(StandardCharsets.UTF_8); }
    private static SecretKey key(boolean create) throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null);
        if (!store.containsAlias(ALIAS)) {
            if (!create) throw new GeneralSecurityException("Gateway settings key is unavailable.");
            KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setKeySize(256).setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setRandomizedEncryptionRequired(true).build());
            generator.generateKey();
        }
        SecretKey key = (SecretKey)store.getKey(ALIAS, null);
        if (key == null) throw new GeneralSecurityException("Gateway settings key is unavailable.");
        return key;
    }
    public static final class Settings {
        public final String url, token;
        private Settings(String url, String token) { this.url = url; this.token = token; }
    }
    /** Pure validation is also exercised by host tests with synthetic settings only. */
    public static final class Validation {
        private Validation() { }
        public static void require(String url, String token) throws IOException {
            if (url == null || url.isEmpty() || url.length() > 2048 || token == null || token.isEmpty() || token.length() > 4096) throw new IOException("Gateway settings are invalid.");
            for (int index = 0; index < url.length(); index++) if (Character.isISOControl(url.charAt(index)) || Character.isWhitespace(url.charAt(index))) throw new IOException("Gateway address is invalid.");
            try {
                URI endpoint = new URI(url);
                String scheme = endpoint.getScheme();
                if (!("http".equalsIgnoreCase(scheme) || "https".equalsIgnoreCase(scheme)) || endpoint.getHost() == null || endpoint.getHost().isEmpty() || endpoint.getRawUserInfo() != null || endpoint.getPort() == 0 || endpoint.getPort() > 65535)
                    throw new IOException("Gateway address is invalid.");
            } catch (java.net.URISyntaxException invalid) { throw new IOException("Gateway address is invalid."); }
            // Printable ASCII without spaces is safe for the existing Authorization: Bearer header.
            for (int index = 0; index < token.length(); index++) if (token.charAt(index) < 33 || token.charAt(index) > 126) throw new IOException("Gateway session token is invalid.");
        }
    }
    /** AES-GCM envelope shared by Android Keystore use and behavioral host tests. */
    public static final class Codec {
        private Codec() { }
        public static String seal(String url, String token, SecretKey key, byte[] aad) throws Exception {
            Validation.require(url, token);
            byte[] plaintext;
            try (ByteArrayOutputStream bytes = new ByteArrayOutputStream(); DataOutputStream data = new DataOutputStream(bytes)) {
                data.writeUTF(url); data.writeUTF(token); data.flush(); plaintext = bytes.toByteArray();
            }
            try {
                Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.ENCRYPT_MODE, key); cipher.updateAAD(aad);
                byte[] encrypted = cipher.doFinal(plaintext);
                return "1:" + Base64.getEncoder().encodeToString(cipher.getIV()) + ":" + Base64.getEncoder().encodeToString(encrypted);
            } finally { Arrays.fill(plaintext, (byte)0); }
        }
        public static Settings open(String sealed, SecretKey key, byte[] aad) throws Exception {
            if (sealed == null || sealed.length() > 24576) throw new IOException("Encrypted gateway settings are invalid.");
            String[] parts = sealed.split(":", -1);
            if (parts.length != 3 || !"1".equals(parts[0])) throw new IOException("Encrypted gateway settings are invalid.");
            byte[] iv = Base64.getDecoder().decode(parts[1]), encrypted = Base64.getDecoder().decode(parts[2]);
            if (iv.length != 12 || encrypted.length < 16 || encrypted.length > 16384) throw new IOException("Encrypted gateway settings are invalid.");
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.DECRYPT_MODE, key, new GCMParameterSpec(128, iv)); cipher.updateAAD(aad);
            byte[] plaintext = cipher.doFinal(encrypted);
            try (DataInputStream input = new DataInputStream(new ByteArrayInputStream(plaintext))) {
                String url = input.readUTF(), token = input.readUTF();
                if (input.read() != -1) throw new IOException("Encrypted gateway settings are invalid.");
                Validation.require(url, token); return new Settings(url, token);
            } finally { Arrays.fill(plaintext, (byte)0); }
        }
    }
}

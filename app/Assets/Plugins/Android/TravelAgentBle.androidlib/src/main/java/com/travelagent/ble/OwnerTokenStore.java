package com.travelagent.ble;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Tokens never enter Unity, PlayerPrefs, logs or APK resources. */
final class OwnerTokenStore {
    private static final String ALIAS = "TravelAgentOwnerTokenV1";
    private final Context context;
    OwnerTokenStore(Context context) { this.context = context.getApplicationContext(); }

    void save(String deviceId, String token) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        cipher.updateAAD(deviceId.getBytes(StandardCharsets.UTF_8));
        byte[] encrypted = cipher.doFinal(token.getBytes(StandardCharsets.UTF_8));
        String value = Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP) + ":" + Base64.encodeToString(encrypted, Base64.NO_WRAP);
        if (!context.getSharedPreferences("bag_owner_tokens", Context.MODE_PRIVATE).edit().putString(deviceId, value).commit())
            throw new IllegalStateException("Owner token could not be saved");
    }

    String load(String deviceId) throws Exception {
        String stored = context.getSharedPreferences("bag_owner_tokens", Context.MODE_PRIVATE).getString(deviceId, "");
        if (stored == null || stored.isEmpty()) return "";
        String[] parts = stored.split(":");
        if (parts.length != 2) throw new IllegalStateException("Stored owner token is invalid");
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, Base64.decode(parts[0], Base64.NO_WRAP)));
        cipher.updateAAD(deviceId.getBytes(StandardCharsets.UTF_8));
        return new String(cipher.doFinal(Base64.decode(parts[1], Base64.NO_WRAP)), StandardCharsets.UTF_8);
    }

    private SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null);
        if (!store.containsAlias(ALIAS)) {
            KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());
            generator.generateKey();
        }
        return (SecretKey)store.getKey(ALIAS, null);
    }
}

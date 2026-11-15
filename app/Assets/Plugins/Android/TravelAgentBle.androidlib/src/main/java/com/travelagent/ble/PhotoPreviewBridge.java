package com.travelagent.ble;

import android.content.Context;
import org.json.JSONObject;
import java.io.File;
import java.util.*;

/** On-demand thumbnails for saved private originals. Preview failures retain the original. */
public final class PhotoPreviewBridge {
    private static final Map<String, String> results = new LinkedHashMap<>();
    private static int running;
    private PhotoPreviewBridge() { }
    public static synchronized String start(Context context, String path) {
        if (running >= 4) return "";
        while (results.size() >= 16) {
            String remove = null; for (Map.Entry<String, String> item : results.entrySet()) if (!item.getValue().isEmpty()) { remove = item.getKey(); break; }
            if (remove == null) return "";
            results.remove(remove);
        }
        String id = UUID.randomUUID().toString(); results.put(id, "");
        running++;
        Context app = context.getApplicationContext();
        new Thread(() -> {
            String preview = "", error = "";
            try {
                File source = CaptureFiles.requirePrivateImage(new File(path), new File[] { app.getFilesDir(), app.getExternalFilesDir(null) });
                preview = PhotoPreview.create(source, app.getFilesDir()).getAbsolutePath();
            } catch (Exception failure) { error = "The original is retained, but its display preview could not be prepared."; }
            JSONObject result = new JSONObject(); BleBridge.put(result, "path", path); BleBridge.put(result, "previewPath", preview); BleBridge.put(result, "error", error);
            synchronized (PhotoPreviewBridge.class) { results.put(id, result.toString()); running--; }
        }, "TravelAgent-photo-preview").start();
        return id;
    }
    public static synchronized String poll(String id) {
        String value = results.get(id);
        if (value == null) return "";
        if (!value.isEmpty()) results.remove(id);
        return value;
    }
}

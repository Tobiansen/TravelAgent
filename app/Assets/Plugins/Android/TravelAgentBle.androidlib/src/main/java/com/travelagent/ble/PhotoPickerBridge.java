package com.travelagent.ble;

import android.app.Activity;
import android.content.Intent;
import org.json.JSONObject;
import java.util.ArrayDeque;

/** A selected document is copied into private files; no media/storage permission or upload. */
public final class PhotoPickerBridge {
    private static final ArrayDeque<String> results = new ArrayDeque<>();
    private static boolean busy;
    private PhotoPickerBridge() { }

    public static void launch(Activity activity) {
        synchronized (PhotoPickerBridge.class) {
            if (busy) { enqueue("", "A photo selection is already open.", false); return; }
            busy = true;
        }
        activity.runOnUiThread(() -> {
            try { activity.startActivity(new Intent(activity, PhotoPickerActivity.class)); }
            catch (RuntimeException exception) { complete("", "Android could not open the image picker. Please retry.", false); }
        });
    }

    public static synchronized String poll() { return results.isEmpty() ? "" : results.removeFirst(); }
    public static synchronized void complete(String path, String error, boolean cancelled) {
        complete(path, "", error, cancelled);
    }
    public static synchronized void complete(String path, String preview, String error, boolean cancelled) {
        enqueue(path, preview, error, cancelled); busy = false;
    }
    private static synchronized void enqueue(String path, String error, boolean cancelled) {
        enqueue(path, "", error, cancelled);
    }
    private static synchronized void enqueue(String path, String preview, String error, boolean cancelled) {
        JSONObject result = new JSONObject();
        BleBridge.put(result, "path", path); BleBridge.put(result, "error", error); BleBridge.put(result, "cancelled", cancelled);
        BleBridge.put(result, "previewPath", preview); BleBridge.put(result, "origin", "local imported image");
        if (results.size() >= 4) results.removeFirst();
        results.addLast(result.toString());
    }
}

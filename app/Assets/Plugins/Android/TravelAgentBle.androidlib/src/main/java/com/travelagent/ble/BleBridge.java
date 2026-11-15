package com.travelagent.ble;

import android.app.Activity;
import android.content.Intent;
import android.os.Build;
import org.json.JSONObject;
import java.lang.ref.WeakReference;

/** JNI entry point. Native callbacks only enqueue; Unity polls on its main thread. */
public final class BleBridge {
    private static final int MAX_QUEUE = 512;
    private static final LiveMotionBuffer messages = new LiveMotionBuffer(MAX_QUEUE);
    private static WeakReference<Activity> activity = new WeakReference<>(null);

    private BleBridge() { }
    public static void initialize(Activity currentActivity) { activity = new WeakReference<>(currentActivity); }
    public static synchronized void beginSession() { messages.clear(); }
    public static void scan() { dispatch("scan", ""); }
    public static void connect(String address) { dispatch("connect", address); }
    public static void disconnect() { dispatch("disconnect", ""); }
    public static void send(String command) { dispatch("send", command); }
    public static void claimOwnership() { dispatch("claim", ""); }

    private static void dispatch(String action, String value) {
        Activity current = activity.get();
        if (current == null) { notice("Android activity is unavailable. Reopen the app."); return; }
        current.runOnUiThread(() -> {
            try {
                Intent intent = new Intent(current, BagBleService.class);
                intent.setAction(action);
                intent.putExtra("value", value);
                if (Build.VERSION.SDK_INT >= 26) current.startForegroundService(intent);
                else current.startService(intent);
            } catch (RuntimeException exception) {
                notice("Bluetooth service could not start. Check Nearby devices permissions and keep the app visible while connecting.");
            }
        });
    }

    public static synchronized String poll() { return messages.poll(); }
    static synchronized void clearMotion() { messages.clearMotion(); }
    static synchronized void enqueue(JSONObject value) {
        if ("motion".equals(value.optString("type"))) { messages.offerMotion(value.toString()); return; }
        if (messages.offerControl(value.toString())) {
            JSONObject gap = new JSONObject();
            put(gap, "type", "notice");
            put(gap, "status", "Phone event queue overflowed; some live events may be missing. Reconnect for current board state.");
            messages.offerControl(gap.toString());
        }
    }
    static void notice(String status) {
        JSONObject value = new JSONObject(); put(value, "type", "notice"); put(value, "status", status); enqueue(value);
    }
    static void transport(String status, boolean ready) {
        clearMotion();
        JSONObject value = new JSONObject(); put(value, "type", "transport"); put(value, "status", status); put(value, "connected", ready); enqueue(value);
    }
    static void put(JSONObject object, String key, Object value) {
        try { object.put(key, value); } catch (org.json.JSONException ignored) { }
    }
}

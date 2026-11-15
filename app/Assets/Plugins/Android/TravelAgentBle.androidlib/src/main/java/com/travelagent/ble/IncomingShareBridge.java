package com.travelagent.ble;

import android.content.Context;
import org.json.JSONObject;
import java.io.IOException;

/** Unity polls only its private inbox. No sender identity, Gmail access or content logging. */
public final class IncomingShareBridge {
    private IncomingShareBridge() { }
    public static String peek(Context context) {
        try {
            IncomingShareInbox.Record record = new IncomingShareInbox(context.getFilesDir()).peek();
            if (record == null) return "";
            JSONObject json = new JSONObject();
            BleBridge.put(json, "requestId", record.requestId); BleBridge.put(json, "text", record.text);
            BleBridge.put(json, "origin", record.origin); BleBridge.put(json, "receivedAtUtc", record.receivedAtUtc);
            return json.toString();
        } catch (IOException | RuntimeException unavailable) {
            JSONObject json = new JSONObject(); BleBridge.put(json, "error", "Shared text could not be read. It has not been imported or acknowledged. Please retry."); return json.toString();
        }
    }
    public static boolean acknowledge(Context context, String requestId) {
        try { return new IncomingShareInbox(context.getFilesDir()).acknowledge(requestId); }
        catch (IOException | RuntimeException unavailable) { return false; }
    }
}

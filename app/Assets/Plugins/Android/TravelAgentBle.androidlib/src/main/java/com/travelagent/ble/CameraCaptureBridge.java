package com.travelagent.ble;

import android.app.Activity;
import android.content.*;
import android.net.Uri;
import org.json.JSONObject;
import java.io.*;
import java.nio.file.Files;
import java.time.Instant;
import java.util.UUID;

/** Durable, single-request camera outbox. A result is retained until copied or explicitly discarded. */
public final class CameraCaptureBridge {
    private static String workerId = "";
    private CameraCaptureBridge() { }
    public static synchronized String launch(Activity activity, String context) {
        File root = activity.getFilesDir();
        try {
            if (!CaptureFiles.validContext(context)) return failure("The camera journey context is invalid.");
            CaptureJournal journal = new CaptureJournal(root);
            CaptureJournal.Record previous = journal.read();
            if (previous != null) {
                if ("cancelled".equals(previous.state) || "error".equals(previous.state)) cleanup(activity, previous);
                else {
                    JSONObject retained = result(activity, previous, true);
                    BleBridge.put(retained, "error", "Review or discard the previous camera request before taking another photo.");
                    return retained.toString();
                }
            }
            CaptureJournal.Record record = new CaptureJournal.Record();
            record.id = UUID.randomUUID().toString(); record.context = context; record.requested = Instant.now().toString();
            File pending = CaptureFiles.image(root, "pending", record.id, false);
            if (!pending.createNewFile()) throw new IOException("Camera staging collision.");
            try { journal.write(record); } catch (IOException error) { pending.delete(); throw error; }
            activity.runOnUiThread(() -> {
                try { activity.startActivity(new Intent(activity, CameraCaptureActivity.class).putExtra("requestId", record.id)); }
                catch (RuntimeException unavailable) { fail(activity, record.id, "Android could not open a camera. Choose an existing photo instead."); }
            });
            return result(activity, record, false).toString();
        } catch (Exception error) { return failure("Private camera storage is unavailable. Your existing photos are retained."); }
    }
    public static synchronized String poll(Context context, String id) {
        try {
            CaptureJournal.Record record = new CaptureJournal(context.getFilesDir()).read();
            if (record == null || !record.id.equals(id)) return failure("The camera request is no longer available. Your existing evidence is retained.");
            if ("capturing".equals(record.state) || "processing".equals(record.state)) return "";
            String json = result(context, record, false).toString();
            if ("cancelled".equals(record.state) || "error".equals(record.state)) cleanup(context, record);
            return json;
        } catch (Exception failure) { return failure("The camera result could not be recovered. Your existing evidence is retained."); }
    }
    public static synchronized String recover(Context context) {
        try {
            CaptureJournal.Record record = new CaptureJournal(context.getFilesDir()).read();
            if (record == null) return "";
            if ("processing".equals(record.state)) process(context, record.id);
            String json = result(context, record, true).toString();
            if ("cancelled".equals(record.state) || "error".equals(record.state)) cleanup(context, record);
            return json;
        } catch (Exception error) { return failure("A saved camera request could not be read. No photo was attached."); }
    }
    public static synchronized String release(Context context, String id) {
        try {
            CaptureJournal.Record record = new CaptureJournal(context.getFilesDir()).read();
            if (record == null || !record.id.equals(id)) return "";
            if (workerId.equals(id)) return "The camera preview is still being prepared. Review or discard it again in a moment.";
            cleanup(context, record); return "";
        } catch (Exception error) { return "The retained camera request could not be released. Your journey copies are unchanged."; }
    }
    static synchronized CaptureJournal.Record request(Context context, String id) throws IOException {
        CaptureJournal.Record record = new CaptureJournal(context.getFilesDir()).read();
        if (record == null || !record.id.equals(id)) throw new IOException("Unknown camera request.");
        return record;
    }
    static Uri uri(Context context, String id) { return Uri.parse("content://" + context.getPackageName() + ".camera/capture/" + id + ".jpg"); }
    static synchronized void returned(Context context, String id, boolean ok) {
        try {
            CaptureJournal.Record record = request(context, id);
            if (!"capturing".equals(record.state)) return;
            context.revokeUriPermission(uri(context, id), Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            record.returned = Instant.now().toString(); record.state = ok ? "processing" : "cancelled";
            new CaptureJournal(context.getFilesDir()).write(record);
            if (ok) process(context, id);
        } catch (Exception error) { fail(context, id, "The camera result could not be retained. Your existing photos are unchanged."); }
    }
    static synchronized void fail(Context context, String id, String message) {
        try {
            CaptureJournal.Record record = request(context, id);
            context.revokeUriPermission(uri(context, id), Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            record.state = "error"; record.error = message; record.returned = Instant.now().toString();
            new CaptureJournal(context.getFilesDir()).write(record);
        } catch (Exception ignored) { /* A released request cannot overwrite a newer journal. */ }
    }
    static synchronized void process(Context context, String id) {
        if (!workerId.isEmpty()) return;
        workerId = id; Context app = context.getApplicationContext();
        new Thread(() -> {
            try {
                CaptureJournal.Record record;
                synchronized (CameraCaptureBridge.class) { record = request(app, id); if (!"processing".equals(record.state)) return; }
                File root = app.getFilesDir();
                CaptureCompletion.Result prepared = CaptureCompletion.prepare(root, id, source -> PhotoPreview.validate(source), source -> PhotoPreview.create(source, root));
                synchronized (CameraCaptureBridge.class) {
                    record = request(app, id); record.preview = prepared.previewPath; record.previewWarning = prepared.previewWarning; record.state = "ready"; new CaptureJournal(root).write(record);
                }
            } catch (Exception error) { fail(app, id, error instanceof IOException ? error.getMessage() : "The photo could not be prepared. Your existing photos are retained."); }
            finally { synchronized (CameraCaptureBridge.class) { workerId = ""; } }
        }, "TravelAgent-camera-prepare").start();
    }
    private static JSONObject result(Context context, CaptureJournal.Record record, boolean recovered) throws IOException {
        JSONObject value = new JSONObject();
        BleBridge.put(value, "requestId", record.id); BleBridge.put(value, "context", record.context);
        BleBridge.put(value, "status", record.state); BleBridge.put(value, "origin", "local camera capture");
        BleBridge.put(value, "captureRequestedAtUtc", record.requested); BleBridge.put(value, "captureReturnedAtUtc", record.returned);
        BleBridge.put(value, "error", record.error); BleBridge.put(value, "cancelled", "cancelled".equals(record.state));
        BleBridge.put(value, "recovered", recovered); BleBridge.put(value, "pending", "capturing".equals(record.state) || "processing".equals(record.state));
        if ("ready".equals(record.state)) {
            BleBridge.put(value, "path", CaptureFiles.image(context.getFilesDir(), "originals", record.id, true).getAbsolutePath());
            String preview = "", warning = record.previewWarning;
            try { if (!record.preview.isEmpty()) preview = PhotoPreview.requirePreview(context.getFilesDir(), record.preview).getAbsolutePath(); }
            catch (IOException unavailablePreview) { warning = "Original camera photo retained. The cached preview is unavailable; retry preview preparation."; }
            BleBridge.put(value, "previewPath", preview); BleBridge.put(value, "previewWarning", warning);
        }
        return value;
    }
    private static void cleanup(Context context, CaptureJournal.Record record) throws IOException {
        context.revokeUriPermission(uri(context, record.id), Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        Files.deleteIfExists(CaptureFiles.image(context.getFilesDir(), "pending", record.id, false).toPath());
        Files.deleteIfExists(CaptureFiles.image(context.getFilesDir(), "originals", record.id, false).toPath());
        // Previews are content-addressed shared display cache entries; removing one here could race another preview request.
        new CaptureJournal(context.getFilesDir()).remove(record.id);
    }
    private static String failure(String message) { JSONObject value = new JSONObject(); BleBridge.put(value, "error", message); return value.toString(); }
}

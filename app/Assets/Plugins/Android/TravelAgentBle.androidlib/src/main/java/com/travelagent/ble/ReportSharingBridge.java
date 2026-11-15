package com.travelagent.ble;

import android.app.Activity;
import android.content.Intent;
import org.json.JSONObject;
import java.io.File;
import java.lang.ref.WeakReference;
import java.util.ArrayDeque;

/** User-initiated preparation only; destination selection and sending belong to Android/recipient UI. */
public final class ReportSharingBridge {
    private static final ArrayDeque<String> results = new ArrayDeque<>();
    private static boolean busy;
    private ReportSharingBridge() { }

    public static void share(Activity activity, String exportPath) {
        synchronized (ReportSharingBridge.class) {
            if (busy) { emit("complete", "", "A share sheet is already open. Return to it or close it first.", false); return; }
            busy = true;
        }
        WeakReference<Activity> owner = new WeakReference<>(activity);
        final android.content.Context context = activity.getApplicationContext();
        new Thread(() -> {
            try {
                File folder = ReportFiles.requireExportFolder(new File(exportPath), new File[] { context.getFilesDir(), context.getExternalFilesDir(null) });
                File archive = ReportArchive.create(folder, new File(context.getCacheDir(), "SharedReports"));
                Activity current = owner.get();
                if (current == null) { complete(archive.getAbsolutePath(), "Return to the app and share the retained export again.", false); return; }
                current.runOnUiThread(() -> {
                    if (current.isFinishing() || current.isDestroyed() || !current.hasWindowFocus()) {
                        complete(archive.getAbsolutePath(), "The ZIP is prepared. Return to the app and share again; your local export is retained.", false); return;
                    }
                    try {
                        Intent intent = new Intent(current, ReportShareActivity.class);
                        intent.putExtra("archiveName", archive.getName()); current.startActivity(intent);
                    } catch (RuntimeException exception) { complete(archive.getAbsolutePath(), "Android could not open sharing. Your local export is retained.", false); }
                });
            } catch (Exception exception) { complete("", "The report ZIP could not be prepared. Your completed local export is retained. " + safeError(exception), false); }
        }, "TravelAgent-report-zip").start();
    }
    private static String safeError(Exception exception) {
        return exception instanceof java.io.IOException ? exception.getMessage() : "Please retry from the app.";
    }
    public static synchronized String poll() { return results.isEmpty() ? "" : results.removeFirst(); }
    static synchronized void opened(String path) { emit("opened", path, "", true); }
    static synchronized void complete(String path, String error, boolean opened) { emit("complete", path, error, opened); busy = false; }
    private static synchronized void emit(String type, String path, String error, boolean opened) {
        JSONObject result = new JSONObject(); BleBridge.put(result, "type", type); BleBridge.put(result, "archivePath", path);
        BleBridge.put(result, "error", error); BleBridge.put(result, "sheetOpened", opened);
        if (results.size() >= 8) results.removeFirst(); results.addLast(result.toString());
    }
}

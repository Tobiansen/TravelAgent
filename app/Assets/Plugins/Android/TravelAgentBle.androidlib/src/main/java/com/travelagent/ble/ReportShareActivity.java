package com.travelagent.ble;

import android.app.Activity;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import java.io.File;
import java.io.IOException;

/** Private proxy isolates Android's chooser lifecycle from Unity's activity-result handlers. */
public final class ReportShareActivity extends Activity {
    private static final int SHARE = 4420;
    private String archivePath = "";
    private boolean opened;
    private boolean reported;

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        if (state != null) { archivePath = state.getString("archivePath", ""); opened = state.getBoolean("opened"); return; }
        try {
            File archive = ReportFiles.requireArchive(new File(getCacheDir(), "SharedReports"), getIntent().getStringExtra("archiveName"));
            archivePath = archive.getAbsolutePath();
            Uri uri = new Uri.Builder().scheme("content").authority(getPackageName() + ".reports").appendPath("reports").appendPath(archive.getName()).build();
            Intent send = new Intent(Intent.ACTION_SEND);
            send.setType("application/zip"); send.putExtra(Intent.EXTRA_STREAM, uri);
            send.putExtra(Intent.EXTRA_TITLE, "TravelAgent evidence report");
            send.setClipData(ClipData.newUri(getContentResolver(), "Evidence report ZIP", uri));
            send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            Intent chooser = Intent.createChooser(send, "Share evidence report ZIP");
            chooser.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            startActivityForResult(chooser, SHARE);
            opened = true; ReportSharingBridge.opened(archivePath);
        } catch (IOException | RuntimeException exception) {
            finishShare("Android sharing is unavailable. Your local export is retained.", false);
        }
    }
    @Override protected void onSaveInstanceState(Bundle out) { out.putString("archivePath", archivePath); out.putBoolean("opened", opened); super.onSaveInstanceState(out); }
    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != SHARE) return;
        // A chooser result does not establish delivery or distinguish every recipient/cancellation path.
        finishShare("", opened);
    }
    private void finishShare(String error, boolean sheetOpened) {
        if (!reported) { reported = true; ReportSharingBridge.complete(archivePath, error, sheetOpened); }
        finish();
    }
    @Override protected void onDestroy() {
        if (isFinishing() && !reported) { reported = true; ReportSharingBridge.complete(archivePath, "", opened); }
        super.onDestroy();
    }
}

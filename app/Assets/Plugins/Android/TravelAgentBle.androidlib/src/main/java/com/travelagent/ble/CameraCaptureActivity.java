package com.travelagent.ble;

import android.app.Activity;
import android.content.*;
import android.net.Uri;
import android.os.Bundle;
import android.provider.MediaStore;

/** Private proxy lets Unity keep its own Activity callbacks. No CAMERA/media permission is needed. */
public final class CameraCaptureActivity extends Activity {
    private static final int REQUEST_CAPTURE = 4411;
    private String requestId = "";
    @Override protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        requestId = savedInstanceState == null ? getIntent().getStringExtra("requestId") : savedInstanceState.getString("requestId", "");
        try {
            CaptureJournal.Record record = CameraCaptureBridge.request(this, requestId);
            if ("processing".equals(record.state)) { CameraCaptureBridge.process(this, requestId); finish(); return; }
            if (!"capturing".equals(record.state)) { finish(); return; }
            if (savedInstanceState != null) return; // Android will redeliver the outstanding result; never take a second photo.
            Uri output = CameraCaptureBridge.uri(this, requestId);
            Intent camera = new Intent(MediaStore.ACTION_IMAGE_CAPTURE);
            camera.putExtra(MediaStore.EXTRA_OUTPUT, output);
            camera.setClipData(ClipData.newRawUri("TravelAgent camera output", output));
            camera.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            startActivityForResult(camera, REQUEST_CAPTURE);
        } catch (RuntimeException | java.io.IOException unavailable) {
            CameraCaptureBridge.fail(this, requestId, "No compatible camera could open. Choose an existing photo instead."); finish();
        }
    }
    @Override protected void onSaveInstanceState(Bundle state) { state.putString("requestId", requestId); super.onSaveInstanceState(state); }
    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_CAPTURE) return;
        // Full-resolution output is in our own URI; camera applications commonly return a null Intent here.
        CameraCaptureBridge.returned(this, requestId, resultCode == RESULT_OK); finish();
    }
}

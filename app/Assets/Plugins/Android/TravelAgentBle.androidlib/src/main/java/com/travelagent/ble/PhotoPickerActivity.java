package com.travelagent.ble;

import android.app.Activity;
import android.content.Intent;
import android.graphics.BitmapFactory;
import android.net.Uri;
import android.os.Bundle;
import java.io.*;
import java.util.UUID;

/** Private translucent proxy preserves Unity's own Activity and result handlers. */
public final class PhotoPickerActivity extends Activity {
    private static final int REQUEST_IMAGE = 4410;
    private static final long MAX_BYTES = 20L * 1024 * 1024;

    @Override protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (savedInstanceState != null) return;
        Intent picker = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        picker.addCategory(Intent.CATEGORY_OPENABLE);
        picker.setType("image/*");
        picker.putExtra(Intent.EXTRA_MIME_TYPES, new String[] { "image/jpeg", "image/png" });
        picker.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try { startActivityForResult(picker, REQUEST_IMAGE); }
        catch (RuntimeException exception) { PhotoPickerBridge.complete("", "No compatible Android image picker is available.", false); finish(); }
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_IMAGE) return;
        if (resultCode != RESULT_OK || data == null || data.getData() == null) {
            PhotoPickerBridge.complete("", "", true); finish(); return;
        }
        final Uri selected = data.getData();
        new Thread(() -> {
            File target = null;
            try {
                File directory = new File(getFilesDir(), "EvidenceInbox");
                if (!directory.isDirectory() && !directory.mkdirs()) throw new IOException("private directory");
                target = new File(directory, "selected-" + UUID.randomUUID() + ".part");
                try (InputStream input = getContentResolver().openInputStream(selected);
                     OutputStream output = new FileOutputStream(target)) {
                    if (input == null) throw new IOException("document unavailable");
                    byte[] buffer = new byte[32768]; long total = 0; int count;
                    while ((count = input.read(buffer)) != -1) {
                        total += count;
                        if (total > MAX_BYTES) throw new ImageRejected("Choose an image smaller than 20 MB.");
                        output.write(buffer, 0, count);
                    }
                    if (total < 8) throw new ImageRejected("The selected file is not a valid PNG or JPEG image.");
                }
                byte[] header = new byte[8];
                try (InputStream verify = new FileInputStream(target)) {
                    if (verify.read(header) != 8) throw new ImageRejected("The selected image could not be read.");
                }
                boolean jpeg = (header[0] & 255) == 255 && (header[1] & 255) == 216 && (header[2] & 255) == 255;
                boolean png = (header[0] & 255) == 137 && header[1] == 80 && header[2] == 78 && header[3] == 71 && header[4] == 13 && header[5] == 10 && header[6] == 26 && header[7] == 10;
                if (!jpeg && !png) throw new ImageRejected("Choose a PNG or JPEG image. Other image formats are not supported yet.");
                BitmapFactory.Options dimensions = new BitmapFactory.Options(); dimensions.inJustDecodeBounds = true;
                BitmapFactory.decodeFile(target.getAbsolutePath(), dimensions);
                if (dimensions.outWidth <= 0 || dimensions.outHeight <= 0) throw new ImageRejected("The selected image is damaged or cannot be decoded.");
                File image = new File(directory, target.getName().replace(".part", png ? ".png" : ".jpg"));
                if (!target.renameTo(image)) throw new IOException("private file commit");
                target = null;
                File preview = PhotoPreview.create(image, getFilesDir());
                PhotoPickerBridge.complete(image.getAbsolutePath(), preview.getAbsolutePath(), "", false);
            } catch (ImageRejected rejected) { PhotoPickerBridge.complete("", rejected.getMessage(), false); }
            catch (Exception exception) { PhotoPickerBridge.complete("", "The selected image could not be copied into private app storage. Please retry.", false); }
            finally {
                if (target != null && target.exists()) target.delete();
                runOnUiThread(this::finish);
            }
        }, "TravelAgent-photo-copy").start();
    }

    private static final class ImageRejected extends Exception { ImageRejected(String message) { super(message); } }
}

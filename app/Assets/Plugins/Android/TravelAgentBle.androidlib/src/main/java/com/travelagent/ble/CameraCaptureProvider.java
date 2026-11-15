package com.travelagent.ble;

import android.content.*;
import android.content.pm.ProviderInfo;
import android.database.*;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.*;
import java.util.List;

/** The selected camera may write exactly the current pending file via a temporary URI grant. */
public final class CameraCaptureProvider extends ContentProvider {
    @Override public void attachInfo(Context context, ProviderInfo info) {
        if (info.exported || !info.grantUriPermissions || !info.authority.equals(context.getPackageName() + ".camera")) throw new SecurityException("Camera provider must be private with URI grants.");
        super.attachInfo(context, info);
    }
    @Override public boolean onCreate() { return true; }
    private File resolve(Uri uri) throws FileNotFoundException {
        List<String> parts = uri.getPathSegments();
        if (!"content".equals(uri.getScheme()) || !(getContext().getPackageName() + ".camera").equals(uri.getAuthority()) || uri.getQuery() != null || uri.getFragment() != null || parts.size() != 2 || !"capture".equals(parts.get(0)) || !parts.get(1).endsWith(".jpg")) throw new FileNotFoundException("Unknown camera URI.");
        String id = parts.get(1).substring(0, parts.get(1).length() - 4);
        try {
            CaptureJournal.Record record = new CaptureJournal(getContext().getFilesDir()).read();
            if (record == null || !record.id.equals(id) || !"capturing".equals(record.state)) throw new IOException("Camera request is no longer writable.");
            return CaptureFiles.image(getContext().getFilesDir(), "pending", id, true);
        } catch (IOException failure) { throw new FileNotFoundException("Camera output unavailable."); }
    }
    @Override public String getType(Uri uri) { try { resolve(uri); return "image/jpeg"; } catch (FileNotFoundException unavailable) { return null; } }
    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        int access;
        if ("r".equals(mode)) access = ParcelFileDescriptor.MODE_READ_ONLY;
        else if ("w".equals(mode) || "wt".equals(mode)) access = ParcelFileDescriptor.MODE_WRITE_ONLY | ParcelFileDescriptor.MODE_TRUNCATE;
        else if ("rw".equals(mode) || "rwt".equals(mode)) access = ParcelFileDescriptor.MODE_READ_WRITE | ("rwt".equals(mode) ? ParcelFileDescriptor.MODE_TRUNCATE : 0);
        else throw new FileNotFoundException("Unsupported camera file mode.");
        return ParcelFileDescriptor.open(resolve(uri), access);
    }
    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] args, String sort) {
        try {
            File file = resolve(uri);
            MatrixCursor result = new MatrixCursor(new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE });
            result.addRow(new Object[] { "TravelAgent-capture.jpg", file.length() }); return result;
        } catch (FileNotFoundException unavailable) { return null; }
    }
    @Override public Uri insert(Uri uri, ContentValues values) { throw new SecurityException("Camera URI cannot create arbitrary files."); }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) { throw new SecurityException("Camera URI cannot update metadata."); }
    @Override public int delete(Uri uri, String selection, String[] args) { throw new SecurityException("Camera URI cannot delete files."); }
}

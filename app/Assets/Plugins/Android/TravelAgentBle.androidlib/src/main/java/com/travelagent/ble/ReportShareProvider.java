package com.travelagent.ble;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.content.pm.ProviderInfo;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

/** Only opaque ZIP names in our share-cache directory are readable, through per-URI grants. */
public final class ReportShareProvider extends ContentProvider {
    @Override public void attachInfo(Context context, ProviderInfo info) {
        if (info.exported || !info.grantUriPermissions || !info.authority.equals(context.getPackageName() + ".reports"))
            throw new SecurityException("Report provider must be private with URI grants.");
        super.attachInfo(context, info);
    }
    @Override public boolean onCreate() { return true; }

    private File resolve(Uri uri) throws FileNotFoundException {
        List<String> segments = uri.getPathSegments();
        if (!"content".equals(uri.getScheme()) || !(getContext().getPackageName() + ".reports").equals(uri.getAuthority()) ||
            uri.getQuery() != null || uri.getFragment() != null || segments.size() != 2 || !"reports".equals(segments.get(0)))
            throw new FileNotFoundException("Unknown report URI.");
        try { return ReportFiles.requireArchive(new File(getContext().getCacheDir(), "SharedReports"), segments.get(1)); }
        catch (IOException exception) { throw new FileNotFoundException("Report archive unavailable."); }
    }

    @Override public String getType(Uri uri) { try { resolve(uri); return "application/zip"; } catch (FileNotFoundException exception) { return null; } }
    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (!"r".equals(mode)) throw new FileNotFoundException("Reports are read-only.");
        return ParcelFileDescriptor.open(resolve(uri), ParcelFileDescriptor.MODE_READ_ONLY);
    }
    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        final File file;
        try { file = resolve(uri); } catch (FileNotFoundException exception) { return null; }
        String[] requested = projection == null ? new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE } : projection;
        ArrayList<String> columns = new ArrayList<>(); ArrayList<Object> values = new ArrayList<>();
        for (String column : requested) {
            if (OpenableColumns.DISPLAY_NAME.equals(column)) { columns.add(column); values.add("TravelAgent-evidence-report.zip"); }
            else if (OpenableColumns.SIZE.equals(column)) { columns.add(column); values.add(file.length()); }
        }
        MatrixCursor result = new MatrixCursor(columns.toArray(new String[0])); result.addRow(values.toArray()); return result;
    }
    @Override public Uri insert(Uri uri, ContentValues values) { throw new SecurityException("Reports are read-only."); }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) { throw new SecurityException("Reports are read-only."); }
    @Override public int delete(Uri uri, String selection, String[] args) { throw new SecurityException("Reports are read-only."); }
}

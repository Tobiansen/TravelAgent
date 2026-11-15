package com.travelagent.ble;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Matrix;
import android.media.ExifInterface;
import java.io.*;
import java.nio.file.Files;
import java.security.MessageDigest;
import java.util.*;

/** Bounded display derivatives only. Never decodes, rewrites or rotates an original in place. */
public final class PhotoPreview {
    private PhotoPreview() { }
    public static BitmapFactory.Options validate(File original) throws IOException {
        CaptureFiles.imageFormat(original);
        BitmapFactory.Options bounds = new BitmapFactory.Options(); bounds.inJustDecodeBounds = true;
        BitmapFactory.decodeFile(original.getAbsolutePath(), bounds);
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0 || bounds.outWidth > 100000 || bounds.outHeight > 100000)
            throw new IOException("The image is damaged or its dimensions are unsupported.");
        return bounds;
    }
    public static File create(File original, File filesRoot) throws IOException {
        BitmapFactory.Options bounds = validate(original);
        File directory = directory(filesRoot);
        File target = new File(directory, hash(original) + ".jpg");
        if (validCachedBitmap(target)) return target;
        Bitmap decoded = null, oriented = null;
        File partial = new File(directory, UUID.randomUUID() + ".partial");
        try {
            BitmapFactory.Options options = new BitmapFactory.Options();
            options.inSampleSize = ImageGeometry.sampleSize(bounds.outWidth, bounds.outHeight, ImageGeometry.PREVIEW_EDGE);
            options.inPreferredConfig = Bitmap.Config.ARGB_8888;
            decoded = BitmapFactory.decodeFile(original.getAbsolutePath(), options);
            if (decoded == null) throw new IOException("The image could not be decoded for display.");
            int orientation = 1;
            try { orientation = new ExifInterface(original.getAbsolutePath()).getAttributeInt(ExifInterface.TAG_ORIENTATION, 1); }
            catch (IOException ignored) { /* PNGs without EXIF are displayed in their pixel orientation. */ }
            Matrix matrix = new Matrix(); matrix.setValues(ImageGeometry.orientationMatrix(orientation));
            oriented = Bitmap.createBitmap(decoded, 0, 0, decoded.getWidth(), decoded.getHeight(), matrix, true);
            if (Math.max(oriented.getWidth(), oriented.getHeight()) > ImageGeometry.PREVIEW_EDGE) throw new IOException("Preview dimensions exceeded the display bound.");
            try (FileOutputStream output = new FileOutputStream(partial)) {
                if (!oriented.compress(Bitmap.CompressFormat.JPEG, 88, output)) throw new IOException("Preview could not be saved.");
                output.getFD().sync();
            }
            if (Files.isSymbolicLink(target.toPath())) throw new IOException("Invalid preview destination.");
            Files.move(partial.toPath(), target.toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING);
            prune(directory, target);
            return target;
        } catch (OutOfMemoryError exhausted) { throw new IOException("The image could not be prepared within the phone's available memory."); }
        finally {
            if (oriented != null && oriented != decoded) oriented.recycle();
            if (decoded != null) decoded.recycle();
            Files.deleteIfExists(partial.toPath());
        }
    }
    public static File requirePreview(File filesRoot, String path) throws IOException {
        File file = new File(path), directory = directory(filesRoot);
        if (!file.getName().matches("[a-f0-9]{64}\\.jpg") || !file.getCanonicalFile().getParentFile().equals(directory) || Files.isSymbolicLink(file.toPath()) || !file.isFile())
            throw new IOException("Private preview unavailable.");
        if (!validCachedBitmap(file)) throw new IOException("Preview is damaged or exceeds the display bound.");
        return file.getCanonicalFile();
    }
    private static boolean validCachedBitmap(File file) {
        if (!file.isFile() || file.length() < 8 || file.length() > 4L * 1024 * 1024 || Files.isSymbolicLink(file.toPath())) return false;
        Bitmap decoded = null;
        try {
            BitmapFactory.Options bounds = validate(file);
            if (Math.max(bounds.outWidth, bounds.outHeight) > ImageGeometry.PREVIEW_EDGE) return false;
            decoded = BitmapFactory.decodeFile(file.getAbsolutePath());
            return decoded != null && decoded.getWidth() > 0 && decoded.getHeight() > 0 && Math.max(decoded.getWidth(), decoded.getHeight()) <= ImageGeometry.PREVIEW_EDGE;
        } catch (IOException | RuntimeException | OutOfMemoryError damaged) { return false; }
        finally { if (decoded != null) decoded.recycle(); }
    }
    private static File directory(File filesRoot) throws IOException {
        File directory = new File(filesRoot.getCanonicalFile(), "PhotoPreviews");
        if (Files.isSymbolicLink(directory.toPath())) throw new IOException("Invalid preview directory.");
        if (!directory.isDirectory() && !directory.mkdirs()) throw new IOException("Preview storage unavailable.");
        return directory.getCanonicalFile();
    }
    private static String hash(File file) throws IOException {
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            try (InputStream input = new FileInputStream(file)) { byte[] buffer = new byte[32768]; int count; while ((count = input.read(buffer)) != -1) digest.update(buffer, 0, count); }
            StringBuilder value = new StringBuilder(); for (byte item : digest.digest()) value.append(String.format(Locale.ROOT, "%02x", item & 255)); return value.toString();
        } catch (java.security.NoSuchAlgorithmException impossible) { throw new IOException("Image hash unavailable."); }
    }
    private static void prune(File directory, File retained) {
        File[] files = directory.listFiles((parent, name) -> name.matches("[a-f0-9]{64}\\.jpg"));
        if (files == null || files.length <= 128) return;
        Arrays.sort(files, Comparator.comparingLong(File::lastModified));
        for (int index = 0; index < files.length - 128; index++) if (!files[index].equals(retained)) files[index].delete();
    }
}

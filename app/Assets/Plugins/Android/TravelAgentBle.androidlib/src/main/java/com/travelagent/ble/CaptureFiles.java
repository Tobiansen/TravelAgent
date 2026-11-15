package com.travelagent.ble;

import java.io.*;
import java.nio.file.Files;

/** Exact camera-request paths. No caller-supplied absolute paths or general file-provider roots. */
public final class CaptureFiles {
    public static final long MAX_BYTES = 20L * 1024 * 1024;
    private CaptureFiles() { }
    public static boolean validId(String id) { return id != null && id.matches("[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}"); }
    public static boolean validContext(String context) { return context != null && context.matches("(demo|real):[A-Za-z0-9_-]{1,80}:(before|after)"); }
    public static File directory(File filesRoot, String leaf) throws IOException {
        if (!leaf.matches("pending|originals")) throw new IOException("Invalid camera directory.");
        File base = new File(filesRoot.getCanonicalFile(), "CameraCapture");
        File directory = new File(base, leaf);
        if (Files.isSymbolicLink(base.toPath()) || Files.isSymbolicLink(directory.toPath())) throw new IOException("Invalid camera directory.");
        if (!directory.isDirectory() && !directory.mkdirs()) throw new IOException("Private camera storage unavailable.");
        if (!base.getCanonicalFile().equals(directory.getCanonicalFile().getParentFile())) throw new IOException("Invalid camera directory.");
        return directory.getCanonicalFile();
    }
    public static File image(File root, String leaf, String id, boolean mustExist) throws IOException {
        if (!validId(id)) throw new IOException("Invalid camera request.");
        File directory = directory(root, leaf);
        File file = new File(directory, id + ".jpg");
        if (Files.isSymbolicLink(file.toPath()) || !directory.equals(file.getCanonicalFile().getParentFile()) || (mustExist && !file.isFile()))
            throw new IOException("Camera image unavailable.");
        return file;
    }
    public static String imageFormat(File file) throws IOException {
        if (!file.isFile() || file.length() < 8 || file.length() > MAX_BYTES) throw new IOException("Choose a PNG or JPEG image smaller than 20 MB.");
        byte[] header = new byte[8];
        try (DataInputStream input = new DataInputStream(new FileInputStream(file))) { input.readFully(header); }
        if ((header[0] & 255) == 255 && (header[1] & 255) == 216 && (header[2] & 255) == 255) return "jpeg";
        if ((header[0] & 255) == 137 && header[1] == 80 && header[2] == 78 && header[3] == 71 && header[4] == 13 && header[5] == 10 && header[6] == 26 && header[7] == 10) return "png";
        throw new IOException("Choose a PNG or JPEG image.");
    }
    public static File requirePrivateImage(File candidate, File[] roots) throws IOException {
        File canonical = candidate.getCanonicalFile();
        if (Files.isSymbolicLink(candidate.toPath()) || !canonical.isFile()) throw new IOException("Private image unavailable.");
        for (File root : roots) {
            if (root == null) continue;
            for (String folder : new String[] { "Demo/Images", "Real/Images", "EvidenceInbox", "CameraCapture/originals" }) {
                File allowed = new File(root.getCanonicalFile(), folder).getCanonicalFile();
                if (canonical.getParentFile().equals(allowed)) { imageFormat(canonical); return canonical; }
            }
        }
        throw new IOException("Preview source must be an app-private original image.");
    }
}

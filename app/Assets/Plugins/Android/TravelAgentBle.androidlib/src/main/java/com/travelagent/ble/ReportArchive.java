package com.travelagent.ble;

import java.io.*;
import java.nio.file.Files;
import java.util.Arrays;
import java.util.Comparator;
import java.util.UUID;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

/** Copies completed flat exports unchanged. Never modifies or deletes the original export. */
public final class ReportArchive {
    public static final int MAX_FILES = 128;
    public static final long MAX_BYTES = 256L * 1024 * 1024;
    private static final long CACHE_LIMIT = 512L * 1024 * 1024;
    private static final long CACHE_AGE = 24L * 60 * 60 * 1000;
    private ReportArchive() { }

    public static File create(File exportFolder, File cacheDirectory) throws IOException {
        File folder = exportFolder.getCanonicalFile();
        if (cacheDirectory.getCanonicalFile().toPath().startsWith(folder.toPath()) || Files.isSymbolicLink(cacheDirectory.toPath()))
            throw new IOException("Share cache must be separate from the original export.");
        File[] files = folder.listFiles();
        if (files == null || files.length == 0 || files.length > MAX_FILES) throw new IOException("Share supports up to 128 exported files.");
        Arrays.sort(files, Comparator.comparing(File::getName));
        long expected = 0;
        for (File file : files) {
            if (!file.isFile() || Files.isSymbolicLink(file.toPath()) || !folder.equals(file.getCanonicalFile().getParentFile()))
                throw new IOException("The export contains an unsupported nested or linked file.");
            if (!file.getName().matches("[A-Za-z0-9][A-Za-z0-9._-]{0,127}")) throw new IOException("An exported filename is not supported for portable sharing.");
            if (file.length() > MAX_BYTES - expected) throw new IOException("This export exceeds the 256 MB sharing limit. The local export is retained.");
            expected += file.length();
        }
        if (!cacheDirectory.isDirectory() && !cacheDirectory.mkdirs()) throw new IOException("Share cache could not be created.");
        long existing = 0; long now = System.currentTimeMillis();
        File[] cached = cacheDirectory.listFiles();
        if (cached == null) throw new IOException("Share cache is unavailable.");
        for (File file : cached) {
            if (!file.isFile() || Files.isSymbolicLink(file.toPath())) continue;
            if (now - file.lastModified() > CACHE_AGE && file.delete()) continue;
            existing += file.length();
        }
        if (existing + expected > CACHE_LIMIT) throw new IOException("Share cache is full. Your local export is retained; free app cache space and retry.");
        String name = UUID.randomUUID() + ".zip";
        File temporary = new File(cacheDirectory, name + ".partial");
        File archive = new File(cacheDirectory, name);
        boolean committed = false;
        try {
            long copied = 0;
            try (ZipOutputStream output = new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(temporary)))) {
                byte[] buffer = new byte[32768];
                for (File file : files) {
                    if (!folder.equals(file.getCanonicalFile().getParentFile()) || Files.isSymbolicLink(file.toPath())) throw new IOException("Export contents changed during sharing.");
                    ZipEntry entry = new ZipEntry(file.getName()); entry.setTime(file.lastModified()); output.putNextEntry(entry);
                    try (InputStream input = new BufferedInputStream(new FileInputStream(file))) {
                        int size;
                        while ((size = input.read(buffer)) != -1) {
                            copied += size;
                            if (copied > MAX_BYTES) throw new IOException("Export grew beyond the sharing limit. Local evidence is retained.");
                            output.write(buffer, 0, size);
                        }
                    }
                    output.closeEntry();
                }
            }
            if (!temporary.renameTo(archive)) throw new IOException("Share archive could not be finalized.");
            committed = true; return archive;
        } finally { if (!committed) temporary.delete(); }
    }
}

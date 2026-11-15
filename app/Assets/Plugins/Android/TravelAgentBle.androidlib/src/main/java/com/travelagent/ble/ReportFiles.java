package com.travelagent.ble;

import java.io.File;
import java.io.IOException;
import java.nio.file.Files;

/** File boundaries shared by the archive builder and read-only provider; no Android dependencies. */
public final class ReportFiles {
    private ReportFiles() { }

    public static File requireExportFolder(File candidate, File[] privateRoots) throws IOException {
        File folder = candidate.getCanonicalFile();
        if (!folder.isDirectory() || !folder.getName().matches("[0-9]{8}-[0-9]{6}-[0-9]{3}")) throw new IOException("Choose a completed evidence export.");
        boolean allowed = false;
        for (File root : privateRoots) {
            if (root == null) continue;
            for (String mode : new String[] { "Demo", "Real" }) {
                File exports = new File(new File(root, mode), "Exports").getCanonicalFile();
                if (exports.equals(folder.getParentFile())) allowed = true;
            }
        }
        if (!allowed || Files.isSymbolicLink(candidate.toPath())) throw new IOException("The selected folder is not a private evidence export.");
        if (!new File(folder, "report.html").isFile() || !new File(folder, "journey.json").isFile() || !new File(folder, "evidence-files.json").isFile())
            throw new IOException("The evidence export is incomplete. Export again before sharing.");
        return folder;
    }

    public static File requireArchive(File cacheDirectory, String name) throws IOException {
        if (name == null || !name.matches("[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}\\.zip"))
            throw new IOException("Unknown report archive.");
        File file = new File(cacheDirectory, name);
        File canonical = file.getCanonicalFile();
        if (!cacheDirectory.getCanonicalFile().equals(canonical.getParentFile()) || !canonical.isFile() || Files.isSymbolicLink(file.toPath()))
            throw new IOException("Report archive unavailable.");
        return canonical;
    }
}

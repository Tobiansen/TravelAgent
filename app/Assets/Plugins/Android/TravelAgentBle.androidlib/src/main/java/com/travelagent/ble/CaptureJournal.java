package com.travelagent.ble;

import java.io.*;
import java.nio.file.*;
import java.util.Properties;
import java.util.UUID;

/** One durable request/result. File paths are derived from its validated opaque ID. */
public final class CaptureJournal {
    public static final class Record {
        public String id = "", context = "", state = "capturing", requested = "", returned = "", error = "", preview = "", previewWarning = "";
    }
    private final File file;
    public CaptureJournal(File filesRoot) throws IOException {
        File base = CaptureFiles.directory(filesRoot, "pending").getParentFile();
        file = new File(base, "request.properties");
        if (Files.isSymbolicLink(file.toPath())) throw new IOException("Invalid camera journal.");
    }
    public Record read() throws IOException {
        if (!file.exists()) return null;
        if (file.length() > 16384) throw new IOException("Invalid camera journal.");
        Properties values = new Properties();
        try (InputStream input = new FileInputStream(file)) { values.load(input); }
        Record record = new Record();
        record.id = values.getProperty("id", ""); record.context = values.getProperty("context", ""); record.state = values.getProperty("state", "");
        record.requested = values.getProperty("requested", ""); record.returned = values.getProperty("returned", "");
        record.error = values.getProperty("error", ""); record.preview = values.getProperty("preview", ""); record.previewWarning = values.getProperty("previewWarning", "");
        validate(record); return record;
    }
    public void write(Record record) throws IOException {
        validate(record);
        Properties values = new Properties();
        values.setProperty("id", record.id); values.setProperty("context", record.context); values.setProperty("state", record.state);
        values.setProperty("requested", record.requested); values.setProperty("returned", record.returned);
        values.setProperty("error", record.error); values.setProperty("preview", record.preview); values.setProperty("previewWarning", record.previewWarning);
        File temporary = new File(file.getParentFile(), "journal-" + UUID.randomUUID() + ".partial");
        try {
            try (FileOutputStream output = new FileOutputStream(temporary)) { values.store(output, "TravelAgent camera request"); output.getFD().sync(); }
            try { Files.move(temporary.toPath(), file.toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING); }
            catch (AtomicMoveNotSupportedException exception) { Files.move(temporary.toPath(), file.toPath(), StandardCopyOption.REPLACE_EXISTING); }
        } finally { Files.deleteIfExists(temporary.toPath()); }
    }
    public boolean remove(String id) throws IOException {
        Record record = read();
        if (record == null) return true;
        if (!record.id.equals(id)) return false;
        return Files.deleteIfExists(file.toPath());
    }
    private static void validate(Record record) throws IOException {
        if (!CaptureFiles.validId(record.id) || !CaptureFiles.validContext(record.context) || !record.state.matches("capturing|processing|ready|cancelled|error") || record.preview.length() > 1024 || record.error.length() > 1024 || record.previewWarning.length() > 1024)
            throw new IOException("Invalid camera request record.");
    }
}

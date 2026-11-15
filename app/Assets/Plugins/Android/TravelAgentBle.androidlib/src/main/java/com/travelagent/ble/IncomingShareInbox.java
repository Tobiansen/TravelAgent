package com.travelagent.ble;

import java.io.*;
import java.nio.file.*;
import java.time.Instant;
import java.util.UUID;

/** Single durable private slot. Peek is repeatable and new shares never replace pending user data. */
public final class IncomingShareInbox {
    public static final class Record {
        public final String requestId, text, receivedAtUtc;
        public final String origin = IncomingShareText.ORIGIN;
        private Record(String id, String text, String time) { requestId = id; this.text = text; receivedAtUtc = time; }
    }
    private static final int MAGIC = 0x54534931;
    private final File directory, slot;
    public IncomingShareInbox(File privateFiles) throws IOException {
        directory = new File(privateFiles.getCanonicalFile(), "IncomingShares");
        slot = new File(directory, "pending.bin");
        if (Files.isSymbolicLink(directory.toPath()) || Files.isSymbolicLink(slot.toPath())) throw new IOException("Private shared-text storage is unavailable.");
        if (!directory.isDirectory() && !directory.mkdirs() && !directory.isDirectory()) throw new IOException("Private shared-text storage is unavailable.");
    }
    public Record accept(String text, String receivedAtUtc) throws IOException {
        IncomingShareText.require(text); requireTime(receivedAtUtc);
        synchronized (IncomingShareInbox.class) {
            File[] abandoned = directory.listFiles((parent, name) -> name.matches("incoming-[a-f0-9-]{36}\\.partial"));
            if (abandoned != null) for (File partial : abandoned) Files.deleteIfExists(partial.toPath());
            if (slot.exists()) return null;
            Record record = new Record(UUID.randomUUID().toString(), text, receivedAtUtc);
            File partial = new File(directory, "incoming-" + record.requestId + ".partial");
            try {
                try (FileOutputStream output = new FileOutputStream(partial); DataOutputStream data = new DataOutputStream(output)) {
                    data.writeInt(MAGIC); data.writeUTF(record.requestId); data.writeUTF(record.receivedAtUtc);
                    byte[] bytes = IncomingShareText.encode(record.text); data.writeInt(bytes.length); data.write(bytes); data.flush(); output.getFD().sync();
                }
                try { Files.move(partial.toPath(), slot.toPath(), StandardCopyOption.ATOMIC_MOVE); }
                catch (AtomicMoveNotSupportedException unsupported) { Files.move(partial.toPath(), slot.toPath()); }
                return record;
            } finally { Files.deleteIfExists(partial.toPath()); }
        }
    }
    public Record peek() throws IOException {
        synchronized (IncomingShareInbox.class) { return read(); }
    }
    public boolean acknowledge(String requestId) throws IOException {
        if (!validId(requestId)) return false;
        synchronized (IncomingShareInbox.class) {
            Record current = read();
            if (current == null) return true;
            if (!current.requestId.equals(requestId)) return false;
            return Files.deleteIfExists(slot.toPath());
        }
    }
    private Record read() throws IOException {
        if (!slot.exists()) return null;
        if (Files.isSymbolicLink(slot.toPath()) || !slot.isFile() || slot.length() > IncomingShareText.MAX_BYTES + 256) throw new IOException("Saved shared text is invalid.");
        try (DataInputStream input = new DataInputStream(new FileInputStream(slot))) {
            if (input.readInt() != MAGIC) throw new IOException("Saved shared text is invalid.");
            String id = input.readUTF(), time = input.readUTF();
            if (!validId(id)) throw new IOException("Saved shared text is invalid.");
            requireTime(time);
            int count = input.readInt(); if (count < 1 || count > IncomingShareText.MAX_BYTES) throw new IOException("Saved shared text exceeds the size limit.");
            byte[] bytes = new byte[count]; input.readFully(bytes);
            if (input.read() != -1) throw new IOException("Saved shared text is invalid.");
            return new Record(id, IncomingShareText.decode(bytes), time);
        }
    }
    private static boolean validId(String id) { return id != null && id.matches("[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}"); }
    private static void requireTime(String time) throws IOException {
        try { if (time == null || time.length() > 64) throw new IllegalArgumentException(); Instant.parse(time); }
        catch (RuntimeException invalid) { throw new IOException("Shared-text receipt time is unavailable."); }
    }
}

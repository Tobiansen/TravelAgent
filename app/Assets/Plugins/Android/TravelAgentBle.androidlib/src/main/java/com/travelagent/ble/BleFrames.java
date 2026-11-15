package com.travelagent.ble;

import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Map;

/** Version-one application framing, including MTU-23 fallback. No Android dependencies. */
public final class BleFrames {
    public static final int MAX_MESSAGE = 8192;
    private static final int HEADER = 8;
    private static final int MAX_PARTS = 1024;
    private static final int MAX_ASSEMBLIES = 4;
    private static final long TTL_MS = 10000;
    private final Map<Integer, Assembly> assemblies = new HashMap<>();

    public static List<byte[]> split(String message, int mtu, int messageId) {
        byte[] bytes = message.getBytes(StandardCharsets.UTF_8);
        if (bytes.length == 0 || bytes.length > MAX_MESSAGE) throw new IllegalArgumentException("Message length exceeds protocol bound");
        int payload = Math.max(12, Math.min(512, mtu - 3) - HEADER);
        int count = (bytes.length + payload - 1) / payload;
        List<byte[]> frames = new ArrayList<>(count);
        for (int index = 0; index < count; index++) {
            int offset = index * payload;
            int size = Math.min(payload, bytes.length - offset);
            byte[] frame = new byte[HEADER + size];
            frame[0] = 0x54; frame[1] = 1;
            write16(frame, 2, messageId); write16(frame, 4, index); write16(frame, 6, count);
            System.arraycopy(bytes, offset, frame, HEADER, size); frames.add(frame);
        }
        return frames;
    }

    public String accept(byte[] frame, long nowMs) {
        expire(nowMs);
        if (frame == null || frame.length <= HEADER || frame[0] != 0x54 || frame[1] != 1)
            throw new IllegalArgumentException("Invalid BLE frame header");
        int id = read16(frame, 2), index = read16(frame, 4), count = read16(frame, 6);
        if (count < 1 || count > MAX_PARTS || index >= count) throw new IllegalArgumentException("Invalid BLE fragment index");
        Assembly assembly = assemblies.get(id);
        if (assembly == null) {
            if (assemblies.size() >= MAX_ASSEMBLIES) throw new IllegalArgumentException("Too many incomplete BLE messages");
            assembly = new Assembly(count, nowMs); assemblies.put(id, assembly);
        }
        if (assembly.parts.length != count) { assemblies.remove(id); throw new IllegalArgumentException("BLE fragment count changed"); }
        int size = frame.length - HEADER;
        if (assembly.parts[index] == null) {
            if (assembly.total + size > MAX_MESSAGE) { assemblies.remove(id); throw new IllegalArgumentException("BLE message exceeds bound"); }
            byte[] bytes = new byte[size]; System.arraycopy(frame, HEADER, bytes, 0, size);
            assembly.parts[index] = bytes; assembly.total += size; assembly.received++;
        } else {
            byte[] prior = assembly.parts[index];
            if (prior.length != size) { assemblies.remove(id); throw new IllegalArgumentException("Conflicting duplicate fragment"); }
            for (int i = 0; i < size; i++) if (prior[i] != frame[HEADER + i]) {
                assemblies.remove(id); throw new IllegalArgumentException("Conflicting duplicate fragment");
            }
        }
        if (assembly.received != count) return null;
        ByteArrayOutputStream output = new ByteArrayOutputStream(assembly.total);
        for (byte[] part : assembly.parts) output.write(part, 0, part.length);
        assemblies.remove(id);
        return new String(output.toByteArray(), StandardCharsets.UTF_8);
    }

    public void clear() { assemblies.clear(); }
    private void expire(long nowMs) {
        Iterator<Map.Entry<Integer, Assembly>> iterator = assemblies.entrySet().iterator();
        while (iterator.hasNext()) if (nowMs - iterator.next().getValue().started > TTL_MS) iterator.remove();
    }
    private static int read16(byte[] data, int offset) { return (data[offset] & 255) | ((data[offset + 1] & 255) << 8); }
    private static void write16(byte[] data, int offset, int value) { data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8); }
    private static final class Assembly {
        final byte[][] parts; final long started; int total; int received;
        Assembly(int count, long started) { parts = new byte[count][]; this.started = started; }
    }
}

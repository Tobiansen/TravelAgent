package com.travelagent.ble;

import java.util.ArrayDeque;

/** A single replaceable motion slot cannot crowd execution acknowledgements or discrete events. */
public final class LiveMotionBuffer {
    private final int capacity;
    private final ArrayDeque<String> controls = new ArrayDeque<>();
    private String motion;
    private long replaced;

    public LiveMotionBuffer(int capacity) {
        if (capacity < 2) throw new IllegalArgumentException("Queue needs at least two control slots");
        this.capacity = capacity;
    }
    public boolean offerControl(String value) {
        boolean overflow = controls.size() >= capacity;
        if (overflow) controls.removeFirst();
        controls.addLast(value);
        return overflow;
    }
    public void offerMotion(String value) { if (motion != null) replaced++; motion = value; }
    public String poll() {
        if (!controls.isEmpty()) return controls.removeFirst();
        String value = motion; motion = null; return value == null ? "" : value;
    }
    public void clearMotion() { motion = null; }
    public void clear() { controls.clear(); motion = null; replaced = 0; }
    public long replacedMotionCount() { return replaced; }
    public int size() { return controls.size() + (motion == null ? 0 : 1); }

    public static boolean valid(boolean authenticated, boolean capable, long snapshotAgeMs,
                                String device, String boot, int version, String sampleDevice, String sampleBoot,
                                long sequence, long elapsedMs, double deltaG, double gyroDps) {
        return authenticated && capable && snapshotAgeMs >= 0 && snapshotAgeMs <= 8000 && version == 1 &&
            device != null && !device.isEmpty() && device.equals(sampleDevice) &&
            boot != null && !boot.isEmpty() && boot.equals(sampleBoot) && sequence > 0 && elapsedMs >= 0 &&
            Double.isFinite(deltaG) && deltaG >= 0 && deltaG <= 4 &&
            Double.isFinite(gyroDps) && gyroDps >= 0 && gyroDps <= 10000;
    }
}

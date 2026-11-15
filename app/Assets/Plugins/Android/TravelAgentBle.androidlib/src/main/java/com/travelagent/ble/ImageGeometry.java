package com.travelagent.ble;

/** Native-independent preview sizing and all eight EXIF orientation transforms. */
public final class ImageGeometry {
    public static final int PREVIEW_EDGE = 1280;
    private ImageGeometry() { }
    public static int sampleSize(int width, int height, int edge) {
        if (width <= 0 || height <= 0 || edge <= 0) throw new IllegalArgumentException("Invalid image dimensions.");
        int sample = 1;
        while (((long)Math.max(width, height) + sample - 1) / sample > edge && sample < (1 << 30)) sample *= 2;
        return sample;
    }
    public static float[] orientationMatrix(int orientation) {
        switch (orientation) {
            case 2: return matrix(-1, 0, 0, 1);
            case 3: return matrix(-1, 0, 0, -1);
            case 4: return matrix(1, 0, 0, -1);
            case 5: return matrix(0, 1, 1, 0);
            case 6: return matrix(0, -1, 1, 0);
            case 7: return matrix(0, -1, -1, 0);
            case 8: return matrix(0, 1, -1, 0);
            default: return matrix(1, 0, 0, 1);
        }
    }
    private static float[] matrix(float a, float b, float c, float d) { return new float[] { a, b, 0, c, d, 0, 0, 0, 1 }; }
}

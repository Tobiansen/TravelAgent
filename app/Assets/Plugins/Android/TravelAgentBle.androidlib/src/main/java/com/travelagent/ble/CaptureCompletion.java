package com.travelagent.ble;

import java.io.*;

/** Commit original evidence independently of the optional, replaceable display derivative. */
public final class CaptureCompletion {
    public interface Validator { void validate(File original) throws IOException; }
    public interface PreviewCreator { File create(File original) throws IOException; }
    public static final class Result {
        public final File original;
        public final String previewPath, previewWarning;
        private Result(File original, String preview, String warning) { this.original = original; previewPath = preview; previewWarning = warning; }
    }
    private CaptureCompletion() { }
    public static Result prepare(File filesRoot, String id, Validator validator, PreviewCreator preview) throws IOException {
        File original = CaptureFiles.image(filesRoot, "originals", id, false);
        if (!original.isFile()) {
            File pending = CaptureFiles.image(filesRoot, "pending", id, true);
            if (!"jpeg".equals(CaptureFiles.imageFormat(pending))) throw new IOException("The camera must return a JPEG photo. Choose an existing PNG/JPEG instead.");
            validator.validate(pending);
            if (!pending.renameTo(original)) throw new IOException("Camera original could not be committed.");
        } else { CaptureFiles.imageFormat(original); validator.validate(original); }
        try { return new Result(original, preview.create(original).getAbsolutePath(), ""); }
        catch (IOException failedPreview) { return new Result(original, "", "Original camera photo retained. Its display preview is unavailable; retry preview preparation."); }
    }
}

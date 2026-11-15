import com.travelagent.ble.*;
import java.io.*;
import java.nio.file.*;
import java.util.*;

/** Exercises real disk journals/path isolation and orientation/sampling math without Android stubs. */
public final class PhotoCaptureTest {
    private static int checks;
    public static void main(String[] args) throws Exception {
        Path temporary = Files.createTempDirectory("travelagent-camera-test-");
        try {
            File root = temporary.resolve("private").toFile(); root.mkdirs();
            CaptureJournal journal = new CaptureJournal(root);
            check(journal.read() == null, "no invented recovered request");
            CaptureJournal.Record record = new CaptureJournal.Record();
            record.id = UUID.randomUUID().toString(); record.context = "demo:journey_123:after"; record.requested = "2026-11-14T18:00:00Z";
            File pending = CaptureFiles.image(root, "pending", record.id, false); check(pending.createNewFile(), "private pending output created");
            journal.write(record);
            CaptureJournal restarted = new CaptureJournal(root);
            check(restarted.read().id.equals(record.id) && restarted.read().context.equals(record.context), "restart retains exact request and journey context");
            check(restarted.read().state.equals("capturing"), "interrupted capture is pending, not accepted evidence");
            check(!journal.remove(UUID.randomUUID().toString()) && journal.read() != null, "old acknowledgement cannot release another request");
            byte[] jpeg = new byte[] { (byte)255, (byte)216, (byte)255, (byte)224, 0, 2, (byte)255, (byte)217 };
            Files.write(pending.toPath(), jpeg);
            record.state = "processing"; record.returned = "2026-11-14T18:01:00Z"; journal.write(record);
            check(new CaptureJournal(root).read().state.equals("processing"), "successful camera return can resume validation after process loss");
            File original = CaptureFiles.image(root, "originals", record.id, false);
            check(pending.renameTo(original), "full-resolution original committed without decode/reencode");
            check(Arrays.equals(jpeg, Files.readAllBytes(original.toPath())), "commit preserves original bytes");
            check(CaptureFiles.imageFormat(original).equals("jpeg"), "JPEG signature accepted");
            record.state = "ready"; record.preview = "a separate bounded derivative"; journal.write(record);
            check(journal.read().state.equals("ready") && journal.read().returned.equals(record.returned), "completed result and return time survive restart until acknowledgement");
            check(!journal.read().requested.equals(journal.read().returned), "request and return clocks remain distinct");
            record.state = "error"; record.error = "Readable error\nwith literal = and : characters"; journal.write(record);
            check(journal.read().error.equals(record.error), "durable metadata round-trips without newline corruption");
            for (String bad : new String[] { "../outside", "", record.id + ".jpg", "/tmp/photo", "%2e%2e" })
                failure(() -> CaptureFiles.image(root, "pending", bad, false), "non-opaque output identifier rejected");
            for (String bad : new String[] { "demo:journey:unknown", "demo:../secret:after", "real:journey:after\nother", "demo:token:after:extra" }) {
                check(!CaptureFiles.validContext(bad), "invalid store/stage/context rejected");
            }
            failure(() -> CaptureFiles.directory(root, "../../outside"), "camera directory cannot escape private root");
            File importedFolder = new File(root, "Real/Images"); importedFolder.mkdirs();
            File imported = new File(importedFolder, "original.jpg"); Files.write(imported.toPath(), jpeg);
            check(CaptureFiles.requirePrivateImage(imported, new File[] { root }).equals(imported.getCanonicalFile()), "legacy private original can get bounded preview");
            File sibling = temporary.resolve("private-other/Real/Images").toFile(); sibling.mkdirs();
            File outside = new File(sibling, "original.jpg"); Files.write(outside.toPath(), jpeg);
            failure(() -> CaptureFiles.requirePrivateImage(outside, new File[] { root }), "prefix-sibling private roots rejected");
            File secret = new File(root, "secret.jpg"); Files.write(secret.toPath(), jpeg);
            failure(() -> CaptureFiles.requirePrivateImage(secret, new File[] { root }), "arbitrary private files cannot be previewed");
            File nested = new File(importedFolder, "nested"); nested.mkdir(); File nestedImage = new File(nested, "image.jpg"); Files.write(nestedImage.toPath(), jpeg);
            failure(() -> CaptureFiles.requirePrivateImage(nestedImage, new File[] { root }), "unapproved nested path rejected");
            File huge = new File(importedFolder, "huge.jpg"); try (RandomAccessFile sparse = new RandomAccessFile(huge, "rw")) { sparse.setLength(CaptureFiles.MAX_BYTES + 1); }
            failure(() -> CaptureFiles.imageFormat(huge), "oversize input bounded before bitmap decoder");
            check(huge.length() == CaptureFiles.MAX_BYTES + 1, "rejected source unchanged");
            Files.write(secret.toPath(), new byte[8]); failure(() -> CaptureFiles.imageFormat(secret), "nonimage signature rejected");
            record.context = "real:changed:before"; record.id = "../escape"; failure(() -> journal.write(record), "journal cannot persist path-injected identifiers");
            check(journal.read().context.equals("demo:journey_123:after"), "rejected write retains prior exact context");
            String retainedId = journal.read().id; check(journal.remove(retainedId) && new CaptureJournal(root).read() == null, "matching acknowledgement releases durable result");
            check(original.isFile(), "metadata acknowledgement itself cannot delete arbitrary image originals");

            String previewFailureId = UUID.randomUUID().toString();
            File newPending = CaptureFiles.image(root, "pending", previewFailureId, false); Files.write(newPending.toPath(), jpeg);
            CaptureCompletion.Result retained = CaptureCompletion.prepare(root, previewFailureId, source -> { }, source -> { throw new IOException("simulated decoder/cache failure"); });
            check(retained.original.isFile() && Arrays.equals(jpeg, Files.readAllBytes(retained.original.toPath())), "preview failure retains byte-identical camera original");
            check(retained.previewPath.isEmpty() && !retained.previewWarning.isEmpty(), "preview failure is a nonfatal derivative warning");
            check(!newPending.exists(), "successful original commit is not left writable in camera staging");
            CaptureCompletion.Result retried = CaptureCompletion.prepare(root, previewFailureId, source -> { }, source -> { File preview = new File(root, "display.jpg"); Files.write(preview.toPath(), jpeg); return preview; });
            check(!retried.previewPath.isEmpty() && retried.previewWarning.isEmpty(), "optional preview can be retried after original commit");
            check(Arrays.equals(jpeg, Files.readAllBytes(retained.original.toPath())), "preview retry cannot rewrite original");

            for (int[] dimensions : new int[][] { {4000,3000}, {8000,6000}, {12000,9000}, {1,1}, {Integer.MAX_VALUE,1} }) {
                int sample = ImageGeometry.sampleSize(dimensions[0], dimensions[1], 1280);
                check(((long)Math.max(dimensions[0], dimensions[1]) + sample - 1) / sample <= 1280, "decode sampling obeys maximum edge");
                check((sample & (sample - 1)) == 0, "Android decoder sample uses power of two");
            }
            int[][] expected = { {1,2,3,4,5,6}, {3,2,1,6,5,4}, {6,5,4,3,2,1}, {4,5,6,1,2,3}, {1,4,2,5,3,6}, {4,1,5,2,6,3}, {6,3,5,2,4,1}, {3,6,2,5,1,4} };
            for (int orientation = 1; orientation <= 8; orientation++) check(Arrays.equals(orient(orientation), expected[orientation - 1]), "EXIF " + orientation + " preserves correct pixel orientation, including mirrored cases");
            System.out.println("PASS: " + checks + " camera journal/path/orientation assertions");
        } finally {
            try (java.util.stream.Stream<Path> paths = Files.walk(temporary)) { for (Path path : paths.sorted(Comparator.reverseOrder()).toArray(Path[]::new)) Files.deleteIfExists(path); }
        }
    }
    private static int[] orient(int orientation) {
        float[] m = ImageGeometry.orientationMatrix(orientation); int width = orientation >= 5 ? 2 : 3;
        int minX = Integer.MAX_VALUE, minY = Integer.MAX_VALUE;
        for (int x : new int[] {0,2}) for (int y : new int[] {0,1}) { minX = Math.min(minX, (int)(m[0]*x+m[1]*y)); minY = Math.min(minY, (int)(m[3]*x+m[4]*y)); }
        int[] result = new int[6];
        for (int y=0;y<2;y++) for(int x=0;x<3;x++) { int ox=(int)(m[0]*x+m[1]*y)-minX, oy=(int)(m[3]*x+m[4]*y)-minY; result[oy*width+ox]=y*3+x+1; }
        return result;
    }
    private static void check(boolean condition, String message) { checks++; if (!condition) throw new AssertionError(message); }
    private static void failure(Checked action, String message) throws Exception { try { action.run(); } catch (IOException expected) { checks++; return; } throw new AssertionError(message); }
    private interface Checked { void run() throws Exception; }
}

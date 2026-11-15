import com.travelagent.ble.ReportArchive;
import com.travelagent.ble.ReportFiles;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.zip.*;

/** Native-independent behavioral tests for export isolation, ZIP bytes and provider path boundaries. */
public final class ReportSharingTest {
    private static int checks;
    public static void main(String[] args) throws Exception {
        Path root = Files.createTempDirectory("travelagent-sharing-test-");
        try {
            File privateFiles = root.resolve("private-files").toFile();
            File exports = new File(privateFiles, "Demo/Exports");
            File export = new File(exports, "20261114-160001-001"); export.mkdirs();
            byte[] image = new byte[] { (byte)137, 80, 78, 71, 13, 10, 26, 10, 0, 1, 2, 3 };
            write(export, "report.html", "<h1>DEMO report</h1>"); write(export, "journey.json", "{\"origin\":\"demo\"}");
            write(export, "evidence-files.json", "{\"files\":[\"original.png\"]}"); Files.write(new File(export, "original.png").toPath(), image);
            File valid = ReportFiles.requireExportFolder(export, new File[] { privateFiles, null });
            check(valid.equals(export.getCanonicalFile()), "completed export accepted");
            expectFailure(() -> ReportFiles.requireExportFolder(privateFiles, new File[] { privateFiles }), "private root cannot be shared");
            File sibling = root.resolve("private-files-other/Demo/Exports/20261114-160001-001").toFile(); sibling.mkdirs();
            expectFailure(() -> ReportFiles.requireExportFolder(sibling, new File[] { privateFiles }), "prefix sibling rejected");
            File incomplete = new File(exports, "20261114-160001-002"); incomplete.mkdirs(); write(incomplete, "report.html", "partial");
            expectFailure(() -> ReportFiles.requireExportFolder(incomplete, new File[] { privateFiles }), "incomplete export rejected");
            File traversed = new File(export, "../../../../private-files-other/Demo/Exports/20261114-160001-001");
            expectFailure(() -> ReportFiles.requireExportFolder(traversed, new File[] { privateFiles }), "path traversal rejected");

            File cache = root.resolve("cache/SharedReports").toFile();
            File zip = ReportArchive.create(valid, cache);
            check(zip.isFile() && zip.getName().endsWith(".zip"), "committed archive exists");
            Map<String, byte[]> entries = new HashMap<>();
            try (ZipInputStream input = new ZipInputStream(new FileInputStream(zip))) {
                ZipEntry entry;
                while ((entry = input.getNextEntry()) != null) {
                    check(!entry.getName().contains("/") && !entry.getName().contains("\\"), "portable flat archive entries");
                    entries.put(entry.getName(), input.readAllBytes());
                }
            }
            check(entries.size() == 4, "all completed export files included");
            check(Arrays.equals(image, entries.get("original.png")), "original image bytes retained");
            check(Arrays.equals(Files.readAllBytes(new File(export, "journey.json").toPath()), entries.get("journey.json")), "structured provenance bytes retained");
            check(export.listFiles().length == 4 && new File(export, "original.png").isFile(), "local export unchanged");
            check(ReportFiles.requireArchive(cache, zip.getName()).equals(zip.getCanonicalFile()), "opaque committed archive resolves");
            for (String name : new String[] { "../" + zip.getName(), "..\\" + zip.getName(), "/" + zip.getName(), zip.getName() + ".partial", "%2e%2e", "app-state.json", "" })
                expectFailure(() -> ReportFiles.requireArchive(cache, name), "provider rejects non-opaque or traversal name");
            File untouched = new File(export, "original.png");
            File nested = new File(export, "nested"); nested.mkdir();
            expectFailure(() -> ReportArchive.create(export, cache), "nested source directory rejected"); nested.delete();
            check(Arrays.equals(image, Files.readAllBytes(untouched.toPath())), "failed sharing retains originals");
            expectFailure(() -> ReportArchive.create(export, new File(export, "cache")), "cache cannot alter original export tree");

            File tooMany = root.resolve("many-files").toFile(); tooMany.mkdir();
            for (int i = 0; i <= ReportArchive.MAX_FILES; i++) write(tooMany, "file-" + i + ".txt", "x");
            expectFailure(() -> ReportArchive.create(tooMany, cache), "file count bounded");
            File large = root.resolve("large-export").toFile(); large.mkdir();
            try (RandomAccessFile sparse = new RandomAccessFile(new File(large, "large.png"), "rw")) { sparse.setLength(ReportArchive.MAX_BYTES + 1); }
            expectFailure(() -> ReportArchive.create(large, cache), "aggregate bytes bounded before compression");
            check(new File(large, "large.png").length() == ReportArchive.MAX_BYTES + 1, "size rejection retains original");
            check(Arrays.stream(cache.listFiles()).noneMatch(file -> file.getName().endsWith(".partial")), "no incomplete share archive exposed");
            System.out.println("PASS: " + checks + " report sharing/path assertions");
        } finally {
            try (java.util.stream.Stream<Path> paths = Files.walk(root)) { for (Path path : paths.sorted(Comparator.reverseOrder()).toArray(Path[]::new)) Files.deleteIfExists(path); }
        }
    }
    private static void write(File folder, String name, String value) throws IOException { Files.write(new File(folder, name).toPath(), value.getBytes(StandardCharsets.UTF_8)); }
    private static void check(boolean value, String message) { checks++; if (!value) throw new AssertionError(message); }
    private static void expectFailure(Checked action, String message) throws Exception {
        try { action.run(); } catch (IOException expected) { checks++; return; }
        throw new AssertionError(message);
    }
    private interface Checked { void run() throws Exception; }
}

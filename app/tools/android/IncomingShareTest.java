import com.travelagent.ble.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;

/** Behavioral text bounds, durable non-consuming inbox, matching acknowledgement and concurrent delivery tests. */
public final class IncomingShareTest {
    private static int checks;
    public static void main(String[] args) throws Exception {
        for (String[] supported : new String[][] { {"android.intent.action.SEND","text/plain"}, {"android.intent.action.SEND","text/html"}, {"android.intent.action.PROCESS_TEXT","text/plain"} })
            check(IncomingShareText.supported(supported[0], supported[1]), "only supported selected-text intent route accepted");
        for (String[] unsupported : new String[][] { {"android.intent.action.SEND_MULTIPLE","text/plain"}, {"android.intent.action.VIEW","text/plain"}, {"android.intent.action.SEND","image/png"}, {"android.intent.action.SEND","application/pdf"}, {"android.intent.action.SEND","*/*"}, {"android.intent.action.PROCESS_TEXT","text/html"}, {null,"text/plain"}, {"android.intent.action.SEND",null} })
            check(!IncomingShareText.supported(unsupported[0], unsupported[1]), "unexpected action/MIME rejected");
        String unicode = "Flight AY123\nHEL → CPH\nName: TEST Ää 😀\n<b>literal user text</b>\nhttps://untrusted.invalid/not-fetched";
        check(IncomingShareText.require(unicode).equals(unicode), "plain text preserves Unicode/markup-like characters without evaluating them");
        check(IncomingShareText.decode(IncomingShareText.encode(unicode)).equals(unicode), "strict UTF-8 round-trip retains selected text");
        check(IncomingShareText.encode("x".repeat(65536)).length == 65536, "64KiB ASCII boundary accepted");
        check(IncomingShareText.encode("ä".repeat(32768)).length == 65536, "64KiB multi-byte boundary accepted");
        check(IncomingShareText.encode("😀".repeat(16384)).length == 65536, "64KiB supplementary Unicode boundary accepted");
        fail(() -> IncomingShareText.require("x".repeat(65537)), "ASCII byte limit enforced");
        fail(() -> IncomingShareText.require("ä".repeat(32769)), "UTF-8 byte limit is not a UTF-16 character-count limit");
        fail(() -> IncomingShareText.require("😀".repeat(16385)), "supplementary UTF-8 byte limit enforced");
        fail(() -> IncomingShareText.require(" \n\t\u00a0"), "all-whitespace share rejected");
        fail(() -> IncomingShareText.require("A\0B"), "embedded NUL rejected");
        fail(() -> IncomingShareText.require("bad\ud800"), "unpaired surrogate rejected");
        fail(() -> IncomingShareText.decode(new byte[] {(byte)0xc0,(byte)0xaf}), "malformed UTF-8 cannot be silently normalized");
        Path temporary = Files.createTempDirectory("travelagent-incoming-share-test-");
        try {
            File root = temporary.resolve("private").toFile(); root.mkdirs();
            IncomingShareInbox inbox = new IncomingShareInbox(root);
            check(inbox.peek() == null, "no invented incoming share");
            String time = "2026-11-14T20:00:00Z";
            IncomingShareInbox.Record first = inbox.accept(unicode, time);
            check(first != null && first.origin.equals(IncomingShareText.ORIGIN), "accepted text is labelled unverified user input");
            check(first.origin.indexOf("Gmail") < 0 && first.origin.indexOf("verified sender") < 0, "origin never asserts sender authentication");
            IncomingShareInbox restarted = new IncomingShareInbox(root);
            check(restarted.peek().text.equals(unicode) && restarted.peek().requestId.equals(first.requestId) && restarted.peek().receivedAtUtc.equals(time), "process restart retains exact body/id/receipt time");
            check(restarted.peek().requestId.equals(first.requestId), "repeated polling does not consume the inbox");
            check(restarted.accept("Second ticket", time) == null && restarted.peek().text.equals(unicode), "new share cannot overwrite pending text");
            check(!inbox.acknowledge(UUID.randomUUID().toString()) && inbox.peek() != null, "stale acknowledgement cannot remove current pending item");
            check(!inbox.acknowledge("../pending.bin") && inbox.peek() != null, "acknowledgement does not accept paths");
            check(inbox.acknowledge(first.requestId) && new IncomingShareInbox(root).peek() == null, "explicit matching acknowledgement consumes exactly one item");
            check(inbox.acknowledge(first.requestId), "same acknowledgement is idempotent after removal");
            fail(() -> inbox.accept("ä".repeat(32769), time), "invalid text cannot occupy inbox");
            check(inbox.peek() == null, "oversize rejection leaves inbox empty");
            fail(() -> inbox.accept("ticket", "not-a-time"), "invalid receipt metadata rejected");
            File directory = new File(root, "IncomingShares");
            File abandoned = new File(directory, "incoming-" + UUID.randomUUID() + ".partial"); Files.writeString(abandoned.toPath(), "uncommitted prior request");
            IncomingShareInbox.Record afterCrash = inbox.accept("User retried explicitly", time);
            check(!abandoned.exists() && afterCrash != null, "uncommitted crash staging is cleaned without affecting a committed item");
            inbox.acknowledge(afterCrash.requestId);
            ExecutorService workers = Executors.newFixedThreadPool(2);
            try {
                CountDownLatch start = new CountDownLatch(1);
                Future<IncomingShareInbox.Record> a = workers.submit(() -> { start.await(); return new IncomingShareInbox(root).accept("Concurrent ticket A", time); });
                Future<IncomingShareInbox.Record> b = workers.submit(() -> { start.await(); return new IncomingShareInbox(root).accept("Concurrent ticket B", time); });
                start.countDown(); IncomingShareInbox.Record ar = a.get(), br = b.get();
                check((ar == null) != (br == null), "concurrent shares commit exactly one pending item");
                String expected = ar == null ? br.text : ar.text;
                check(inbox.peek().text.equals(expected), "concurrent loser cannot replace winner's text");
                inbox.acknowledge(inbox.peek().requestId);
            } finally { workers.shutdownNow(); }
            File slot = new File(directory, "pending.bin"); Files.write(slot.toPath(), new byte[] {0,1,2,3});
            fail(() -> inbox.peek(), "truncated/corrupt private record rejected");
            check(inbox.accept("New share", time) == null && Arrays.equals(Files.readAllBytes(slot.toPath()), new byte[] {0,1,2,3}), "a malformed retained record is never silently overwritten");
            Files.delete(slot.toPath());
            IncomingShareInbox.Record maximum = inbox.accept("ä".repeat(32768), time);
            check(new IncomingShareInbox(root).peek().text.equals(maximum.text), "maximum valid UTF-8 text persists across restart");
            check(directory.listFiles().length == 1, "only committed bounded inbox file remains");
            System.out.println("PASS: " + checks + " incoming-share input/inbox/lifecycle assertions");
        } finally {
            try (java.util.stream.Stream<Path> paths = Files.walk(temporary)) { for (Path path : paths.sorted(Comparator.reverseOrder()).toArray(Path[]::new)) Files.deleteIfExists(path); }
        }
    }
    private static void check(boolean condition, String message) { checks++; if(!condition)throw new AssertionError(message); }
    private static void fail(Checked action, String message) throws Exception { try {action.run();}catch(IOException expected){checks++;return;}throw new AssertionError(message); }
    private interface Checked { void run() throws Exception; }
}

import com.travelagent.ble.BleFrames;
import java.util.*;

/** Standalone deterministic protocol checks, with no Android device or test dependency. */
public final class BleFramesTest {
    private static int checks;
    public static void main(String[] args) {
        roundTrip("{\"operation\":\"getSnapshot\"}", 23);
        roundTrip("A long UTF-8 message: Helsinki \u2192 Espoo \ud83e\uddf3 ".repeat(80), 185);
        roundTrip("x".repeat(BleFrames.MAX_MESSAGE), 23);
        List<byte[]> ordered = BleFrames.split("sequence configuration payload".repeat(10), 23, 91);
        List<byte[]> reversed = new ArrayList<>(ordered); Collections.reverse(reversed);
        BleFrames receiver = new BleFrames(); String result = null;
        for (byte[] frame : reversed) { result = receiver.accept(frame, 1); receiver.accept(frame, 1); }
        check(result != null && result.equals("sequence configuration payload".repeat(10)), "out-of-order / duplicate frames");
        expectFailure(() -> new BleFrames().accept(new byte[] { 1, 2 }, 0), "short frame rejected");
        expectFailure(() -> BleFrames.split("x".repeat(BleFrames.MAX_MESSAGE + 1), 23, 1), "oversized output rejected");
        byte[] bad = ordered.get(0).clone(); bad[6] = 0; bad[7] = 0;
        expectFailure(() -> new BleFrames().accept(bad, 0), "zero fragment count rejected");
        BleFrames duplicate = new BleFrames(); duplicate.accept(ordered.get(0), 0);
        byte[] conflict = ordered.get(0).clone(); conflict[8] ^= 1;
        expectFailure(() -> duplicate.accept(conflict, 0), "conflicting duplicate rejected");
        BleFrames limited = new BleFrames();
        for (int id = 1; id <= 4; id++) limited.accept(BleFrames.split("x".repeat(100), 23, id).get(0), 0);
        expectFailure(() -> limited.accept(BleFrames.split("x".repeat(100), 23, 5).get(0), 0), "incomplete assembly bound");
        limited.accept(BleFrames.split("x".repeat(100), 23, 5).get(0), 10001);
        check(true, "expired assemblies release capacity");
        System.out.println("PASS: " + checks + " BLE framing checks");
    }
    private static void roundTrip(String text, int mtu) {
        BleFrames receiver = new BleFrames(); String result = null;
        for (byte[] frame : BleFrames.split(text, mtu, 42)) {
            check(frame.length <= mtu - 3, "frame fits ATT payload");
            result = receiver.accept(frame, 0);
        }
        check(text.equals(result), "UTF-8 round trip at MTU " + mtu);
    }
    private static void check(boolean value, String message) { checks++; if (!value) throw new AssertionError(message); }
    private static void expectFailure(Runnable action, String message) {
        try { action.run(); } catch (IllegalArgumentException expected) { checks++; return; }
        throw new AssertionError(message);
    }
}

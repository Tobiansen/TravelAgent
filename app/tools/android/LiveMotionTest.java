import com.travelagent.ble.LiveMotionBuffer;

/** Deterministic stream admission and queue-pressure checks, no Android runtime or radio. */
public final class LiveMotionTest {
    private static int checks;
    public static void main(String[] args) {
        check(valid(true, true, 100, 1, "bag", "boot", 1, 200, .2, 20), "current authenticated fresh measurement accepted");
        check(!valid(false, true, 100, 1, "bag", "boot", 1, 200, .2, 20), "unowned measurement rejected");
        check(!valid(true, false, 100, 1, "bag", "boot", 1, 200, .2, 20), "unadvertised stream rejected");
        check(!valid(true, true, 8001, 1, "bag", "boot", 1, 200, .2, 20), "stale snapshot cannot authenticate new samples");
        check(!valid(true, true, -1, 1, "bag", "boot", 1, 200, .2, 20), "negative snapshot age rejected");
        check(valid(true, true, 8000, 1, "bag", "boot", 1, 0, 0, 0), "inclusive freshness and elapsed lower bounds");
        check(!valid(true, true, 100, 0, "bag", "boot", 1, 200, .2, 20), "sample schema required");
        check(!valid(true, true, 100, 1, "other", "boot", 1, 200, .2, 20), "other device rejected");
        check(!valid(true, true, 100, 1, "bag", "old", 1, 200, .2, 20), "old boot rejected");
        check(!valid(true, true, 100, 1, "bag", "boot", 0, 200, .2, 20), "zero sequence rejected");
        check(!valid(true, true, 100, 1, "bag", "boot", 1, -1, .2, 20), "negative elapsed time rejected");
        for (double invalid : new double[] { -.01, 4.01, Double.NaN, Double.POSITIVE_INFINITY, Double.NEGATIVE_INFINITY })
            check(!valid(true, true, 100, 1, "bag", "boot", 1, 200, invalid, 20), "invalid acceleration excluded");
        for (double invalid : new double[] { -.01, 10000.01, Double.NaN, Double.POSITIVE_INFINITY, Double.NEGATIVE_INFINITY })
            check(!valid(true, true, 100, 1, "bag", "boot", 1, 200, .2, invalid), "invalid angular speed excluded");
        LiveMotionBuffer queue = new LiveMotionBuffer(8);
        queue.offerControl("arm-ack");
        for (int index = 0; index < 10000; index++) queue.offerMotion("sample-" + index);
        queue.offerControl("disarm-ack"); queue.offerControl("movement-event");
        check(queue.size() == 4, "ten thousand samples use one slot beside three control/events");
        check(queue.replacedMotionCount() == 9999, "motion coalescing count exact");
        check(queue.poll().equals("arm-ack"), "arm acknowledgement retained in order");
        check(queue.poll().equals("disarm-ack"), "disarm acknowledgement has priority over motion");
        check(queue.poll().equals("movement-event"), "discrete disturbance event retained");
        check(queue.poll().equals("sample-9999"), "latest measurement survives pressure");
        check(queue.poll().isEmpty(), "empty poll bounded and harmless");
        queue.offerMotion("old-boot-sample"); queue.clearMotion();
        check(queue.poll().isEmpty(), "disconnect or boot change clears pending motion");
        for (int index = 0; index < 8; index++) check(!queue.offerControl("control-" + index), "control capacity accepted");
        check(queue.offerControl("overflow"), "non-motion queue overflow is explicit");
        check(queue.size() == 8, "control queue remains bounded");
        check(queue.poll().equals("control-1"), "bounded control overflow retains documented FIFO policy");
        queue.offerMotion("old-session-sample");
        queue.clear();
        check(queue.poll().isEmpty() && queue.size() == 0, "fresh app session clears queued old controls and motion");
        check(queue.replacedMotionCount() == 0, "fresh session resets queue diagnostic counter");
        System.out.println("PASS: " + checks + " live motion admission/queue-pressure assertions");
    }
    private static boolean valid(boolean authenticated, boolean capable, long age, int version, String device, String boot,
                                 long sequence, long elapsed, double delta, double gyro) {
        return LiveMotionBuffer.valid(authenticated, capable, age, "bag", "boot", version, device, boot, sequence, elapsed, delta, gyro);
    }
    private static void check(boolean value, String description) { checks++; if (!value) throw new AssertionError(description); }
}

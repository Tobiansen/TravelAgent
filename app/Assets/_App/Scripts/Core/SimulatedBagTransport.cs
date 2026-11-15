using System;
using System.Collections.Generic;

namespace TravelAgent.Core
{
    /// <summary>
    /// A small board simulator. It runs while disconnected, acknowledges desired-state commands,
    /// and retains a bounded event journal for reconnect replay. Recording/configuration are
    /// proposed firmware capabilities; every snapshot and event remains explicitly simulated.
    /// </summary>
    public sealed class SimulatedBagTransport : IBagTransport
    {
        public event Action<DeviceSnapshot> SnapshotChanged;
        public event Action<BagEvent> EventReceived;
        // Demo event categories are illustrative; no measured motion stream is invented.
        public event Action<MotionSample> MotionReceived { add { } remove { } }
        public event Action<string> Notice;
        public event Action<DiscoveredDevice> DeviceDiscovered;
        public DeviceSnapshot Snapshot { get { return reported.Copy(); } }
        public const string SimulatedDeviceId = "demo-bag-01";
        public float CommandDelaySeconds = 0.25f;
        public float CommandTimeoutSeconds = 5f;
        public int JournalCapacity = 512;
        public int WrongAttemptLimit = 3;
        public float WrongAttemptDelaySeconds = 30f;

        private DeviceSnapshot board;
        private DeviceSnapshot reported;
        private readonly ButtonSequenceParser buttons = new ButtonSequenceParser();
        private readonly List<BagEvent> journal = new List<BagEvent>();
        private readonly Dictionary<string, CommandResult> executed = new Dictionary<string, CommandResult>();
        private readonly Queue<string> executedOrder = new Queue<string>();
        private readonly DateTime utcAnchor;
        private Pending pending;
        private bool connected;
        private bool disposed;
        private bool failCommands;
        private bool motorInhibited;
        private bool bothStopReported;
        private double now;
        private double bootStarted;
        private double lastPublished;
        private double armingStarted;
        private double lastMotion;
        private double warningStarted;
        private double alarmStarted;
        private double blockedUntil;
        private int wrongAttempts;
        private long sequence;

        private sealed class Pending { public DeviceCommand command; public double sentAt; public bool dropped; }
        private sealed class CommandResult { public bool ok; public string message; }

        public SimulatedBagTransport(string armSequence = "AABB", string disarmSequence = "BABA", int configRevision = 1)
            : this(DateTime.UtcNow, armSequence, disarmSequence, configRevision) { }

        public SimulatedBagTransport(DateTime startUtc, string armSequence = "AABB", string disarmSequence = "BABA", int configRevision = 1)
        {
            if (!SequenceRules.Validate(armSequence, disarmSequence, out string error)) throw new ArgumentException(error);
            utcAnchor = startUtc.Kind == DateTimeKind.Utc ? startUtc : startUtc.ToUniversalTime();
            board = NewBoard(armSequence, disarmSequence, configRevision);
            reported = board.Copy();
            reported.stale = true;
            reported.status = "Demo board disconnected. Connect to read its confirmed state.";
            buttons.Completed += OnSequence;
            buttons.ProgressChanged += OnProgress;
            buttons.Feedback += OnButtonFeedback;
        }

        private static DeviceSnapshot NewBoard(string arm, string disarm, int revision)
        {
            return new DeviceSnapshot
            {
                simulated = true, deviceId = SimulatedDeviceId, bootId = Guid.NewGuid().ToString("N"),
                guardState = "disarmed", recordingState = "idle", armSequence = arm, disarmSequence = disarm,
                configRevision = revision, motorState = "idle", reason = "none",
                capabilities = new[] { "snapshot", "guard", "buttonSequences", "quietRecording", "eventReplay", "testFeedback" }
            };
        }

        public void Scan()
        {
            CheckDisposed();
            DeviceDiscovered?.Invoke(new DiscoveredDevice { id = SimulatedDeviceId, name = "Atech suitcase · simulated", rssi = -42, simulated = true });
            Notice?.Invoke("Found one simulated board. No Bluetooth scan was performed.");
        }

        public void Connect(string deviceId)
        {
            CheckDisposed();
            if (!string.IsNullOrEmpty(deviceId) && deviceId != SimulatedDeviceId)
            {
                Notice?.Invoke("The selected simulated device is unavailable.");
                return;
            }
            bool reconnect = journal.Count > 0;
            connected = true;
            Publish(reconnect ? "Simulated board reconnected; stored events synchronized." : "Connected to simulated board.");
            foreach (BagEvent item in journal) EventReceived?.Invoke(item.Copy());
            Record("connection", reconnect ? "Simulated Bluetooth link restored." : "Simulated Bluetooth link connected.");
            Notice?.Invoke("Demo owner authenticated. This is simulated pairing, not a live BLE connection.");
        }

        public void Disconnect()
        {
            CheckDisposed();
            if (!connected) return;
            Record("connection", "Simulated link disconnected; the board continues independently.");
            connected = false;
            if (pending != null)
            {
                reported.lastCommandId = pending.command.id;
                reported.lastCommandSucceeded = false;
                pending = null;
            }
            reported.connected = false;
            reported.authenticated = false;
            reported.stale = true;
            reported.pendingCommandId = "";
            reported.pendingOperation = "";
            reported.status = "Disconnected. Guard and recording show the last confirmed state.";
            SnapshotChanged?.Invoke(reported.Copy());
            Notice?.Invoke("Link lost; local guarding or recording continues on the simulated board.");
        }

        public void Send(DeviceCommand command)
        {
            CheckDisposed();
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!connected) { Notice?.Invoke("Connect before sending a device command."); return; }
            if (string.IsNullOrWhiteSpace(command.id)) { Notice?.Invoke("Command rejected: a request ID is required."); return; }
            if (executed.TryGetValue(command.id, out CommandResult result))
            {
                board.lastCommandId = command.id;
                board.lastCommandSucceeded = result.ok;
                Publish("Repeated request acknowledged without executing twice. " + result.message);
                return;
            }
            if (pending != null)
            {
                Notice?.Invoke(pending.command.id == command.id ? "That command is already pending." : "Wait for the pending command to complete.");
                return;
            }
            pending = new Pending { command = command.Copy(), sentAt = now, dropped = failCommands };
            Publish("Waiting for device acknowledgement: " + command.operation + ".");
        }

        public void Tick(float deltaSeconds)
        {
            CheckDisposed();
            if (deltaSeconds < 0 || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            now += deltaSeconds;
            buttons.Tick(deltaSeconds);
            if (buttons.BothHeld && buttons.BothHeldSeconds >= 0.5 && !bothStopReported)
            {
                bothStopReported = true;
                if (board.guardState == "alarm" || board.guardState == "warning")
                {
                    motorInhibited = true;
                    board.motorState = "inhibited";
                    Record("motor", "Both buttons held: motor inhibited; guard and siren remain active.");
                    Publish("Motor inhibited by the physical stop gesture. Guard remains active.");
                }
            }
            if (!buttons.BothHeld) bothStopReported = false;
            if (board.guardState == "arming")
            {
                if (now - armingStarted >= 20) ChangeGuard("disarmed", "Arming canceled: the board did not settle.");
                else if (now - armingStarted >= 5 && now - lastMotion >= 1) ChangeGuard("armed", "Guard armed after settling.");
            }
            else if (board.guardState == "warning" && now - warningStarted >= 2)
            {
                alarmStarted = now;
                ChangeGuard("alarm", board.reason == "sensor_fault" ? "Sensor fault alarm; motor inhibited." : "Motion alarm latched.");
            }
            UpdateMotor();
            if (pending != null)
            {
                double age = now - pending.sentAt;
                if (age >= CommandTimeoutSeconds)
                {
                    string id = pending.command.id;
                    pending = null;
                    board.lastCommandId = id;
                    board.lastCommandSucceeded = false;
                    Publish("Device command timed out. Previous confirmed state retained.");
                    Notice?.Invoke("No device acknowledgement arrived. Retry after checking the connection.");
                }
                else if (!pending.dropped && age >= CommandDelaySeconds)
                {
                    DeviceCommand command = pending.command;
                    pending = null;
                    Execute(command);
                }
            }
            if (connected && now - lastPublished >= 1) Publish(reported.status);
        }

        /// <summary>A presenter convenience using the same physical parser, including release and debounce.</summary>
        public void Tap(char button)
        {
            Press(button, true);
            Tick(0.04f);
            Press(button, false);
        }

        public void Press(char button, bool down) { CheckDisposed(); buttons.Press(button, down); }

        public void SetFailure(bool enabled)
        {
            CheckDisposed();
            failCommands = enabled;
            Notice?.Invoke(enabled ? "Demo command loss enabled: the next command will time out." : "Demo command loss disabled.");
        }

        public void InjectMotion(string kind)
        {
            CheckDisposed();
            kind = (kind ?? "").Trim().ToLowerInvariant();
            switch (kind)
            {
                case "bump":
                case "incidental":
                case "incidental bump":
                    lastMotion = now;
                    Record("bump", "Sample brief disturbance below the 150 ms trigger duration; guard unchanged.");
                    break;
                case "rolling":
                case "slow rolling":
                case "slowrolling":
                    Record("movement", "Slow rolling sample: gentle movement may be missed by the current detector.");
                    Notice?.Invoke("Slow rolling is a known detector limitation; this sample does not promise an alarm.");
                    break;
                case "impact":
                    Record("impact", "Illustrative handling impact. Inspect the bag; this does not establish damage.", 2.4f);
                    BeginMotionWarning();
                    break;
                case "open":
                case "opening":
                case "lid":
                case "open lid":
                    Record("opening", "Fixture lid-opening event. The current physical rig has no lid sensor.");
                    break;
                case "sensor_fault":
                case "sensorfault":
                case "sensor fault":
                    if (board.guardState == "arming") ChangeGuard("disarmed", "Arming canceled: motion sensor unavailable.");
                    else if (board.guardState == "armed") BeginWarning("sensor_fault");
                    Record("sensor", "Simulated motion-sensor fault; no theft attribution.");
                    break;
                case "sustained":
                case "movement":
                case "sustained movement":
                    Record("movement", "Sample movement above detector thresholds for at least 150 ms.");
                    BeginMotionWarning();
                    break;
                default:
                    Notice?.Invoke("Unknown demo motion sample.");
                    return;
            }
            Publish(connected ? reported.status : null);
        }

        private void BeginMotionWarning()
        {
            lastMotion = now;
            if (board.guardState == "armed" && board.recordingState != "transit") BeginWarning("motion");
        }

        private void BeginWarning(string reason)
        {
            board.reason = reason;
            warningStarted = now;
            ChangeGuard("warning", reason == "sensor_fault" ? "Sensor fault warning; motor will remain inhibited." : "Movement warning: alarm begins after two seconds.");
        }

        private void Execute(DeviceCommand command)
        {
            bool ok = true;
            string message;
            switch (command.operation)
            {
                case "getSnapshot": message = "Current simulated board snapshot confirmed."; break;
                case "setGuardState":
                    if (command.desiredState == "disarmed")
                    {
                        ChangeGuard("disarmed", "Guard disarmed. Any transit record is retained.");
                        message = "Disarm confirmed by simulated board.";
                    }
                    else if (command.desiredState != "armed") { ok = false; message = "Guard desiredState must be armed or disarmed."; }
                    else if (board.recordingState == "transit") { ok = false; message = "Finish quiet transit recording before arming the guard."; }
                    else
                    {
                        if (board.guardState == "disarmed") StartArming();
                        message = "Arm command accepted; protection is confirmed only after settling.";
                    }
                    break;
                case "setButtonSequences":
                    if (!connected || !board.authenticated) { ok = false; message = "An authenticated owner connection is required."; }
                    else if (board.guardState != "disarmed") { ok = false; message = "Disarm the board before changing button sequences."; }
                    else if (command.expectedConfigRevision >= 0 && command.expectedConfigRevision != board.configRevision)
                    { ok = false; message = "Configuration revision changed; read the snapshot and try again."; }
                    else if (!SequenceRules.Validate(command.armSequence, command.disarmSequence, out message)) ok = false;
                    else
                    {
                        board.armSequence = command.armSequence;
                        board.disarmSequence = command.disarmSequence;
                        board.configRevision++;
                        buttons.Reset();
                        message = "Sequences saved and read back from the simulated board (revision " + board.configRevision + ").";
                        Record("configuration", "Button sequences confirmed at revision " + board.configRevision + ".");
                    }
                    break;
                case "setRecordingMode":
                    if (command.recordingMode == "transit")
                    {
                        ChangeGuard("disarmed", "Quiet transit starts with siren and automatic motor outputs off.");
                        board.recordingState = "transit";
                        Record("recording", "Quiet transit recording started; siren and automatic dispensing suppressed.");
                        message = "Quiet transit recording confirmed by simulated board.";
                    }
                    else if (command.recordingMode == "complete" || command.recordingMode == "idle")
                    {
                        board.recordingState = command.recordingMode;
                        Record("recording", "Transit recording " + command.recordingMode + "; stored evidence retained.");
                        message = "Recording mode confirmed: " + command.recordingMode + ".";
                    }
                    else { ok = false; message = "Recording mode must be transit, complete or idle."; }
                    break;
                case "getEventsSince":
                    if (journal.Count > 0 && command.sinceSequence < journal[0].sequence - 1)
                        Notice?.Invoke("The bounded simulated event journal overflowed; older events are unavailable.");
                    foreach (BagEvent item in journal)
                        if (item.sequence > command.sinceSequence) EventReceived?.Invoke(item.Copy());
                    message = "Stored events replayed; the app must deduplicate device/boot/sequence identities.";
                    break;
                case "testFeedback":
                    if (board.guardState != "disarmed") { ok = false; message = "Disarm before the bounded feedback test."; }
                    else { message = "Simulated brief light/sound feedback test complete; motor remains off."; Record("feedback", message); }
                    break;
                default: ok = false; message = "Unsupported simulated operation: " + command.operation + "."; break;
            }
            executed[command.id] = new CommandResult { ok = ok, message = message };
            executedOrder.Enqueue(command.id);
            if (executedOrder.Count > 128) executed.Remove(executedOrder.Dequeue());
            board.lastCommandId = command.id;
            board.lastCommandSucceeded = ok;
            Publish((ok ? "" : "Command rejected: ") + message);
            Notice?.Invoke(message);
        }

        private void OnSequence(string entered)
        {
            if (board.guardState == "disarmed")
            {
                if (entered == board.armSequence)
                {
                    if (board.recordingState == "transit") { Notice?.Invoke("Guard arming is unavailable during quiet transit."); return; }
                    StartArming();
                    Notice?.Invoke("Arm sequence accepted. Wait for settling.");
                }
                else { Record("button", "Incorrect arm sequence; guard unchanged."); Notice?.Invoke("Sequence not accepted."); }
                return;
            }
            if (now < blockedUntil)
            {
                Notice?.Invoke("Physical disarm input temporarily delayed after repeated incorrect attempts. Owner app disarm remains available.");
                return;
            }
            if (entered == board.disarmSequence)
            {
                wrongAttempts = 0;
                blockedUntil = 0;
                ChangeGuard("disarmed", "Physical disarm sequence accepted; recording is retained.");
                Notice?.Invoke("Disarmed by the physical sequence.");
            }
            else
            {
                wrongAttempts++;
                Record("button", "Incorrect disarm sequence; protection remains active.");
                if (wrongAttempts >= WrongAttemptLimit)
                {
                    blockedUntil = now + WrongAttemptDelaySeconds;
                    wrongAttempts = 0;
                    Notice?.Invoke("Repeated incorrect entries: physical input delayed for " + WrongAttemptDelaySeconds + " seconds.");
                }
                else Notice?.Invoke("Sequence not accepted. Protection remains active.");
            }
        }

        private void OnProgress(int progress) { board.codeProgress = progress; if (connected) Publish(reported.status); }
        private void OnButtonFeedback(string text) { Notice?.Invoke(text); }

        private void StartArming()
        {
            armingStarted = now;
            lastMotion = now;
            ChangeGuard("arming", "Arming: five seconds of settling and a final quiet second required.");
        }

        private void ChangeGuard(string state, string message)
        {
            bool changed = board.guardState != state;
            board.guardState = state;
            buttons.Reset();
            if (state == "disarmed")
            {
                board.reason = "none";
                board.motorState = "idle";
                motorInhibited = false;
                wrongAttempts = 0;
                blockedUntil = 0;
            }
            if (changed) Record("guard", message);
            Publish(message);
        }

        private void UpdateMotor()
        {
            string previous = board.motorState;
            if (board.guardState != "alarm") return;
            double elapsed = now - alarmStarted;
            if (motorInhibited || board.reason == "sensor_fault") board.motorState = "inhibited";
            else if (elapsed >= 20 || (int)(elapsed / 2) >= 8) board.motorState = "finished";
            else board.motorState = elapsed % 2 < 0.8 ? "commanded" : "gap";
            if (previous != board.motorState)
            {
                Record("motor", "Simulated motor command state: " + board.motorState + ". This is not measured rotation or dispensing.");
                Publish(reported.status);
            }
        }

        private void Record(string type, string summary, float peakG = 0)
        {
            var item = new BagEvent
            {
                deviceId = board.deviceId, bootId = board.bootId, sequence = ++sequence,
                elapsedMs = (long)((now - bootStarted) * 1000), recordedAtUtc = UtcNow(), type = type,
                summary = summary, origin = "simulated device", quality = "illustrative fixture; not a physical measurement", peakG = peakG
            };
            journal.Add(item);
            while (journal.Count > Math.Max(1, JournalCapacity)) journal.RemoveAt(0);
            if (connected) EventReceived?.Invoke(item.Copy());
        }

        private void Publish(string status)
        {
            if (!connected) return;
            board.connected = true;
            board.authenticated = true;
            board.stale = false;
            board.lastSeenUtc = UtcNow();
            board.pendingCommandId = pending == null ? "" : pending.command.id;
            board.pendingOperation = pending == null ? "" : pending.command.operation;
            if (status != null) board.status = status;
            reported = board.Copy();
            lastPublished = now;
            SnapshotChanged?.Invoke(reported.Copy());
        }

        private string UtcNow() { return utcAnchor.AddSeconds(now).ToString("O"); }

        /// <summary>Simulates a device reboot: saved codes survive, volatile guard state does not.</summary>
        public void Reboot()
        {
            CheckDisposed();
            string arm = board.armSequence;
            string disarm = board.disarmSequence;
            int revision = board.configRevision;
            board = NewBoard(arm, disarm, revision);
            buttons.Reset();
            pending = null;
            executed.Clear();
            executedOrder.Clear();
            motorInhibited = false;
            wrongAttempts = 0;
            blockedUntil = 0;
            sequence = 0;
            bootStarted = now;
            Record("connection", "Simulated board rebooted; saved codes and bounded journal retained, guard booted disarmed. New boot starts a separate time anchor.");
            Publish("Simulated reboot complete; saved button sequences retained.");
        }

        public void Reset()
        {
            CheckDisposed();
            board = NewBoard("AABB", "BABA", 1);
            reported = board.Copy();
            pending = null;
            connected = false;
            failCommands = false;
            motorInhibited = false;
            bothStopReported = false;
            wrongAttempts = 0;
            blockedUntil = 0;
            sequence = 0;
            bootStarted = now;
            journal.Clear();
            executed.Clear();
            executedOrder.Clear();
            buttons.Reset();
            reported.stale = true;
            reported.status = "Demo reset. Connect to the simulated board.";
            SnapshotChanged?.Invoke(reported.Copy());
        }

        private void CheckDisposed() { if (disposed) throw new ObjectDisposedException(nameof(SimulatedBagTransport)); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            buttons.Completed -= OnSequence;
            buttons.ProgressChanged -= OnProgress;
            buttons.Feedback -= OnButtonFeedback;
            SnapshotChanged = null;
            EventReceived = null;
            Notice = null;
            DeviceDiscovered = null;
        }
    }
}

using System;
using TravelAgent.Core;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace TravelAgent.Integrations
{
    /// <summary>Real BLE transport. Editor use explains its platform requirement; it never simulates success.</summary>
    public sealed class AndroidBagTransport : IBagTransport
    {
        public event Action<DeviceSnapshot> SnapshotChanged;
        public event Action<BagEvent> EventReceived;
        public event Action<MotionSample> MotionReceived;
        public event Action<string> Notice;
        public event Action<DiscoveredDevice> DeviceDiscovered;
        public DeviceSnapshot Snapshot { get { return snapshot.Copy(); } }

        private DeviceSnapshot snapshot = new DeviceSnapshot { status = "Ready to scan for your bag. Choose Find nearby bag." };
        private DeviceCommand pending;
        private string pendingDevice = "", pendingBoot = "";
        private float pendingSeconds, silentSeconds;
        private bool disposed, expectingInitialSnapshot;
        private long lastMotionSequence, lastMotionElapsed = -1;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass bridge;
        private PermissionCallbacks permissionCallbacks;
        private bool requestingPermissions;
#endif

        public AndroidBagTransport(bool startFreshSession = false)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                bridge = new AndroidJavaClass("com.travelagent.ble.BleBridge");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    bridge.CallStatic("initialize", activity);
                    if (startFreshSession) bridge.CallStatic("beginSession");
                }
            }
            catch (Exception) { snapshot.status = "Android Bluetooth plugin could not initialize. Check the Android build and retry."; }
#endif
        }

        public void Scan() { WithPermissions(() => Call("scan")); }
        public void Connect(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) { Report("Select a discovered board before connecting."); return; }
            WithPermissions(() => Call("connect", deviceId));
        }
        public void Disconnect()
        {
            Call("disconnect");
            MarkDisconnected("Disconnected. The board keeps its own guard and recording state.");
        }
        public void ClaimOwnership()
        {
            if (!snapshot.connected || snapshot.stale) { Report("Connect and read a current board snapshot first."); return; }
            if (snapshot.guardState != "disarmed") { Report("Disarm the board physically before authorizing this phone."); return; }
            Call("claimOwnership");
        }

        public void Send(DeviceCommand command)
        {
            if (command == null || string.IsNullOrEmpty(command.operation)) { Report("Choose a valid device operation."); return; }
            bool readOnly = command.operation == "getSnapshot";
            if (!snapshot.connected || (snapshot.stale && !readOnly)) { Report("No current Bluetooth snapshot. Reconnect before sending commands."); return; }
            if (pending != null) { Report("A device command is still pending. Wait for confirmation or timeout."); return; }
            if (!readOnly && !snapshot.authenticated) { Report("Authorize this phone on the disarmed board before changing settings or reading stored events."); return; }
            if (!readOnly && Array.IndexOf(snapshot.capabilities ?? new string[0], command.operation) < 0)
            { Report("This firmware does not support " + command.operation + ". See the firmware handoff."); return; }
            if (command.operation == "setButtonSequences")
            {
                string validation;
                if (!ValidSequences(command.armSequence, command.disarmSequence, out validation)) { Report(validation); return; }
                if (snapshot.guardState != "disarmed") { Report("Sequence changes require a confirmed disarmed board."); return; }
            }
            pending = command.Copy();
            if (string.IsNullOrEmpty(pending.id)) pending.id = Guid.NewGuid().ToString("N");
            if (pending.operation == "setButtonSequences" && pending.expectedConfigRevision < 0) pending.expectedConfigRevision = snapshot.configRevision;
            pendingDevice = snapshot.deviceId; pendingBoot = snapshot.bootId; pendingSeconds = 0;
            snapshot.pendingCommandId = pending.id; snapshot.pendingOperation = pending.operation;
            snapshot.status = "Waiting for board confirmation: " + pending.operation;
            Publish();
            Call("send", JsonUtility.ToJson(pending));
        }

        public void Tick(float deltaSeconds)
        {
            if (disposed) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                for (int i = 0; i < 64; i++)
                {
                    string json;
                    try { json = bridge.CallStatic<string>("poll"); }
                    catch (Exception) { Report("Android Bluetooth event polling failed."); break; }
                    if (string.IsNullOrEmpty(json)) break;
                    Receive(json);
                }
            }
#endif
            if (snapshot.connected)
            {
                silentSeconds += Mathf.Max(0, deltaSeconds);
                if (silentSeconds > 8 && !snapshot.stale) { snapshot.stale = true; snapshot.status = "Board status is stale. Reconnect or request a fresh snapshot."; Publish(); }
            }
            if (pending != null)
            {
                pendingSeconds += Mathf.Max(0, deltaSeconds);
                if (pendingSeconds > 10) Finish(false, "No execution acknowledgement. Read the board's current state before retrying.");
            }
        }

        private void Receive(string json)
        {
            BleEnvelope message;
            try { message = JsonUtility.FromJson<BleEnvelope>(json); }
            catch (Exception) { Report("A Bluetooth message could not be read. Reconnect to refresh the board state."); return; }
            if (message == null) return;
            if (message.type == "notice") { Report(message.status); return; }
            if (message.type == "device") { if (message.device != null) DeviceDiscovered?.Invoke(message.device); return; }
            if (message.type == "transport")
            {
                if (!message.connected) MarkDisconnected(message.status);
                else { snapshot.connected = true; snapshot.stale = true; snapshot.authenticated = false; snapshot.status = message.status; silentSeconds = 0; Publish(); }
                return;
            }
            if (message.protocolVersion != 1) { Report("Unsupported board protocol version. Firmware update required."); return; }
            if (message.type == "ack")
            {
                // Authentication acknowledgements may carry a fresh snapshot without a Unity command.
                bool matches = pending != null && message.id == pending.id && message.operation == pending.operation && message.deviceId == pendingDevice && message.bootId == pendingBoot;
                if (matches)
                {
                    bool validIdentity = message.snapshot != null && message.snapshot.deviceId == pendingDevice && message.snapshot.bootId == pendingBoot;
                    if (message.ok && validIdentity && pending.operation == "setButtonSequences" &&
                        (message.snapshot.armSequence != pending.armSequence || message.snapshot.disarmSequence != pending.disarmSequence || message.snapshot.configRevision <= pending.expectedConfigRevision))
                    { Finish(false, "Acknowledgement did not confirm the requested sequences and revision. Read the board configuration before retrying."); return; }
                    bool confirmed = validIdentity && ApplySnapshot(message.snapshot);
                    Finish(message.ok && confirmed, message.ok && !confirmed
                        ? "Board acknowledged without a state snapshot. Execution needs confirmation."
                        : message.ok ? "Confirmed by board: " + pending.operation : "Board rejected command: " + message.error);
                }
                else if (snapshot.connected && message.id != null && message.id.StartsWith("native-", StringComparison.Ordinal) &&
                         (message.operation == "authenticate" || message.operation == "claimOwner" || message.operation == "getSnapshot") &&
                         message.snapshot != null && message.deviceId == message.snapshot.deviceId && message.bootId == message.snapshot.bootId)
                {
                    if (message.snapshot != null) ApplySnapshot(message.snapshot);
                    Report(message.operation == "getSnapshot" ? "Current board snapshot received." :
                        message.ok ? "Phone authorization confirmed by board." : "Phone authorization rejected: " + message.error);
                }
                return;
            }
            if (message.type == "snapshot" && message.snapshot != null) { ApplySnapshot(message.snapshot); return; }
            if (message.type == "motion" && message.sample != null)
            {
                MotionSample sample = message.sample;
                if (!snapshot.connected || snapshot.stale || !snapshot.authenticated || !sample.IsValid ||
                    sample.deviceId != snapshot.deviceId || sample.bootId != snapshot.bootId ||
                    Array.IndexOf(snapshot.capabilities, "liveMotion") < 0 || sample.sequence <= lastMotionSequence ||
                    sample.elapsedMs < lastMotionElapsed) return;
                lastMotionSequence = sample.sequence; lastMotionElapsed = sample.elapsedMs;
                MotionReceived?.Invoke(sample.Copy());
                return;
            }
            if (message.type == "event" && message.bagEvent != null)
            {
                BagEvent item = message.bagEvent;
                if (!snapshot.connected || !snapshot.authenticated || item.protocolVersion != 1 || item.deviceId != snapshot.deviceId || string.IsNullOrEmpty(item.bootId) || item.sequence < 1)
                { Report("An event has an unconfirmed device/session identity and was not imported."); return; }
                bool liveConnected = item.quality != null && item.quality.IndexOf("live_connected", StringComparison.Ordinal) >= 0;
                if (liveConnected && (snapshot.stale || item.bootId != snapshot.bootId || item.elapsedMs < 0 ||
                    Array.IndexOf(snapshot.capabilities, "liveEvents") < 0 || !string.IsNullOrEmpty(item.recordedAtUtc))) return;
                item.origin = "Live device";
                if (string.IsNullOrEmpty(item.quality)) item.quality = "Time/measurement quality not supplied by firmware";
                EventReceived?.Invoke(item);
            }
        }

        private bool ApplySnapshot(DeviceSnapshot next)
        {
            if (!snapshot.connected) { Report("A late board snapshot was ignored after disconnect. Reconnect to refresh state."); return false; }
            if (string.IsNullOrEmpty(next.deviceId) || string.IsNullOrEmpty(next.bootId) || next.protocolVersion != 1)
            { Report("Board snapshot lacks versioned device/session identity. Firmware update required."); return false; }
            if (!expectingInitialSnapshot && !string.IsNullOrEmpty(snapshot.deviceId) && snapshot.deviceId != next.deviceId && snapshot.connected)
            { Report("Unexpected board identity. Reconnect and select the intended device."); return false; }
            if (pending != null && next.bootId != pendingBoot) Finish(false, "Board restarted before execution was confirmed. Read its current state before retrying.");
            string oldPending = snapshot.pendingCommandId, oldOperation = snapshot.pendingOperation;
            string lastCommandId = snapshot.lastCommandId; bool lastSuccess = snapshot.lastCommandSucceeded;
            if (next.capabilities == null) next.capabilities = new string[0];
            if (snapshot.deviceId != next.deviceId || snapshot.bootId != next.bootId)
            { lastMotionSequence = 0; lastMotionElapsed = -1; }
            snapshot = next.Copy(); snapshot.connected = true; snapshot.simulated = false; snapshot.stale = false; expectingInitialSnapshot = false;
            snapshot.lastSeenUtc = DateTime.UtcNow.ToString("o"); silentSeconds = 0;
            snapshot.pendingCommandId = oldPending; snapshot.pendingOperation = oldOperation;
            snapshot.lastCommandId = lastCommandId; snapshot.lastCommandSucceeded = lastSuccess;
            Publish(); return true;
        }

        private void Finish(bool success, string status)
        {
            if (pending != null) snapshot.lastCommandId = pending.id;
            snapshot.lastCommandSucceeded = success; snapshot.pendingCommandId = ""; snapshot.pendingOperation = "";
            pending = null; snapshot.status = status; Publish(); Notice?.Invoke(status);
        }
        private void MarkDisconnected(string status)
        {
            if (pending != null) Finish(false, "Connection lost before command execution was confirmed.");
            snapshot.connected = false; snapshot.stale = true; snapshot.authenticated = false;
            snapshot.status = status; expectingInitialSnapshot = true; Publish();
        }
        private void Publish() { SnapshotChanged?.Invoke(snapshot.Copy()); }
        private void Report(string status) { snapshot.status = status; Publish(); Notice?.Invoke(status); }
        private static bool ValidSequences(string arm, string disarm, out string error)
        {
            if (arm == null || disarm == null || arm.Length != 4 || disarm.Length != 4) { error = "Both sequences need exactly four A/B presses."; return false; }
            foreach (char value in arm + disarm) if (value != 'A' && value != 'B') { error = "Sequences may contain only A and B."; return false; }
            if (arm == disarm) { error = "Arm and disarm sequences must differ."; return false; }
            error = ""; return true;
        }

        private void Call(string method, string argument = null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge == null) { Report("Android Bluetooth plugin is unavailable in this build."); return; }
            try { if (argument == null) bridge.CallStatic(method); else bridge.CallStatic(method, argument); }
            catch (Exception) { Report("Android Bluetooth operation failed. Check permissions and retry."); }
#else
            Report("Real Bluetooth is available in the Android build. The Editor simulator is selected separately.");
#endif
        }

        private void WithPermissions(Action action)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (disposed || bridge == null) { Report("Android Bluetooth plugin is unavailable."); return; }
            if (requestingPermissions) { Report("Respond to Android's permission request first."); return; }
            int sdk;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
            string[] required = sdk >= 31 ? new[] { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" } : new[] { Permission.FineLocation };
            var missing = new System.Collections.Generic.List<string>();
            foreach (string permission in required) if (!Permission.HasUserAuthorizedPermission(permission)) missing.Add(permission);
            if (sdk >= 33 && !Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS")) missing.Add("android.permission.POST_NOTIFICATIONS");
            if (missing.Count == 0) { action(); return; }
            requestingPermissions = true;
            var responses = new System.Collections.Generic.HashSet<string>();
            permissionCallbacks = new PermissionCallbacks();
            Action<string> completed = permissionName =>
            {
                if (!responses.Add(permissionName) || responses.Count < missing.Count) return;
                requestingPermissions = false;
                foreach (string permission in required) if (!Permission.HasUserAuthorizedPermission(permission))
                { Report("Nearby devices permission is required. Allow it in Android app settings, then retry."); return; }
                if (sdk >= 33 && !Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS")) Report("Notifications are disabled. Bluetooth may connect, but phone alerts will be unavailable.");
                if (!disposed) action();
            };
            permissionCallbacks.PermissionGranted += completed;
            permissionCallbacks.PermissionDenied += completed;
            permissionCallbacks.PermissionRequestDismissed += completed;
            Permission.RequestUserPermissions(missing.ToArray(), permissionCallbacks);
#else
            action();
#endif
        }

        public void Dispose()
        {
            if (disposed) return;
            Disconnect(); disposed = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge?.Dispose(); bridge = null;
#endif
            SnapshotChanged = null; EventReceived = null; MotionReceived = null; Notice = null; DeviceDiscovered = null;
        }

        [Serializable] private sealed class BleEnvelope
        {
            public string type = "", id = "", deviceId = "", bootId = "", operation = "", error = "", status = "";
            public int protocolVersion = 0;
            public bool ok = false, connected = false;
            public DeviceSnapshot snapshot = null;
            public BagEvent bagEvent = null;
            public MotionSample sample = null;
            public DiscoveredDevice device = null;
        }
    }
}

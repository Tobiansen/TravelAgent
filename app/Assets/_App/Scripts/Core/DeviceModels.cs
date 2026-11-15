using System;

namespace TravelAgent.Core
{
    /// <summary>Device state confirmed by the selected transport. Pending fields never imply execution.</summary>
    [Serializable]
    public sealed class DeviceSnapshot
    {
        public int protocolVersion = 1;
        public bool connected;
        public bool stale = true;
        public bool simulated;
        public bool authenticated;
        public string deviceId = "";
        public string bootId = "";
        public string guardState = "unknown";
        public string recordingState = "idle";
        public string armSequence = "";
        public string disarmSequence = "";
        public string lastSeenUtc = "";
        public string status = "Disconnected";
        public string[] capabilities = new string[0];
        public int configRevision;
        public string motorState = "idle";
        public string reason = "none";
        public int codeProgress;
        public string pendingCommandId = "";
        public string pendingOperation = "";
        public string lastCommandId = "";
        public bool lastCommandSucceeded;

        public DeviceSnapshot Copy()
        {
            var copy = (DeviceSnapshot)MemberwiseClone();
            copy.capabilities = (string[])capabilities.Clone();
            return copy;
        }
    }

    [Serializable]
    public sealed class DeviceCommand
    {
        public string id = "";
        public string operation = "";
        public string desiredState = "";
        public string armSequence = "";
        public string disarmSequence = "";
        public string recordingMode = "";
        public long sinceSequence;
        public int expectedConfigRevision = -1;

        public static DeviceCommand Create(string operation)
        {
            return new DeviceCommand { id = Guid.NewGuid().ToString("N"), operation = operation };
        }

        public DeviceCommand Copy() { return (DeviceCommand)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class DiscoveredDevice
    {
        public string id = "";
        public string name = "";
        public int rssi;
        public bool simulated;
    }

    /// <summary>Original measurements, source identity and quality travel together through exports.</summary>
    [Serializable]
    public sealed class BagEvent
    {
        public int protocolVersion = 1;
        public string deviceId = "";
        public string bootId = "";
        public string type = "";
        public string recordedAtUtc = "";
        public string origin = "";
        public string quality = "";
        public string summary = "";
        public long sequence;
        public long elapsedMs;
        public float peakG;

        public BagEvent Copy() { return (BagEvent)MemberwiseClone(); }
    }

    /// <summary>Live connected-session measurements; device elapsed time is not an exact UTC timestamp.</summary>
    [Serializable]
    public sealed class MotionSample
    {
        public int protocolVersion;
        public string deviceId = "";
        public string bootId = "";
        public long sequence;
        public long elapsedMs = -1;
        // deltaG is abs(norm(acceleration)-1 g), not an impact peak or resting-vector departure.
        public float deltaG = float.NaN;
        public float gyroDps = float.NaN;
        public bool sensorFresh;
        public long sampleAgeMs = -1;
        public long droppedSamples;
        public MotionSample Copy() { return (MotionSample)MemberwiseClone(); }
        public bool IsValid => protocolVersion == 1 && !string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(bootId) &&
            sequence > 0 && elapsedMs >= 0 && sampleAgeMs >= -1 && sampleAgeMs <= uint.MaxValue && droppedSamples >= 0 &&
            (!sensorFresh || sampleAgeMs <= 300) &&
            !float.IsNaN(deltaG) && !float.IsInfinity(deltaG) && deltaG >= 0 && deltaG <= 4 &&
            !float.IsNaN(gyroDps) && !float.IsInfinity(gyroDps) && gyroDps >= 0 && gyroDps <= 10000;
    }

    public interface IBagTransport : IDisposable
    {
        event Action<DeviceSnapshot> SnapshotChanged;
        event Action<BagEvent> EventReceived;
        event Action<MotionSample> MotionReceived;
        event Action<string> Notice;
        event Action<DiscoveredDevice> DeviceDiscovered;
        DeviceSnapshot Snapshot { get; }
        void Scan();
        void Connect(string deviceId);
        void Disconnect();
        void Send(DeviceCommand command);
        void Tick(float deltaSeconds);
    }
}

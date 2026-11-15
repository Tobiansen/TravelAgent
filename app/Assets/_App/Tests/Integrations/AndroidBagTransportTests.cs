using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TravelAgent.Core;
using TravelAgent.Integrations;
using UnityEngine;

namespace TravelAgent.Tests.Integrations
{
    /// <summary>Protocol-state tests only. JSON is injected at the native receive seam; no radio or phone is exercised.</summary>
    public sealed class AndroidBagTransportTests
    {
        private AndroidBagTransport transport;
        private static readonly MethodInfo Receive = typeof(AndroidBagTransport).GetMethod("Receive", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            transport = new AndroidBagTransport();
            Wire(new Message { type = "transport", connected = true, status = "Native notification channel ready" });
            PushSnapshot(Confirmed());
        }

        [TearDown]
        public void TearDown() { transport.Dispose(); }

        [TestCase("old-request", "setButtonSequences", "bag-1", "boot-1")]
        [TestCase("save-codes", "setGuardState", "bag-1", "boot-1")]
        [TestCase("save-codes", "setButtonSequences", "bag-2", "boot-1")]
        [TestCase("save-codes", "setButtonSequences", "bag-1", "boot-previous")]
        public void AckMustMatchRequestOperationDeviceAndBoot(string id, string operation, string deviceId, string bootId)
        {
            transport.Send(ChangeCodes());
            Message response = Ack(Changed());
            response.id = id; response.operation = operation; response.deviceId = deviceId; response.bootId = bootId;
            Wire(response);
            Assert.That(transport.Snapshot.pendingCommandId, Is.EqualTo("save-codes"));
            Assert.That(transport.Snapshot.armSequence, Is.EqualTo("AABB"));
            Assert.That(transport.Snapshot.configRevision, Is.EqualTo(4));
        }

        [Test]
        public void CodeSaveRequiresMatchingAckAndNewConfirmedReadback()
        {
            transport.Send(ChangeCodes());
            Assert.That(transport.Snapshot.armSequence, Is.EqualTo("AABB"), "Send must not optimistically replace confirmed codes");
            Wire(Ack(Changed()));
            DeviceSnapshot state = transport.Snapshot;
            Assert.That(state.pendingCommandId, Is.Empty);
            Assert.That(state.lastCommandId, Is.EqualTo("save-codes"));
            Assert.That(state.lastCommandSucceeded, Is.True);
            Assert.That(state.armSequence, Is.EqualTo("ABAB"));
            Assert.That(state.disarmSequence, Is.EqualTo("AABB"));
            Assert.That(state.configRevision, Is.EqualTo(5));
        }

        [TestCase(0)]
        [TestCase(2)]
        public void UnsupportedProtocolCannotConfirmPendingExecution(int version)
        {
            transport.Send(ChangeCodes()); Message response = Ack(Changed()); response.protocolVersion = version;
            Wire(response);
            Assert.That(transport.Snapshot.pendingCommandId, Is.EqualTo("save-codes"));
            Assert.That(transport.Snapshot.armSequence, Is.EqualTo("AABB"));
            Assert.That(transport.Snapshot.status, Does.Contain("Unsupported board protocol"));
        }

        [Test]
        public void AckSnapshotMustMatchItsCorrelatedEnvelopeIdentity()
        {
            transport.Send(ChangeCodes()); DeviceSnapshot other = Changed(); other.deviceId = "bag-2";
            Wire(Ack(other));
            Assert.That(transport.Snapshot.lastCommandSucceeded, Is.False);
            Assert.That(transport.Snapshot.deviceId, Is.EqualTo("bag-1"));
            Assert.That(transport.Snapshot.configRevision, Is.EqualTo(4));
        }

        [TestCase(4, "ABAB", "AABB")]
        [TestCase(3, "ABAB", "AABB")]
        [TestCase(5, "AAAA", "AABB")]
        [TestCase(5, "ABAB", "BBBB")]
        public void InconsistentCodeReadbackCannotReplaceConfirmedConfiguration(int revision, string arm, string disarm)
        {
            transport.Send(ChangeCodes());
            DeviceSnapshot response = Changed(); response.configRevision = revision; response.armSequence = arm; response.disarmSequence = disarm;
            Wire(Ack(response));
            Assert.That(transport.Snapshot.lastCommandSucceeded, Is.False);
            Assert.That(transport.Snapshot.pendingCommandId, Is.Empty);
            Assert.That(transport.Snapshot.armSequence, Is.EqualTo("AABB"));
            Assert.That(transport.Snapshot.disarmSequence, Is.EqualTo("BABA"));
            Assert.That(transport.Snapshot.configRevision, Is.EqualTo(4));
        }

        [Test]
        public void AckWithoutSnapshotLeavesExecutionUnconfirmed()
        {
            transport.Send(ChangeCodes()); Wire(Ack(null));
            Assert.That(transport.Snapshot.lastCommandSucceeded, Is.False);
            Assert.That(transport.Snapshot.lastCommandId, Is.EqualTo("save-codes"));
            Assert.That(transport.Snapshot.armSequence, Is.EqualTo("AABB"));
            Assert.That(transport.Snapshot.status, Does.Contain("needs confirmation"));
        }

        [Test]
        public void NegativeAckRetainsConfirmedCodesAndExposesRejection()
        {
            transport.Send(ChangeCodes()); Message rejection = Ack(Confirmed()); rejection.ok = false; rejection.error = "NVS write failed";
            Wire(rejection);
            Assert.That(transport.Snapshot.lastCommandSucceeded, Is.False);
            Assert.That(transport.Snapshot.pendingCommandId, Is.Empty);
            Assert.That(transport.Snapshot.configRevision, Is.EqualTo(4));
            Assert.That(transport.Snapshot.status, Does.Contain("NVS write failed"));
        }

        [Test]
        public void CommandTimeoutDoesNotAlterConfirmedConfiguration()
        {
            transport.Send(ChangeCodes()); transport.Tick(10.1f);
            Assert.That(transport.Snapshot.lastCommandSucceeded, Is.False);
            Assert.That(transport.Snapshot.pendingCommandId, Is.Empty);
            Assert.That(transport.Snapshot.armSequence, Is.EqualTo("AABB"));
            Assert.That(transport.Snapshot.status, Does.Contain("No execution acknowledgement"));
        }

        [Test]
        public void StaleSnapshotBlocksWritesButAllowsExplicitRefresh()
        {
            transport.Tick(8.1f);
            Assert.That(transport.Snapshot.stale, Is.True);
            transport.Send(ChangeCodes());
            Assert.That(transport.Snapshot.pendingCommandId, Is.Empty);
            DeviceCommand refresh = DeviceCommand.Create("getSnapshot"); transport.Send(refresh);
            Assert.That(transport.Snapshot.pendingCommandId, Is.EqualTo(refresh.id));
        }

        [Test]
        public void FreshSnapshotClearsStalenessWithoutInventingACommandSuccess()
        {
            transport.Tick(8.1f); PushSnapshot(Confirmed());
            Assert.That(transport.Snapshot.stale, Is.False);
            Assert.That(transport.Snapshot.lastCommandId, Is.Empty);
            Assert.That(transport.Snapshot.lastCommandSucceeded, Is.False);
        }

        [Test]
        public void RebootSnapshotInvalidatesPendingCommandAndPreservesItsFailure()
        {
            transport.Send(ChangeCodes()); DeviceSnapshot reboot = Confirmed(); reboot.bootId = "boot-2";
            PushSnapshot(reboot);
            Assert.That(transport.Snapshot.bootId, Is.EqualTo("boot-2"));
            Assert.That(transport.Snapshot.pendingCommandId, Is.Empty);
            Assert.That(transport.Snapshot.lastCommandId, Is.EqualTo("save-codes"));
            Assert.That(transport.Snapshot.lastCommandSucceeded, Is.False);
            Wire(Ack(Changed()));
            Assert.That(transport.Snapshot.bootId, Is.EqualTo("boot-2"));
            Assert.That(transport.Snapshot.armSequence, Is.EqualTo("AABB"));
        }

        [Test]
        public void AuthenticatedReplayPreservesHistoricalBootIdentityAndOriginalTimeQuality()
        {
            var received = new List<BagEvent>(); transport.EventReceived += received.Add;
            Wire(Event("bag-1", "historical-boot", 17));
            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0].bootId, Is.EqualTo("historical-boot"));
            Assert.That(received[0].sequence, Is.EqualTo(17));
            Assert.That(received[0].recordedAtUtc, Is.Empty);
            Assert.That(received[0].quality, Is.EqualTo("device_elapsed_only"));
            Assert.That(received[0].origin, Is.EqualTo("Live device"));
        }

        [TestCase("different-bag", "boot-1", 1)]
        [TestCase("bag-1", "", 1)]
        [TestCase("bag-1", "boot-1", 0)]
        public void UnidentifiedOrWrongDeviceEventsAreNotImported(string device, string boot, long sequence)
        {
            int received = 0; transport.EventReceived += _ => received++;
            Wire(Event(device, boot, sequence)); Assert.That(received, Is.Zero);
        }

        [Test]
        public void UnauthenticatedDeviceCannotImportStoredEvidence()
        {
            DeviceSnapshot unauthenticated = Confirmed(); unauthenticated.authenticated = false; PushSnapshot(unauthenticated);
            int received = 0; transport.EventReceived += _ => received++;
            Wire(Event("bag-1", "previous-boot", 1)); Assert.That(received, Is.Zero);
            transport.Send(ChangeCodes()); Assert.That(transport.Snapshot.pendingCommandId, Is.Empty);
        }

        [Test]
        public void UnsupportedCapabilityCannotCreateAPendingWrite()
        {
            DeviceSnapshot limited = Confirmed(); limited.capabilities = new[] { "getSnapshot" }; PushSnapshot(limited);
            transport.Send(ChangeCodes());
            Assert.That(transport.Snapshot.pendingCommandId, Is.Empty);
            Assert.That(transport.Snapshot.lastCommandId, Is.Empty);
            Assert.That(transport.Snapshot.status, Does.Contain("does not support"));
        }

        [Test]
        public void LateSnapshotsAndNativeAuthorizationCannotResurrectDisconnectedState()
        {
            transport.Disconnect(); PushSnapshot(Confirmed());
            Message native = Ack(Confirmed()); native.id = "native-old"; native.operation = "authenticate";
            Wire(native);
            Assert.That(transport.Snapshot.connected, Is.False);
            Assert.That(transport.Snapshot.stale, Is.True);
            Assert.That(transport.Snapshot.authenticated, Is.False);
        }

        [Test]
        public void NewTransportConnectionMayConfirmADifferentSelectedBoard()
        {
            transport.Disconnect(); Wire(new Message { type = "transport", connected = true });
            DeviceSnapshot other = Confirmed(); other.deviceId = "bag-2"; other.bootId = "other-boot"; PushSnapshot(other);
            Assert.That(transport.Snapshot.deviceId, Is.EqualTo("bag-2"));
            Assert.That(transport.Snapshot.stale, Is.False);
        }

        [Test]
        public void LiveMotionRequiresCurrentOwnerCapabilityAndPreservesDeviceTime()
        {
            EnableMotion(); var received = new List<MotionSample>(); transport.MotionReceived += received.Add;
            Wire(Motion(1, 1234));
            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0].deviceId, Is.EqualTo("bag-1"));
            Assert.That(received[0].bootId, Is.EqualTo("boot-1"));
            Assert.That(received[0].elapsedMs, Is.EqualTo(1234));
            Assert.That(received[0].deltaG, Is.EqualTo(.25f));
            Assert.That(received[0].gyroDps, Is.EqualTo(22f));
            Assert.That(received[0].sampleAgeMs, Is.EqualTo(5));
            Assert.That(transport.Snapshot.lastCommandId, Is.Empty, "Samples never acknowledge an operation.");
        }

        [TestCase("bag-other", "boot-1", 1)]
        [TestCase("bag-1", "boot-old", 1)]
        [TestCase("bag-1", "boot-1", 0)]
        public void WrongMotionIdentityOrSequenceIsRejected(string device, string boot, long sequence)
        {
            EnableMotion(); int received = 0; transport.MotionReceived += _ => received++;
            var message = Motion(sequence, 100); message.sample.deviceId = device; message.sample.bootId = boot;
            Wire(message); Assert.That(received, Is.Zero);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void LiveMotionCannotBypassAuthenticationOrCapability(bool authenticated, bool capable)
        {
            var state = Confirmed(); state.authenticated = authenticated;
            if (capable) state.capabilities = new[] { "liveMotion" };
            PushSnapshot(state); int received = 0; transport.MotionReceived += _ => received++;
            Wire(Motion(1, 200)); Assert.That(received, Is.Zero);
        }

        [Test]
        public void MotionSequenceGapsArePreservedAndDuplicatesOrTimeRegressionAreRejected()
        {
            EnableMotion(); var received = new List<MotionSample>(); transport.MotionReceived += received.Add;
            Wire(Motion(1, 100)); Wire(Motion(1, 100)); Wire(Motion(2, 90)); Wire(Motion(4, 700));
            Assert.That(received, Has.Count.EqualTo(2));
            Assert.That(received[1].sequence, Is.EqualTo(4), "Coalesced/lost samples remain visible as sequence gaps.");
        }

        [Test]
        public void MotionDoesNotKeepAnOldSnapshotFreshAndIsRejectedAfterDisconnect()
        {
            EnableMotion(); int received = 0; transport.MotionReceived += _ => received++;
            transport.Tick(7.8f); Wire(Motion(1, 200)); transport.Tick(.3f); Wire(Motion(2, 400));
            Assert.That(transport.Snapshot.stale, Is.True);
            Assert.That(received, Is.EqualTo(1));
            transport.Disconnect(); Wire(Motion(3, 600)); Assert.That(received, Is.EqualTo(1));
        }

        [Test]
        public void NewBootResetsMotionCursorButNeverAcceptsTheOldBoot()
        {
            EnableMotion(); var received = new List<MotionSample>(); transport.MotionReceived += received.Add;
            Wire(Motion(90, 9000));
            var reboot = Confirmed(); reboot.capabilities = new[] { "liveMotion" }; reboot.bootId = "boot-2"; PushSnapshot(reboot);
            Wire(Motion(91, 9200)); var fresh = Motion(1, 100); fresh.sample.bootId = "boot-2"; Wire(fresh);
            Assert.That(received, Has.Count.EqualTo(2)); Assert.That(received[1].sequence, Is.EqualTo(1));
            Assert.That(received[1].bootId, Is.EqualTo("boot-2"));
        }

        [Test]
        public void UnhealthyMotionRemainsAnExplicitGapAndDoesNotBecomeAHealthyMeasurement()
        {
            EnableMotion(); MotionSample received = null; transport.MotionReceived += item => received = item;
            var message = Motion(1, 200); message.sample.sensorFresh = false; message.sample.sampleAgeMs = 500;
            message.sample.deltaG = 0; message.sample.gyroDps = 0; Wire(message);
            Assert.That(received, Is.Not.Null); Assert.That(received.sensorFresh, Is.False);
            Assert.That(received.sampleAgeMs, Is.EqualTo(500));
        }

        [TestCase(-1f, 1f)]
        [TestCase(4.01f, 1f)]
        [TestCase(1f, -1f)]
        [TestCase(1f, 10001f)]
        public void OutOfBoundsMeasurementsAreNotImported(float delta, float gyro)
        {
            EnableMotion(); int received = 0; transport.MotionReceived += _ => received++;
            var message = Motion(1, 200); message.sample.deltaG = delta; message.sample.gyroDps = gyro;
            Wire(message); Assert.That(received, Is.Zero);
        }

        [Test]
        public void MissingMeasurementsAndNonfiniteValuesFailValidation()
        {
            EnableMotion(); int received = 0; transport.MotionReceived += _ => received++;
            Receive.Invoke(transport, new object[] { "{\"type\":\"motion\",\"protocolVersion\":1,\"sample\":{\"protocolVersion\":1,\"deviceId\":\"bag-1\",\"bootId\":\"boot-1\",\"sequence\":1,\"elapsedMs\":1}}" });
            Assert.That(received, Is.Zero);
            var sample = Motion(1, 200).sample; sample.deltaG = float.NaN; Assert.That(sample.IsValid, Is.False);
            sample.deltaG = 0; sample.gyroDps = float.PositiveInfinity; Assert.That(sample.IsValid, Is.False);
            sample.gyroDps = 0; sample.sampleAgeMs = 301; Assert.That(sample.IsValid, Is.False, "Old IMU data cannot be labelled fresh.");
            sample.sensorFresh = false; Assert.That(sample.IsValid, Is.True, "An explicitly unhealthy frame may describe a gap.");
        }

        [Test]
        public void ConnectedOnlyMarkersCannotImportHistoricalBootsOrInventUtc()
        {
            EnableMotion(); var received = new List<BagEvent>(); transport.EventReceived += received.Add;
            var old = Event("bag-1", "previous-boot", 1); old.bagEvent.quality = "device_elapsed_only;live_connected;no_offline_replay"; Wire(old);
            var current = Event("bag-1", "boot-1", 2); current.bagEvent.quality = old.bagEvent.quality;
            current.bagEvent.recordedAtUtc = "2026-11-14T21:00:00Z"; Wire(current);
            Assert.That(received, Is.Empty);
            current.bagEvent.recordedAtUtc = ""; Wire(current); Assert.That(received, Has.Count.EqualTo(1));
            transport.Tick(8.1f); current.bagEvent.sequence = 3; Wire(current); Assert.That(received, Has.Count.EqualTo(1));
        }

        private void EnableMotion() { var state = Confirmed(); state.capabilities = new[] { "getSnapshot", "liveMotion", "liveEvents" }; PushSnapshot(state); }
        private static Message Motion(long sequence, long elapsed) => new Message { type = "motion", sample = new MotionSample {
            protocolVersion = 1, deviceId = "bag-1", bootId = "boot-1", sequence = sequence, elapsedMs = elapsed,
            deltaG = .25f, gyroDps = 22, sensorFresh = true, sampleAgeMs = 5, droppedSamples = 0 } };

        private void Wire(Message message) { Receive.Invoke(transport, new object[] { JsonUtility.ToJson(message) }); }
        private void PushSnapshot(DeviceSnapshot state) { Wire(new Message { type = "snapshot", snapshot = state }); }
        private static DeviceCommand ChangeCodes() { return new DeviceCommand { id = "save-codes", operation = "setButtonSequences", armSequence = "ABAB", disarmSequence = "AABB", expectedConfigRevision = 4 }; }
        private static DeviceSnapshot Confirmed()
        {
            return new DeviceSnapshot {
                connected = true, stale = false, authenticated = true, deviceId = "bag-1", bootId = "boot-1", guardState = "disarmed",
                armSequence = "AABB", disarmSequence = "BABA", configRevision = 4, status = "Confirmed state",
                capabilities = new[] { "getSnapshot", "setButtonSequences", "setGuardState", "getEventsSince" }
            };
        }
        private static DeviceSnapshot Changed() { DeviceSnapshot result = Confirmed(); result.armSequence = "ABAB"; result.disarmSequence = "AABB"; result.configRevision = 5; return result; }
        private static Message Ack(DeviceSnapshot state) { return new Message { type = "ack", id = "save-codes", operation = "setButtonSequences", deviceId = "bag-1", bootId = "boot-1", ok = true, snapshot = state }; }
        private static Message Event(string device, string boot, long sequence) { return new Message { type = "event", bagEvent = new BagEvent { deviceId = device, bootId = boot, sequence = sequence, elapsedMs = 1234, type = "movement", recordedAtUtc = "", quality = "device_elapsed_only" } }; }

        [Serializable] private sealed class Message
        {
            public string type = "", id = "", operation = "", deviceId = "", bootId = "", status = "", error = "";
            public int protocolVersion = 1;
            public bool ok, connected;
            public DeviceSnapshot snapshot;
            public BagEvent bagEvent;
            public MotionSample sample;
        }
    }
}

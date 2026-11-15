using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TravelAgent.Core;
using UnityEngine;

namespace TravelAgent.Tests
{
    public sealed class DomainTests
    {
        [TestCase("AABB", "BABA", true)]
        [TestCase("AAAA", "BBBB", true)]
        [TestCase("ABAB", "ABAB", false)]
        [TestCase("aabb", "BABA", false)]
        [TestCase("ABCZ", "BABA", false)]
        [TestCase("AAB", "BABA", false)]
        [TestCase(null, "BABA", false)]
        public void SequencesRequireDistinctFourSymbolValues(string arm, string disarm, bool expected)
        {
            Assert.That(SequenceRules.Validate(arm, disarm, out _), Is.EqualTo(expected));
        }

        [Test]
        public void ParserCountsReleaseOnceAndRejectsBounce()
        {
            var parser = new ButtonSequenceParser();
            string sequence = null;
            parser.Completed += value => sequence = value;
            parser.Press('B', true); parser.Tick(0.01f); parser.Press('B', false);
            Assert.That(parser.Progress, Is.Zero);
            parser.Press('A', true); parser.Tick(2f); parser.Press('A', true); parser.Press('A', false);
            Assert.That(parser.Progress, Is.EqualTo(1));
            Tap(parser, 'A'); Tap(parser, 'B'); Tap(parser, 'B');
            Assert.That(sequence, Is.EqualTo("AABB"));
            Assert.That(parser.Progress, Is.Zero);
        }

        [Test]
        public void ParserTimeoutAndOverlapClearPartialEntry()
        {
            var parser = new ButtonSequenceParser();
            Tap(parser, 'A'); parser.Tick(2.01f);
            Assert.That(parser.Progress, Is.Zero);
            Tap(parser, 'A'); parser.Press('A', true); parser.Press('B', true);
            parser.Tick(0.1f); parser.Press('A', false); parser.Press('B', false);
            Assert.That(parser.Progress, Is.Zero);
            Tap(parser, 'B');
            Assert.That(parser.Progress, Is.EqualTo(1));
        }

        [Test]
        public void TransitionWhileButtonHeldRequiresRelease()
        {
            var parser = new ButtonSequenceParser();
            Tap(parser, 'A'); parser.Press('B', true); parser.Tick(0.1f); parser.Reset(); parser.Press('B', false);
            Assert.That(parser.Progress, Is.Zero);
            Tap(parser, 'A');
            Assert.That(parser.Progress, Is.EqualTo(1));
        }

        [Test]
        public void ConfigurationChangesOnlyAfterAcknowledgementAndPersistsOnSimulatedReboot()
        {
            using (var board = Connected())
            {
                var command = DeviceCommand.Create("setButtonSequences");
                command.armSequence = "ABAB"; command.disarmSequence = "ABBA"; command.expectedConfigRevision = 1;
                board.Send(command);
                Assert.That(board.Snapshot.armSequence, Is.EqualTo("AABB"));
                Assert.That(board.Snapshot.pendingCommandId, Is.EqualTo(command.id));
                board.Tick(0.3f);
                Assert.That(board.Snapshot.armSequence, Is.EqualTo("ABAB"));
                Assert.That(board.Snapshot.configRevision, Is.EqualTo(2));
                board.Send(command); // Retransmission must not increment configuration twice.
                Assert.That(board.Snapshot.configRevision, Is.EqualTo(2));
                board.Reboot();
                Assert.That(board.Snapshot.armSequence, Is.EqualTo("ABAB"));
                Assert.That(board.Snapshot.disarmSequence, Is.EqualTo("ABBA"));
                Assert.That(board.Snapshot.guardState, Is.EqualTo("disarmed"));
            }
        }

        [Test]
        public void TimedOutOrStaleConfigurationCannotBecomeSaved()
        {
            using (var board = Connected())
            {
                board.SetFailure(true);
                var command = DeviceCommand.Create("setButtonSequences");
                command.armSequence = "AAAA"; command.disarmSequence = "BBBB";
                board.Send(command); board.Tick(5.1f);
                Assert.That(board.Snapshot.armSequence, Is.EqualTo("AABB"));
                Assert.That(board.Snapshot.lastCommandSucceeded, Is.False);
                Assert.That(board.Snapshot.pendingCommandId, Is.Empty);
                board.SetFailure(false);
                command = DeviceCommand.Create("setButtonSequences");
                command.armSequence = "AAAA"; command.disarmSequence = "BBBB"; command.expectedConfigRevision = 99;
                board.Send(command); board.Tick(0.3f);
                Assert.That(board.Snapshot.armSequence, Is.EqualTo("AABB"));
                Assert.That(board.Snapshot.lastCommandSucceeded, Is.False);
            }
        }

        [Test]
        public void ArmedBoardRejectsConfigurationAndUsesWarningBeforeAlarm()
        {
            using (var board = Connected())
            {
                Enter(board, "AABB");
                Assert.That(board.Snapshot.guardState, Is.EqualTo("arming"));
                board.Tick(5.1f);
                Assert.That(board.Snapshot.guardState, Is.EqualTo("armed"));
                var config = DeviceCommand.Create("setButtonSequences");
                config.armSequence = "AAAA"; config.disarmSequence = "BBBB";
                board.Send(config); board.Tick(0.3f);
                Assert.That(board.Snapshot.lastCommandSucceeded, Is.False);
                board.InjectMotion("bump");
                Assert.That(board.Snapshot.guardState, Is.EqualTo("armed"));
                board.InjectMotion("sustained");
                Assert.That(board.Snapshot.guardState, Is.EqualTo("warning"));
                board.Tick(1f);
                Assert.That(board.Snapshot.guardState, Is.EqualTo("warning"));
                board.Tick(1.1f);
                Assert.That(board.Snapshot.guardState, Is.EqualTo("alarm"));
                Enter(board, "BABA");
                Assert.That(board.Snapshot.guardState, Is.EqualTo("disarmed"));
            }
        }

        [Test]
        public void WrongDisarmDelayDoesNotBlockAuthenticatedOwnerCommand()
        {
            using (var board = Connected())
            {
                Enter(board, "AABB"); board.Tick(5.1f);
                Enter(board, "AAAA"); Enter(board, "AAAA"); Enter(board, "AAAA");
                Enter(board, "BABA");
                Assert.That(board.Snapshot.guardState, Is.EqualTo("armed"));
                var disarm = DeviceCommand.Create("setGuardState"); disarm.desiredState = "disarmed";
                board.Send(disarm); board.Tick(0.3f);
                Assert.That(board.Snapshot.guardState, Is.EqualTo("disarmed"));
            }
        }

        [Test]
        public void SensorFaultAndPhysicalMotorStopDoNotBecomeDisarm()
        {
            using (var board = Connected())
            {
                Enter(board, "AABB"); board.Tick(5.1f);
                board.InjectMotion("sensor_fault"); board.Tick(2.1f);
                Assert.That(board.Snapshot.guardState, Is.EqualTo("alarm"));
                Assert.That(board.Snapshot.reason, Is.EqualTo("sensor_fault"));
                Assert.That(board.Snapshot.motorState, Is.EqualTo("inhibited"));
                Enter(board, "BABA"); Enter(board, "AABB"); board.Tick(5.1f);
                board.InjectMotion("sustained"); board.Tick(2.1f);
                Assert.That(board.Snapshot.motorState, Is.EqualTo("commanded"));
                board.Press('A', true); board.Press('B', true); board.Tick(0.51f);
                board.Press('A', false); board.Press('B', false);
                Assert.That(board.Snapshot.motorState, Is.EqualTo("inhibited"));
                Assert.That(board.Snapshot.guardState, Is.EqualTo("alarm"));
            }
        }

        [Test]
        public void QuietTransitSuppressesOutputsAndDisarmPreservesRecording()
        {
            using (var board = Connected())
            {
                Enter(board, "AABB"); board.Tick(5.1f); board.InjectMotion("sustained"); board.Tick(2.1f);
                var transit = DeviceCommand.Create("setRecordingMode"); transit.recordingMode = "transit";
                board.Send(transit); board.Tick(0.3f);
                Assert.That(board.Snapshot.guardState, Is.EqualTo("disarmed"));
                Assert.That(board.Snapshot.motorState, Is.EqualTo("idle"));
                board.InjectMotion("impact"); board.Tick(3f);
                Assert.That(board.Snapshot.guardState, Is.EqualTo("disarmed"));
                var disarm = DeviceCommand.Create("setGuardState"); disarm.desiredState = "disarmed";
                board.Send(disarm); board.Tick(0.3f);
                Assert.That(board.Snapshot.recordingState, Is.EqualTo("transit"));
                var arm = DeviceCommand.Create("setGuardState"); arm.desiredState = "armed";
                board.Send(arm); board.Tick(0.3f);
                Assert.That(board.Snapshot.lastCommandSucceeded, Is.False);
            }
        }

        [Test]
        public void OfflineEventsReplayWithoutDuplicatingAppHistory()
        {
            using (var board = Connected())
            {
                var ledger = new EventLedger();
                board.EventReceived += value => ledger.Add(value);
                board.Disconnect();
                int prior = ledger.Events.Count;
                board.InjectMotion("impact"); board.InjectMotion("open");
                Assert.That(ledger.Events.Count, Is.EqualTo(prior));
                Assert.That(board.Snapshot.stale, Is.True);
                board.Connect(SimulatedBagTransport.SimulatedDeviceId);
                Assert.That(ledger.Events.Count, Is.GreaterThan(prior));
                int count = ledger.Events.Count;
                var replay = DeviceCommand.Create("getEventsSince"); replay.sinceSequence = 0;
                board.Send(replay); board.Tick(0.3f);
                Assert.That(ledger.Events.Count, Is.EqualTo(count));
            }
        }

        [Test]
        public void EventIdentityIncludesBootAndPreservesOriginalMeasurements()
        {
            var ledger = new EventLedger();
            var first = new BagEvent { deviceId = "bag", bootId = "boot1", sequence = 1, peakG = 2.4f };
            Assert.That(ledger.Add(first), Is.True);
            Assert.That(ledger.Add(first), Is.False);
            first.peakG = 999;
            Assert.That(ledger.Events[0].peakG, Is.EqualTo(2.4f));
            Assert.That(ledger.Add(new BagEvent { deviceId = "bag", bootId = "boot2", sequence = 1 }), Is.True);
        }

        [Test]
        public void DemoResetCannotEraseRealStateOrOriginalPhoto()
        {
            string root = Path.Combine(Path.GetTempPath(), "TravelAgent-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Func<AppData, string> encode = data => JsonUtility.ToJson(data);
                Func<string, AppData> decode = json => JsonUtility.FromJson<AppData>(json);
                var demo = new JsonAppStore(root, true, encode, decode);
                var real = new JsonAppStore(root, false, encode, decode);
                var data = new AppData(); data.journey.damageDescription = "User description";
                demo.Save(data); real.Save(data);
                string photo = Path.Combine(root, "original-photo.png"); File.WriteAllText(photo, "sample");
                data.journey.photos.Add(new PhotoEvidence { path = photo, origin = "sample fixture" });
                string export = demo.Export(data);
                Assert.That(File.Exists(Path.Combine(export, "images", "photo-01.png")), Is.True);
                demo.Reset();
                Assert.That(demo.Load().journey.damageDescription, Is.Empty);
                Assert.That(real.Load().journey.damageDescription, Is.EqualTo("User description"));
                Assert.That(File.Exists(photo), Is.True);
                Assert.Throws<InvalidOperationException>(() => real.Reset());
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void CorruptSaveRecoversBackupAndPreservesUnreadableOriginal()
        {
            string root = Path.Combine(Path.GetTempPath(), "TravelAgent-recovery-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new JsonAppStore(root, true, value => JsonUtility.ToJson(value), json => JsonUtility.FromJson<AppData>(json));
                var data = new AppData(); data.journey.damageDescription = "Earlier description"; store.Save(data);
                data.journey.damageDescription = "Latest description"; store.Save(data);
                File.WriteAllText(Path.Combine(store.RootDirectory, "app-state.json"), "{broken json");
                AppData recovered = store.Load();
                Assert.That(recovered.journey.damageDescription, Is.EqualTo("Earlier description"));
                Assert.That(store.LastWarning, Does.Contain("Recovered"));
                Assert.That(Directory.GetFiles(store.RootDirectory, "unreadable-*.json"), Has.Length.EqualTo(1));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void NewerSchemaCannotBeSilentlyReplacedByOldBackup()
        {
            string root = Path.Combine(Path.GetTempPath(), "TravelAgent-schema-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new JsonAppStore(root, true, value => JsonUtility.ToJson(value), json => JsonUtility.FromJson<AppData>(json));
                store.Save(new AppData()); store.Save(new AppData());
                string path = Path.Combine(store.RootDirectory, "app-state.json");
                File.WriteAllText(path, "{\"schemaVersion\":2}");
                Assert.Throws<NotSupportedException>(() => store.Load());
                Assert.That(File.ReadAllText(path), Does.Contain("2"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static SimulatedBagTransport Connected()
        {
            var board = new SimulatedBagTransport(new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc));
            board.Connect(SimulatedBagTransport.SimulatedDeviceId);
            return board;
        }
        private static void Tap(ButtonSequenceParser parser, char button)
        { parser.Press(button, true); parser.Tick(0.04f); parser.Press(button, false); }
        private static void Enter(SimulatedBagTransport board, string sequence)
        { foreach (char button in sequence) board.Tap(button); }
    }
}

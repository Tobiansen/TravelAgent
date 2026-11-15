#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TravelAgent.Core;
using TravelAgent.Integrations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TravelAgent.Tests.PlayMode
{
    public sealed class LiveMotionFlowTests
    {
        private AppComposition app;
        private AndroidBagTransport adapter;
        private string storage;
        private readonly MethodInfo receive = typeof(AndroidBagTransport).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic);

        [Serializable] private sealed class Packet
        {
            public string type;
            public int protocolVersion = 1;
            public bool connected;
            public DeviceSnapshot snapshot;
            public MotionSample sample;
        }
        [UnitySetUp] public IEnumerator SetUp()
        {
            storage = Path.Combine(Path.GetTempPath(), "TravelAgent-live-motion-" + Guid.NewGuid().ToString("N"));
            AppComposition.TestStorageRoot = storage;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_App/Scenes/App.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            app = UnityEngine.Object.FindAnyObjectByType<AppComposition>();
            app.SetHardware(false); adapter = (AndroidBagTransport)app.Transport;
            Connect("boot-a"); app.Navigate(0);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            var scene = app.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("Live-motion-cleanup-" + Guid.NewGuid().ToString("N")));
            yield return SceneManager.UnloadSceneAsync(scene);
            app = null; adapter = null; AppComposition.TestStorageRoot = null;
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
        }
        private void Deliver(Packet packet) => receive.Invoke(adapter, new object[] { JsonUtility.ToJson(packet) });
        private void Connect(string boot)
        {
            Deliver(new Packet { type = "transport", connected = true });
            Deliver(new Packet { type = "snapshot", snapshot = new DeviceSnapshot { deviceId = "live-bag", bootId = boot,
                connected = true, authenticated = true, guardState = "disarmed", armSequence = "ABAB", disarmSequence = "ABBA",
                configRevision = 5, capabilities = new[] { "liveMotion", "liveEvents", "setGuardState", "setButtonSequences" } } });
        }
        private void Sample(long sequence, long elapsed, string boot = "boot-a") => Deliver(new Packet { type = "motion", sample = new MotionSample {
            protocolVersion = 1, deviceId = "live-bag", bootId = boot, sequence = sequence, elapsedMs = elapsed,
            deltaG = .08f, gyroDps = 2, sensorFresh = true, sampleAgeMs = 10 } });

        [UnityTest] public IEnumerator ConnectedSamplesRenderAndPersistWithoutBecomingDisturbanceEvents()
        {
            for (int index = 1; index <= 21; index++) Sample(index, 1000 + index * 200);
            Assert.That(app.LiveMotionHistory.Count, Is.EqualTo(21));
            Assert.That(app.Data.motionSamples.Count, Is.EqualTo(21));
            Assert.That(app.Data.events, Is.Empty, "Routine measurements must not create inspection events.");
            app.SaveJourney();
            var saved = JsonUtility.FromJson<AppData>(File.ReadAllText(Path.Combine(storage, "Real", "app-state.json")));
            Assert.That(saved.motionSamples.Count, Is.EqualTo(21));
            Assert.That(saved.motionSamples[20].elapsedMs, Is.EqualTo(5200));
            Assert.That(app.view.timeline.scaleLabel.text, Does.Contain("g"));
            Assert.That(app.view.timeline.captionLabel.text, Does.Not.Contain("UTC"));
            yield return null;
        }
        [UnityTest] public IEnumerator NewJourneyClearsPriorTraceAndAcceptsTheContinuingRealStream()
        {
            Sample(1, 1000); Sample(2, 1200);
            Assert.That(app.BeginFreshJourney(), Is.True);
            Assert.That(app.LiveMotionHistory.Count, Is.Zero);
            Assert.That(app.Data.motionSamples, Is.Empty);
            Assert.That(Directory.GetFiles(Path.Combine(storage, "Real", "JourneyHistory")), Is.Not.Empty);
            Sample(3, 1400);
            Assert.That(app.LiveMotionHistory.Count, Is.EqualTo(1));
            Assert.That(app.Data.motionSamples[0].sequence, Is.EqualTo(3));
            yield return null;
        }
        [UnityTest] public IEnumerator DisconnectRejectsSamplesAndReconnectStartsASeparateLineSegment()
        {
            Sample(1, 1000); Sample(2, 1200);
            Deliver(new Packet { type = "transport", connected = false });
            Sample(3, 1400);
            Assert.That(app.LiveMotionHistory.Count, Is.EqualTo(2));
            Connect("boot-a"); Sample(3, 1600);
            Assert.That(app.LiveMotionHistory.Count, Is.EqualTo(3));
            Assert.That(app.LiveMotionHistory[2].BreakBefore, Is.True);
            yield return null;
        }
        [UnityTest] public IEnumerator NewBootResetsMeasuredAxisAndRejectsOldBootSamples()
        {
            Sample(1, 5000); Sample(2, 5200);
            Connect("boot-b"); Sample(3, 5400);
            Assert.That(app.LiveMotionHistory.Count, Is.EqualTo(2));
            Sample(1, 100, "boot-b");
            Assert.That(app.LiveMotionHistory.Count, Is.EqualTo(1));
            Assert.That(app.Data.motionSamples.Count, Is.EqualTo(1));
            Assert.That(app.Data.motionSamples[0].bootId, Is.EqualTo("boot-b"));
            yield return null;
        }
    }
}
#endif

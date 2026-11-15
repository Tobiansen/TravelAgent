#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using TMPro;
using TravelAgent.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TravelAgent.Tests.PlayMode
{
    public sealed class LiveMotionTimelineTests
    {
        private GameObject root;
        private DisturbanceTimeline timeline;
        private EventSystem events;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            root = new GameObject("LiveMotion-test-canvas", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var graph = new GameObject("LiveMotion-test-graph", typeof(RectTransform), typeof(CanvasRenderer));
            graph.transform.SetParent(root.transform, false);
            var rect = (RectTransform)graph.transform; rect.sizeDelta = new Vector2(400, 200);
            timeline = graph.AddComponent<DisturbanceTimeline>();
            timeline.titleLabel = Label("Title"); timeline.scaleLabel = Label("Scale");
            timeline.captionLabel = Label("Caption"); timeline.emptyLabel = Label("Empty"); timeline.selectionLabel = Label("Selection");
            timeline.yAxisLabels = new[] { Label("Axis0"), Label("Axis1"), Label("Axis2"), Label("Axis3") };
            events = EventSystem.current;
            if (!events) events = root.AddComponent<EventSystem>();
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator TearDown() { UnityEngine.Object.Destroy(root); yield return null; }

        [UnityTest]
        public IEnumerator MeasuredTraceUsesGUnitsAndMovementFlagsOpenOriginalEventDetails()
        {
            var history = History();
            var marker = new BagEvent { deviceId = "bag", bootId = "boot", sequence = 9, elapsedMs = 1500,
                type = "movement", origin = "Live device", summary = "Observed motion threshold crossing", quality = "device_elapsed_only; utc_unanchored" };
            timeline.SetEvents(new[] { marker }, false); timeline.SetMotionHistory(history, true);
            Assert.That(timeline.scaleLabel.text, Is.EqualTo("Acceleration change · g"));
            Assert.That(timeline.yAxisLabels[0].text, Is.EqualTo("0")); Assert.That(timeline.yAxisLabels[3].text, Is.EqualTo("0.5"));
            Assert.That(timeline.captionLabel.text, Does.StartWith("LIVE BLE").And.Contain("device elapsed"));
            Assert.That(timeline.emptyLabel.gameObject.activeSelf, Is.False);
            BagEvent selected = null; timeline.Selected += item => selected = item;
            Canvas.ForceUpdateCanvases();
            var touch = new PointerEventData(events) { position = RectTransformUtility.WorldToScreenPoint(null, timeline.rectTransform.TransformPoint(Vector3.zero)) };
            timeline.OnPointerClick(touch);
            Assert.That(selected, Is.Not.Null); Assert.That(selected.sequence, Is.EqualTo(9));
            Assert.That(selected.elapsedMs, Is.EqualTo(1500)); Assert.That(selected.recordedAtUtc, Is.Empty);
            Assert.That(selected.peakG, Is.Zero, "A top-of-chart event flag cannot invent a measured peak.");
            marker.summary = "outside mutation"; Assert.That(selected.summary, Is.EqualTo("Observed motion threshold crossing"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SwitchingToDemoRestoresCategoricalLabelsAndDoesNotPresentRealSensorSignalAsFixtureData()
        {
            timeline.SetEvents(null, false); timeline.SetMotionHistory(History(), true);
            Assert.That(timeline.titleLabel.text, Is.EqualTo("Live motion"));
            timeline.SetEvents(new[] { new BagEvent { deviceId = "demo", bootId = "fixture", sequence = 1,
                type = "impact", recordedAtUtc = "2026-11-14T12:00:00Z", origin = "Demo fixture", quality = "Illustrative; no acceleration supplied" } }, true);
            Assert.That(timeline.scaleLabel.text, Does.Contain("not acceleration"));
            Assert.That(timeline.yAxisLabels[0].text, Is.EqualTo("Bump")); Assert.That(timeline.yAxisLabels[3].text, Is.EqualTo("Impact"));
            Assert.That(timeline.captionLabel.text, Does.StartWith("DEMO"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator EmptyAndDisconnectedMeasuredSessionsStayExplicitAboutTheirOrigin()
        {
            timeline.SetEvents(null, false); timeline.SetMotionHistory(new MotionHistory(), true);
            Assert.That(timeline.emptyLabel.text, Is.EqualTo("Waiting for motion samples"));
            Assert.That(timeline.emptyLabel.gameObject.activeSelf, Is.True);
            var history = History(); history.Break(); history.Add(Sample(3, 2200, .1f));
            timeline.SetMotionHistory(history, false);
            Assert.That(timeline.captionLabel.text, Does.StartWith("SAVED PHONE SESSION").And.Contain("gaps"));
            Assert.That(timeline.captionLabel.text, Does.Not.Contain("UTC"));
            yield return null;
        }

        private TMP_Text Label(string name)
        {
            var label = new GameObject("LiveMotion-test-" + name, typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(root.transform, false); return label.GetComponent<TMP_Text>();
        }
        private static MotionHistory History()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 1000, .1f)); history.Add(Sample(2, 2000, .4f)); return history;
        }
        private static MotionSample Sample(long sequence, long elapsed, float delta) => new MotionSample
        { protocolVersion = 1, deviceId = "bag", bootId = "boot", sequence = sequence, elapsedMs = elapsed, deltaG = delta, gyroDps = 3, sensorFresh = true };
    }
}
#endif

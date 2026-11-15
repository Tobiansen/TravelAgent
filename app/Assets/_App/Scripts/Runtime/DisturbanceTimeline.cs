using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using TravelAgent.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TravelAgent
{
    /// <summary>Saved uGUI graphic for measured live motion or explicitly categorical Demo events.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DisturbanceTimeline : MaskableGraphic, IPointerClickHandler
    {
        [Header("Saved labels")]
        public TMP_Text titleLabel, scaleLabel, captionLabel, emptyLabel, selectionLabel;
        public TMP_Text[] yAxisLabels = Array.Empty<TMP_Text>();
        [Header("Plot")]
        [Range(1, 100)] public int maximumEvents = 100;
        [Min(0)] public float padding = 22;
        [Min(1)] public float stemWidth = 4;
        [Min(2)] public float markerRadius = 7;
        [Min(8)] public float hitRadius = 28;
        [Header("Palette")]
        [SerializeField] private Color foreground = new Color32(244, 242, 241, 255);
        [SerializeField] private Color muted = new Color32(168, 168, 177, 255);
        [SerializeField] private Color spike = new Color32(255, 51, 71, 255);
        [SerializeField] private Color grid = new Color32(58, 61, 69, 255);

        public event Action<BagEvent> Selected;
        private DisturbanceProjection projection = DisturbanceProjection.Create(null);
        private bool simulated;
        private int selected = -1;
        private string selectedKey = "";
        private MotionHistory motionHistory;
        private MotionChartProjection motionProjection = MotionChartProjection.Create(null, null);
        private readonly List<BagEvent> eventRecords = new List<BagEvent>();
        private bool motionConnected;
        private bool Measured => !simulated && motionHistory != null;
        private static readonly float[] CategoryLevels = { .24f, .46f, .66f, .88f };
        private static readonly float[] MeasuredLevels = { 0f, 1f / 3f, 2f / 3f, 1f };
        private static readonly string[] Categories = { "Bump", "Movement", "Opening", "Impact" };

        /// <summary>Null retains the discrete-event fallback. A non-null history enables the measured axis.</summary>
        public void SetMotionHistory(MotionHistory history, bool connected)
        {
            motionHistory = history; motionConnected = connected;
            motionProjection = MotionChartProjection.Create(history, eventRecords, maximumEvents);
            RestoreSelection(); UpdateLabels(); SetVerticesDirty();
        }

        public void SetPalette(Color foreground, Color muted, Color spike, Color grid)
        {
            this.foreground = foreground; this.muted = muted; this.spike = spike; this.grid = grid;
            UpdateLabels(); SetVerticesDirty();
        }

        public void SetEvents(IEnumerable<BagEvent> events, bool simulated)
        {
            this.simulated = simulated;
            eventRecords.Clear();
            if (events != null) foreach (BagEvent item in events) if (item != null) eventRecords.Add(item.Copy());
            projection = DisturbanceProjection.Create(eventRecords, maximumEvents);
            motionProjection = MotionChartProjection.Create(motionHistory, eventRecords, maximumEvents);
            RestoreSelection();
            UpdateLabels(); SetVerticesDirty();
        }

        private void RestoreSelection()
        {
            selected = -1;
            if (Measured)
            {
                for (int index = 0; index < motionProjection.Markers.Count; index++)
                    if (Key(motionProjection.Markers[index].Event) == selectedKey) selected = index;
            }
            else for (int index = 0; index < projection.Points.Count; index++)
                    if (Key(projection.Points[index].Event) == selectedKey) selected = index;
            if (selected < 0) selectedKey = "";
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = true;
            UpdateLabels();
        }
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            UpdateAxisLabels();
        }

        private Rect PlotRect()
        {
            Rect rect = GetPixelAdjustedRect();
            float inset = Mathf.Min(padding, Mathf.Min(rect.width, rect.height) * 0.2f);
            return new Rect(rect.xMin + inset, rect.yMin + inset, Mathf.Max(1, rect.width - inset * 2), Mathf.Max(1, rect.height - inset * 2));
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Measured) { PopulateMeasured(mesh); return; }
            if (projection == null || projection.Points.Count == 0) return;
            Rect plot = PlotRect();
            foreach (float level in CategoryLevels)
                Quad(mesh, new Rect(plot.xMin, plot.yMin + level * plot.height, plot.width, 1), grid);
            for (int index = 0; index < projection.Points.Count; index++)
            {
                DisturbancePoint point = projection.Points[index];
                if (point.BreakBefore)
                {
                    float x = plot.xMin + (point.X + projection.Points[index - 1].X) * 0.5f * plot.width;
                    for (int dash = 0; dash < 6; dash++)
                        Quad(mesh, new Rect(x - 1, plot.yMin + dash * plot.height / 6, 2, plot.height / 13), muted * new Color(1, 1, 1, 0.45f));
                }
                float px = plot.xMin + point.X * plot.width, py = plot.yMin + point.CategoryHeight * plot.height;
                Color mark = index == selected ? foreground : spike;
                Quad(mesh, new Rect(px - stemWidth / 2, plot.yMin, stemWidth, py - plot.yMin), mark);
                Diamond(mesh, new Vector2(px, py), markerRadius + (index == selected ? 3 : 0), mark);
            }
        }

        private void PopulateMeasured(VertexHelper mesh)
        {
            Rect plot = PlotRect();
            foreach (float level in MeasuredLevels)
                Quad(mesh, new Rect(plot.xMin, plot.yMin + level * plot.height, plot.width, 1), grid);
            if (motionHistory == null || motionHistory.Count == 0) return;
            for (int index = 0; index < motionHistory.Count; index++)
            {
                MotionTracePoint point = motionHistory[index];
                Vector2 position = new Vector2(plot.xMin + motionProjection.X(point.ElapsedMs) * plot.width,
                    plot.yMin + motionProjection.Y(point.DeltaG) * plot.height);
                if (index == 0 || point.BreakBefore)
                {
                    Diamond(mesh, position, 2.5f, foreground);
                    continue;
                }
                MotionTracePoint prior = motionHistory[index - 1];
                Vector2 previous = new Vector2(plot.xMin + motionProjection.X(prior.ElapsedMs) * plot.width,
                    plot.yMin + motionProjection.Y(prior.DeltaG) * plot.height);
                Line(mesh, previous, position, Mathf.Max(2, stemWidth * .7f), foreground);
            }
            for (int index = 0; index < motionProjection.Markers.Count; index++)
            {
                float x = plot.xMin + motionProjection.Markers[index].X * plot.width;
                Color mark = index == selected ? foreground : spike;
                for (int dash = 0; dash < 7; dash++)
                    Quad(mesh, new Rect(x - stemWidth / 2, plot.yMin + dash * plot.height / 7, stemWidth, plot.height / 14), mark);
                // Flag at the top of the chart, deliberately independent of acceleration amplitude.
                Diamond(mesh, new Vector2(x, plot.yMax - markerRadius), markerRadius + (index == selected ? 3 : 0), mark);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Measured ? motionProjection.Markers.Count == 0 : projection == null || projection.Points.Count == 0) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
            Rect plot = PlotRect();
            int hit = Measured ? motionProjection.HitTest(local.x - plot.xMin, local.y - plot.yMin, plot.width, plot.height, hitRadius)
                : projection.HitTest(local.x - plot.xMin, local.y - plot.yMin, plot.width, plot.height, hitRadius);
            if (hit < 0) return;
            BagEvent item = Measured ? motionProjection.Markers[hit].Event : projection.Points[hit].Event;
            selected = hit; selectedKey = Key(item);
            UpdateLabels(); SetVerticesDirty();
            Selected?.Invoke(item.Copy());
        }

        private void UpdateLabels()
        {
            UpdateAxisLabels();
            if (Measured) { UpdateMeasuredLabels(); return; }
            if (titleLabel) { titleLabel.text = "Disturbances"; titleLabel.color = foreground; }
            if (scaleLabel) { scaleLabel.text = "Event categories · not acceleration"; scaleLabel.color = muted; }
            bool empty = projection == null || projection.Points.Count == 0;
            if (emptyLabel)
            {
                emptyLabel.gameObject.SetActive(empty);
                emptyLabel.text = simulated ? "Add a sample disturbance" : "No disturbance events received";
                emptyLabel.color = muted;
                emptyLabel.raycastTarget = false;
            }
            if (captionLabel)
            {
                captionLabel.color = muted;
                string axis = projection.TimeBasis == DisturbanceTimeBasis.EventOrder ? "Event order · not elapsed time"
                    : projection.AxisCaption.Replace("UTC · ", "UTC ").Replace(" UTC", "").Replace("DEVICE ELAPSED · one boot · ", "Boot elapsed · ");
                captionLabel.text = empty ? (simulated ? "DEMO · event categories" : "No live disturbance history supplied")
                    : (simulated ? "DEMO · " : "") + axis + (projection.IsWindowed ? " · last " + projection.Points.Count : "") +
                      (projection.HasSessionBreaks ? " · session/gap breaks" : "");
            }
            if (selectionLabel)
            {
                selectionLabel.color = selected >= 0 ? foreground : muted;
                if (selected < 0 || empty) selectionLabel.text = "Tap a spike to inspect";
                else
                {
                    DisturbancePoint point = projection.Points[selected];
                    string origin = string.IsNullOrWhiteSpace(point.Event.origin) ? "Origin not supplied" : point.Event.origin;
                    selectionLabel.text = point.Category + " · " + point.TimeLabel + " · " + origin + "\n" +
                        (point.HasReportedPeak ? "Event-supplied peak: " + point.Event.peakG.ToString("0.00", CultureInfo.InvariantCulture) + " g" : "Peak measurement not supplied") +
                        (point.Event.origin != null && point.Event.origin.IndexOf("simulat", StringComparison.OrdinalIgnoreCase) >= 0 ? " · simulated" : "");
                }
            }
        }

        private void UpdateMeasuredLabels()
        {
            if (titleLabel) { titleLabel.text = "Live motion"; titleLabel.color = foreground; }
            if (scaleLabel) { scaleLabel.text = "Acceleration change · g"; scaleLabel.color = muted; }
            bool empty = motionHistory == null || motionHistory.Count == 0;
            if (emptyLabel)
            {
                emptyLabel.gameObject.SetActive(empty); emptyLabel.raycastTarget = false; emptyLabel.color = muted;
                emptyLabel.text = motionConnected ? "Waiting for motion samples" : "Connect your bag for live motion";
            }
            if (captionLabel)
            {
                captionLabel.color = muted;
                captionLabel.text = empty ? "Live Bluetooth session · no samples yet" :
                    (motionConnected ? "LIVE BLE · " : "SAVED PHONE SESSION · ") + motionProjection.AxisCaption +
                    (motionProjection.HasGaps ? " · gaps" : "") + (motionHistory.DiscardedCount > 0 ? " · latest window" : "");
            }
            if (selectionLabel)
            {
                selectionLabel.color = selected >= 0 ? foreground : muted;
                selectionLabel.text = selected >= 0 && selected < motionProjection.Markers.Count
                    ? "Movement · boot +" + (motionProjection.Markers[selected].Event.elapsedMs / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + " s · Live device"
                    : "Tap a red marker to inspect · |magnitude - 1 g|";
            }
        }

        private void UpdateAxisLabels()
        {
            if (yAxisLabels == null) return;
            float height = rectTransform.rect.height;
            float inset = Mathf.Min(padding, height * .2f);
            for (int index = 0; index < yAxisLabels.Length && index < 4; index++)
            {
                TMP_Text label = yAxisLabels[index]; if (!label) continue;
                float level = Measured ? MeasuredLevels[index] : CategoryLevels[index];
                label.text = Measured ? (motionProjection.MaximumG * level).ToString("0.##", CultureInfo.InvariantCulture) : Categories[index];
                label.color = muted;
                float anchorY = (inset + Mathf.Max(1, height - inset * 2) * level) / Mathf.Max(1, height);
                var axisRect = label.rectTransform;
                axisRect.anchorMin = new Vector2(axisRect.anchorMin.x, anchorY);
                axisRect.anchorMax = new Vector2(axisRect.anchorMax.x, anchorY);
                axisRect.anchoredPosition = new Vector2(axisRect.anchoredPosition.x, 0);
            }
        }

        private static string Key(BagEvent item)
        {
            string device = item.deviceId ?? "", boot = item.bootId ?? "";
            return device.Length + ":" + device + boot.Length + ":" + boot + ":" + item.sequence;
        }
        private static void Quad(VertexHelper mesh, Rect rect, Color color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
        }
        private static void Diamond(VertexHelper mesh, Vector2 center, float radius, Color color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(center + Vector2.left * radius, color, Vector2.zero);
            mesh.AddVert(center + Vector2.up * radius, color, Vector2.zero);
            mesh.AddVert(center + Vector2.right * radius, color, Vector2.zero);
            mesh.AddVert(center + Vector2.down * radius, color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
        }
        private static void Line(VertexHelper mesh, Vector2 start, Vector2 end, float width, Color color)
        {
            Vector2 difference = end - start;
            if (difference.sqrMagnitude < .001f) { Diamond(mesh, start, width / 2, color); return; }
            Vector2 normal = new Vector2(-difference.y, difference.x).normalized * (width / 2);
            int first = mesh.currentVertCount;
            mesh.AddVert(start - normal, color, Vector2.zero); mesh.AddVert(start + normal, color, Vector2.zero);
            mesh.AddVert(end + normal, color, Vector2.zero); mesh.AddVert(end - normal, color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace TravelAgent.Core
{
    /// <summary>A disturbance flag at device elapsed time; its height is not a peak measurement.</summary>
    public sealed class MotionEventMarker
    {
        public BagEvent Event { get; internal set; }
        public float X { get; internal set; }
    }

    /// <summary>Measured sample scale and event/time mapping for a single visible boot.</summary>
    public sealed class MotionChartProjection
    {
        public long FirstElapsedMs { get; private set; }
        public long LastElapsedMs { get; private set; }
        public float MaximumG { get; private set; } = 0.25f;
        public bool HasGaps { get; private set; }
        public IReadOnlyList<MotionEventMarker> Markers { get; private set; }
        public string AxisCaption { get; private set; } = "Waiting for live samples";

        public static MotionChartProjection Create(MotionHistory history, IEnumerable<BagEvent> events, int maximumEvents = 100)
        {
            var result = new MotionChartProjection();
            var markers = new List<MotionEventMarker>();
            result.Markers = markers.AsReadOnly();
            if (history == null || history.Count == 0) return result;
            result.FirstElapsedMs = history[0].ElapsedMs;
            result.LastElapsedMs = history[history.Count - 1].ElapsedMs;
            float highest = 0;
            for (int index = 0; index < history.Count; index++)
            {
                highest = Math.Max(highest, history[index].DeltaG);
                if (index > 0 && history[index].BreakBefore) result.HasGaps = true;
            }
            result.MaximumG = NiceMaximum(highest);
            var candidates = new List<BagEvent>();
            if (events != null)
                foreach (BagEvent item in events)
                {
                    if (item == null || item.type != "movement" || item.deviceId != history.DeviceId || item.bootId != history.BootId ||
                        item.elapsedMs < result.FirstElapsedMs || item.elapsedMs > result.LastElapsedMs || item.elapsedMs <= 0) continue;
                    candidates.Add(item.Copy());
                }
            candidates.Sort((a, b) => a.elapsedMs != b.elapsedMs ? a.elapsedMs.CompareTo(b.elapsedMs) : a.sequence.CompareTo(b.sequence));
            int bound = Math.Max(1, Math.Min(100, maximumEvents));
            int start = Math.Max(0, candidates.Count - bound);
            for (int index = start; index < candidates.Count; index++)
                markers.Add(new MotionEventMarker { Event = candidates[index], X = result.X(candidates[index].elapsedMs) });
            double duration = (result.LastElapsedMs - result.FirstElapsedMs) / 1000d;
            result.AxisCaption = "0–" + duration.ToString("0.0", CultureInfo.InvariantCulture) + " s · device elapsed";
            return result;
        }

        public float X(long elapsedMs)
        {
            if (LastElapsedMs <= FirstElapsedMs) return 0.5f;
            return (float)Math.Max(0, Math.Min(1, ((double)elapsedMs - FirstElapsedMs) / (LastElapsedMs - FirstElapsedMs)));
        }
        public float Y(float deltaG) => Math.Max(0, Math.Min(1, deltaG / MaximumG));

        /// <summary>Markers are full-height flags; hit testing never turns their height into g.</summary>
        public int HitTest(float x, float y, float width, float height, float radius)
        {
            if (!Finite(x) || !Finite(y) || !Finite(width) || !Finite(height) || !Finite(radius) || width <= 0 || height <= 0 || radius < 0 || y < -radius || y > height + radius) return -1;
            int result = -1;
            float closest = radius;
            for (int index = 0; index < Markers.Count; index++)
            {
                float distance = Math.Abs(x - Markers[index].X * width);
                if (distance <= closest) { closest = distance; result = index; }
            }
            return result;
        }
        private static float NiceMaximum(float highest)
        {
            foreach (float candidate in new[] { 0.25f, 0.5f, 1f, 2f, 4f })
                if (highest <= candidate) return candidate;
            return Math.Max(4f, highest);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

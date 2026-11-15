using System;
using System.Collections.Generic;
using System.Globalization;

namespace TravelAgent.Core
{
    public enum DisturbanceTimeBasis { Utc, DeviceElapsed, EventOrder }

    /// <summary>A categorical event mark, never a reconstructed accelerometer sample.</summary>
    public sealed class DisturbancePoint
    {
        public BagEvent Event { get; internal set; }
        public float X { get; internal set; }
        public float CategoryHeight { get; internal set; }
        public string Category { get; internal set; }
        public string TimeLabel { get; internal set; }
        public bool BreakBefore { get; internal set; }
        public bool HasReportedPeak { get; internal set; }
    }

    /// <summary>
    /// Projects supplied disturbance events into a bounded chart. Heights identify event categories;
    /// neither category nor absence of an event establishes measured acceleration or quiet coverage.
    /// </summary>
    public sealed class DisturbanceProjection
    {
        public const int MaximumEvents = 100;
        public IReadOnlyList<DisturbancePoint> Points { get; private set; }
        public DisturbanceTimeBasis TimeBasis { get; private set; }
        public string AxisCaption { get; private set; }
        public int TotalEvents { get; private set; }
        public bool IsWindowed { get { return TotalEvents > Points.Count; } }
        public bool HasSessionBreaks { get; private set; }

        private sealed class Candidate
        {
            public BagEvent item;
            public int order;
            public DateTimeOffset utc;
            public bool validUtc;
            public float height;
            public string category;
        }

        public static DisturbanceProjection Create(IEnumerable<BagEvent> events, int maximumEvents = MaximumEvents)
        {
            int bound = Math.Max(1, Math.Min(MaximumEvents, maximumEvents));
            var candidates = new List<Candidate>();
            bool allUtc = true, allElapsed = true;
            string session = null;
            int order = 0;
            if (events != null)
            {
                foreach (BagEvent item in events)
                {
                    int sourceOrder = order++;
                    if (item == null || !Category(item.type, out string category, out float height)) continue;
                    var candidate = new Candidate { item = item.Copy(), order = sourceOrder, category = category, height = height };
                    candidate.validUtc = TryUtc(item.recordedAtUtc, out candidate.utc);
                    allUtc &= candidate.validUtc;
                    string nextSession = Session(item);
                    // Zero is also the default for an absent elapsed field. Do not manufacture an anchor from it.
                    allElapsed &= nextSession != null && item.elapsedMs > 0 && (session == null || session == nextSession);
                    if (session == null) session = nextSession;
                    candidates.Add(candidate);
                }
            }

            var result = new DisturbanceProjection { TotalEvents = candidates.Count, TimeBasis = DisturbanceTimeBasis.EventOrder };
            if (candidates.Count > 0 && allUtc)
            {
                result.TimeBasis = DisturbanceTimeBasis.Utc;
                candidates.Sort((a, b) => { int comparison = a.utc.CompareTo(b.utc); return comparison != 0 ? comparison : a.order.CompareTo(b.order); });
            }
            else if (candidates.Count > 0 && allElapsed)
            {
                result.TimeBasis = DisturbanceTimeBasis.DeviceElapsed;
                candidates.Sort((a, b) => { int comparison = a.item.elapsedMs.CompareTo(b.item.elapsedMs); return comparison != 0 ? comparison : a.order.CompareTo(b.order); });
            }
            if (candidates.Count > bound) candidates = candidates.GetRange(candidates.Count - bound, bound);
            double start = candidates.Count == 0 ? 0 : Position(candidates[0], result.TimeBasis);
            double end = candidates.Count == 0 ? 0 : Position(candidates[candidates.Count - 1], result.TimeBasis);
            if (candidates.Count > 1 && end <= start) result.TimeBasis = DisturbanceTimeBasis.EventOrder;
            var points = new List<DisturbancePoint>(candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
            {
                Candidate candidate = candidates[index];
                bool boundary = index > 0 && (!SameSession(candidates[index - 1].item, candidate.item) || HasGap(candidate.item) || HasGap(candidates[index - 1].item));
                result.HasSessionBreaks |= boundary;
                float x = candidates.Count == 1 ? 0.5f : result.TimeBasis == DisturbanceTimeBasis.EventOrder
                    ? (float)index / (candidates.Count - 1)
                    : (float)((Position(candidate, result.TimeBasis) - start) / (end - start));
                points.Add(new DisturbancePoint
                {
                    Event = candidate.item, X = x, CategoryHeight = candidate.height, Category = candidate.category,
                    TimeLabel = result.TimeBasis == DisturbanceTimeBasis.Utc ? candidate.utc.ToUniversalTime().ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                        : result.TimeBasis == DisturbanceTimeBasis.DeviceElapsed ? "+" + (candidate.item.elapsedMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture) + " s"
                        : "Event " + (index + 1),
                    BreakBefore = boundary,
                    HasReportedPeak = candidate.item.peakG > 0 && !float.IsNaN(candidate.item.peakG) && !float.IsInfinity(candidate.item.peakG)
                });
            }
            result.Points = points.AsReadOnly();
            result.AxisCaption = result.TimeBasis == DisturbanceTimeBasis.Utc ? "UTC · " + Range(points)
                : result.TimeBasis == DisturbanceTimeBasis.DeviceElapsed ? "DEVICE ELAPSED · one boot · " + Range(points)
                : "EVENT ORDER · spacing does not represent elapsed time";
            return result;
        }

        /// <summary>Hit-tests the visible vertical stems in plot-local pixels, including category markers.</summary>
        public int HitTest(float x, float y, float width, float height, float radius)
        {
            if (!Finite(x) || !Finite(y) || !Finite(width) || !Finite(height) || !Finite(radius) || width <= 0 || height <= 0 || radius < 0) return -1;
            int selected = -1;
            double closest = radius * radius, closestTop = double.MaxValue;
            for (int index = 0; index < Points.Count; index++)
            {
                DisturbancePoint point = Points[index];
                double px = point.X * width, py = point.CategoryHeight * height;
                double dx = x - px, dy = y < 0 ? y : y > py ? y - py : 0;
                double distance = dx * dx + dy * dy;
                double topDistance = dx * dx + (y - py) * (y - py);
                if (distance < closest || (distance == closest && topDistance < closestTop))
                {
                    selected = index; closest = distance; closestTop = topDistance;
                }
            }
            return selected;
        }

        private static bool Category(string value, out string category, out float height)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "bump": category = "Bump"; height = 0.24f; return true;
                case "movement": category = "Movement"; height = 0.46f; return true;
                case "opening": category = "Opening"; height = 0.66f; return true;
                case "impact": category = "Impact"; height = 0.88f; return true;
                default: category = ""; height = 0; return false;
            }
        }
        private static bool TryUtc(string value, out DateTimeOffset utc)
        {
            utc = default;
            value = (value ?? "").Trim();
            bool zone = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                (value.Length >= 6 && (value[value.Length - 6] == '+' || value[value.Length - 6] == '-') && value[value.Length - 3] == ':');
            return zone && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out utc);
        }
        private static string Session(BagEvent item) => string.IsNullOrEmpty(item.deviceId) || string.IsNullOrEmpty(item.bootId) ? null : item.deviceId.Length + ":" + item.deviceId + ":" + item.bootId;
        private static bool SameSession(BagEvent a, BagEvent b) => Session(a) != null && Session(a) == Session(b);
        private static bool HasGap(BagEvent item)
        {
            string quality = (item.quality ?? "").ToLowerInvariant();
            return quality.Contains("gap") || quality.Contains("overflow") || quality.Contains("missing");
        }
        private static double Position(Candidate candidate, DisturbanceTimeBasis basis) => basis == DisturbanceTimeBasis.Utc ? candidate.utc.UtcDateTime.Ticks : basis == DisturbanceTimeBasis.DeviceElapsed ? candidate.item.elapsedMs : candidate.order;
        private static string Range(IReadOnlyList<DisturbancePoint> points) => points.Count == 0 ? "no event times" : points.Count == 1 ? points[0].TimeLabel : points[0].TimeLabel + " – " + points[points.Count - 1].TimeLabel;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

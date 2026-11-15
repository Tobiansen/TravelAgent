using System;
using System.Collections;
using System.Collections.Generic;

namespace TravelAgent.Core
{
    /// <summary>An immutable original sample and the continuity boundary preceding it.</summary>
    public sealed class MotionTracePoint
    {
        public string DeviceId { get; }
        public string BootId { get; }
        public long Sequence { get; }
        public long ElapsedMs { get; }
        public float DeltaG { get; }
        public float GyroDps { get; }
        public long SampleAgeMs { get; }
        public long DroppedSamples { get; }
        public bool BreakBefore { get; }

        internal MotionTracePoint(MotionSample sample, bool breakBefore)
        {
            DeviceId = sample.deviceId; BootId = sample.bootId;
            Sequence = sample.sequence; ElapsedMs = sample.elapsedMs;
            DeltaG = sample.deltaG; GyroDps = sample.gyroDps;
            SampleAgeMs = sample.sampleAgeMs; DroppedSamples = sample.droppedSamples;
            BreakBefore = breakBefore;
        }
    }

    /// <summary>
    /// Bounded phone-side trace for one device boot. Missing data never becomes an
    /// interpolated quiet segment. A new boot starts a new elapsed-time axis.
    /// </summary>
    public sealed class MotionHistory : IReadOnlyList<MotionTracePoint>
    {
        public const int MaximumCapacity = 3000;
        public const long MaximumContinuousGapMs = 1000;
        private readonly MotionTracePoint[] points;
        private int head;
        private bool breakNext = true;
        public int Count { get; private set; }
        public int Capacity => points.Length;
        public long DiscardedCount { get; private set; }
        public string DeviceId { get; private set; } = "";
        public string BootId { get; private set; } = "";
        public MotionTracePoint this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                return points[(head + index) % points.Length];
            }
        }

        public MotionHistory(int capacity = MaximumCapacity)
        {
            if (capacity < 2 || capacity > MaximumCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
            points = new MotionTracePoint[capacity];
        }

        public bool Add(MotionSample sample)
        {
            if (sample == null || !sample.IsValid || !sample.sensorFresh)
            { Break(); return false; }
            if (Count > 0 && (sample.deviceId != DeviceId || sample.bootId != BootId)) Clear();
            if (Count > 0)
            {
                MotionTracePoint prior = this[Count - 1];
                // Duplicate/reordered delivery is not a new sample or a new clock anchor.
                if (sample.sequence <= prior.Sequence || sample.elapsedMs <= prior.ElapsedMs) return false;
                if (sample.sequence != prior.Sequence + 1 || sample.elapsedMs - prior.ElapsedMs > MaximumContinuousGapMs ||
                    sample.droppedSamples > prior.DroppedSamples)
                    breakNext = true;
            }
            DeviceId = sample.deviceId; BootId = sample.bootId;
            var point = new MotionTracePoint(sample, breakNext);
            breakNext = false;
            if (Count == Capacity)
            {
                points[head] = point;
                head = (head + 1) % Capacity;
                ++DiscardedCount;
            }
            else { points[(head + Count) % Capacity] = point; ++Count; }
            return true;
        }

        /// <summary>Call on disconnect, stale data or a known unavailable sensor interval.</summary>
        public void Break() { breakNext = true; }
        public void Clear()
        {
            Array.Clear(points, 0, points.Length);
            head = 0; Count = 0; DiscardedCount = 0;
            DeviceId = BootId = ""; breakNext = true;
        }
        public IEnumerator<MotionTracePoint> GetEnumerator()
        { for (int index = 0; index < Count; index++) yield return this[index]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

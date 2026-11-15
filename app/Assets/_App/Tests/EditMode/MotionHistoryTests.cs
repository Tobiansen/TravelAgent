using System;
using System.Collections.Generic;
using NUnit.Framework;
using TravelAgent.Core;

namespace TravelAgent.Tests
{
    public sealed class MotionHistoryTests
    {
        [Test]
        public void TwoMinuteDemoKeepsEveryFiveHertzSampleWithoutAResolutionChange()
        {
            var history = new MotionHistory();
            for (int index = 0; index < 600; index++) Assert.That(history.Add(Sample(index + 1, index * 200)), Is.True);
            Assert.That(history.Count, Is.EqualTo(600));
            Assert.That(history.DiscardedCount, Is.Zero);
            Assert.That(history[599].ElapsedMs, Is.EqualTo(119800));
        }

        [Test]
        public void CapacityRollsOldestSamplesOutAndProjectionUsesVisibleElapsedWindow()
        {
            var history = new MotionHistory(3);
            for (int index = 1; index <= 5; index++) history.Add(Sample(index, index * 200));
            Assert.That(history.Count, Is.EqualTo(3)); Assert.That(history.DiscardedCount, Is.EqualTo(2));
            Assert.That(history[0].Sequence, Is.EqualTo(3)); Assert.That(history[2].Sequence, Is.EqualTo(5));
            var projection = MotionChartProjection.Create(history, new[] { Event(1, 200), Event(2, 800) });
            Assert.That(projection.FirstElapsedMs, Is.EqualTo(600)); Assert.That(projection.LastElapsedMs, Is.EqualTo(1000));
            Assert.That(projection.Markers, Has.Count.EqualTo(1)); Assert.That(projection.Markers[0].X, Is.EqualTo(.5f));
        }

        [Test]
        public void IncomingMutableObjectCannotRewriteTheAlreadyRecordedTrace()
        {
            var history = new MotionHistory(); var sample = Sample(1, 200, .12f);
            history.Add(sample); sample.deltaG = 3; sample.bootId = "changed"; sample.elapsedMs = 9000;
            Assert.That(history[0].DeltaG, Is.EqualTo(.12f)); Assert.That(history[0].BootId, Is.EqualTo("boot"));
            Assert.That(history[0].ElapsedMs, Is.EqualTo(200));
        }

        [Test]
        public void DuplicateAndReorderedDeliveryDoNotInventAClockOrBreakTheNextConsecutiveSample()
        {
            var history = new MotionHistory(); history.Add(Sample(10, 2000));
            Assert.That(history.Add(Sample(10, 2000)), Is.False);
            Assert.That(history.Add(Sample(9, 1800)), Is.False);
            Assert.That(history.Add(Sample(11, 2200)), Is.True);
            Assert.That(history.Count, Is.EqualTo(2)); Assert.That(history[1].BreakBefore, Is.False);
        }

        [Test]
        public void RegressedDeviceTimeIsRejectedAndNeverBecomesAnAlternateAnchor()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 2000));
            Assert.That(history.Add(Sample(2, 1900)), Is.False);
            Assert.That(history.Count, Is.EqualTo(1));
            history.Add(Sample(3, 2200));
            Assert.That(history[1].BreakBefore, Is.True, "The missing second sample is visible as a gap.");
        }

        [Test]
        public void ExplicitDisconnectMakesABreakEvenIfTheNextSequenceIsConsecutive()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 1000)); history.Break(); history.Add(Sample(2, 1200));
            Assert.That(history[1].BreakBefore, Is.True);
            Assert.That(MotionChartProjection.Create(history, null).HasGaps, Is.True);
        }

        [Test]
        public void FirmwareDropCounterPreventsDrawingAcrossAnUnreportedLostWindow()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 1000));
            var next = Sample(2, 1200); next.droppedSamples = 4; history.Add(next);
            Assert.That(history[1].BreakBefore, Is.True); Assert.That(history[1].DroppedSamples, Is.EqualTo(4));
        }

        [TestCase(3, 1400)]
        [TestCase(2, 2400)]
        public void MissingSequencesAndLongDeviceElapsedIntervalsBreakThePolyline(long sequence, long elapsed)
        {
            var history = new MotionHistory(); history.Add(Sample(1, 1000)); history.Add(Sample(sequence, elapsed));
            Assert.That(history[1].BreakBefore, Is.True);
        }

        [Test]
        public void SensorUnavailabilityAndMalformedMeasurementsCannotDrawAQuietSegment()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 200));
            var unavailable = Sample(2, 400); unavailable.sensorFresh = false;
            Assert.That(history.Add(unavailable), Is.False);
            var invalid = Sample(3, 600); invalid.deltaG = float.NaN;
            Assert.That(history.Add(invalid), Is.False);
            history.Add(Sample(4, 800));
            Assert.That(history.Count, Is.EqualTo(2)); Assert.That(history[1].BreakBefore, Is.True);
        }

        [Test]
        public void NewBootStartsANewTraceRatherThanJoiningTwoElapsedTimeClocks()
        {
            var history = new MotionHistory(); history.Add(Sample(20, 99000));
            var fresh = Sample(1, 200); fresh.bootId = "rebooted"; history.Add(fresh);
            Assert.That(history.Count, Is.EqualTo(1)); Assert.That(history.BootId, Is.EqualTo("rebooted"));
            Assert.That(history[0].BreakBefore, Is.True); Assert.That(history[0].ElapsedMs, Is.EqualTo(200));
            Assert.That(MotionChartProjection.Create(history, new[] { Event(1, 99000) }).Markers, Is.Empty);
        }

        [Test]
        public void SwitchingDeviceClearsOtherBagsTraceAndManualClearRemovesItsIdentity()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 200));
            var other = Sample(1, 200); other.deviceId = "another-bag"; history.Add(other);
            Assert.That(history.Count, Is.EqualTo(1)); Assert.That(history.DeviceId, Is.EqualTo("another-bag"));
            history.Clear(); Assert.That(history.Count, Is.Zero); Assert.That(history.DeviceId, Is.Empty); Assert.That(history.BootId, Is.Empty);
        }

        [Test]
        public void MeasuredScalePreservesRelativeAmplitudesAndElapsedSpacing()
        {
            var history = new MotionHistory();
            history.Add(Sample(1, 1000, .1f)); history.Add(Sample(2, 1200, .4f)); history.Add(Sample(3, 2000, .2f));
            var projection = MotionChartProjection.Create(history, null);
            Assert.That(projection.MaximumG, Is.EqualTo(.5f));
            Assert.That(projection.Y(.1f), Is.EqualTo(.2f).Within(.001f)); Assert.That(projection.Y(.4f), Is.EqualTo(.8f).Within(.001f));
            Assert.That(projection.X(1200), Is.EqualTo(.2f).Within(.001f));
            Assert.That(projection.AxisCaption, Does.Contain("1.0 s").And.Contain("device elapsed"));
        }

        [Test]
        public void MarkerMappingUsesOnlySuppliedMovementEventsFromTheVisibleDeviceBoot()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 1000)); history.Add(Sample(2, 2000));
            var valid = Event(1, 1500); var oldBoot = Event(2, 1500); oldBoot.bootId = "older";
            var different = Event(3, 1500); different.deviceId = "another-bag";
            var sensor = Event(4, 1500); sensor.type = "sensor_fault";
            var projection = MotionChartProjection.Create(history, new[] { valid, oldBoot, different, sensor, Event(5, 900), Event(6, 2100) });
            Assert.That(projection.Markers, Has.Count.EqualTo(1));
            Assert.That(projection.Markers[0].X, Is.EqualTo(.5f)); Assert.That(projection.Markers[0].Event.peakG, Is.Zero);
            valid.summary = "later mutation"; Assert.That(projection.Markers[0].Event.summary, Is.Not.EqualTo(valid.summary));
        }

        [Test]
        public void MovementFlagHitTestingDoesNotInterpretTheFlagHeightAsAnAccelerationPeak()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 1000)); history.Add(Sample(2, 2000));
            var projection = MotionChartProjection.Create(history, new[] { Event(1, 1500) });
            Assert.That(projection.HitTest(102, 0, 200, 100, 10), Is.Zero);
            Assert.That(projection.HitTest(102, 99, 200, 100, 10), Is.Zero);
            Assert.That(projection.HitTest(70, 50, 200, 100, 10), Is.EqualTo(-1));
            Assert.That(projection.HitTest(float.NaN, 0, 200, 100, 10), Is.EqualTo(-1));
            Assert.That(projection.HitTest(100, 200, 200, 100, 10), Is.EqualTo(-1));
        }

        [Test]
        public void QuietSamplesRemainMeasuredNearZeroRatherThanBecomingArtificialSpikes()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 0, .001f));
            var projection = MotionChartProjection.Create(history, null);
            Assert.That(projection.MaximumG, Is.EqualTo(.25f)); Assert.That(projection.Y(.001f), Is.LessThan(.01f));
            Assert.That(projection.X(0), Is.EqualTo(.5f)); Assert.That(projection.Markers, Is.Empty);
        }

        [Test]
        public void EventBoundKeepsMostRecentRealElapsedFlagsWithoutMakingUpEvents()
        {
            var history = new MotionHistory(); history.Add(Sample(1, 1000)); history.Add(Sample(2, 2000));
            var events = new List<BagEvent>(); for (int index = 0; index < 150; index++) events.Add(Event(index + 1, 1000 + index * 5));
            var projection = MotionChartProjection.Create(history, events);
            Assert.That(projection.Markers, Has.Count.EqualTo(100)); Assert.That(projection.Markers[0].Event.sequence, Is.EqualTo(51));
            Assert.That(projection.Markers[99].Event.sequence, Is.EqualTo(150));
        }

        [TestCase(1)]
        [TestCase(3001)]
        public void InvalidCapacityCannotCreateAnUnboundedTrace(int capacity)
        { Assert.Throws<ArgumentOutOfRangeException>(() => new MotionHistory(capacity)); }

        private static MotionSample Sample(long sequence, long elapsedMs, float deltaG = .05f) => new MotionSample
        { protocolVersion = 1, deviceId = "bag", bootId = "boot", sequence = sequence, elapsedMs = elapsedMs, deltaG = deltaG, gyroDps = 2, sensorFresh = true };
        private static BagEvent Event(long sequence, long elapsedMs) => new BagEvent
        { deviceId = "bag", bootId = "boot", sequence = sequence, elapsedMs = elapsedMs, type = "movement", origin = "Live device", summary = "Motion threshold exceeded", quality = "device_elapsed_only; utc_unanchored" };
    }
}

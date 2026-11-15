using System;
using System.Collections.Generic;
using NUnit.Framework;
using TravelAgent.Core;

namespace TravelAgent.Tests
{
    public sealed class DisturbanceProjectionTests
    {
        [Test]
        public void UtcEventsSortChronologicallyWithoutTurningCategoryIntoMeasuredAcceleration()
        {
            var impact = Event("impact", 3, "2026-11-14T12:00:03Z");
            var movement = Event("movement", 2, "2026-11-14T12:00:02Z");
            var bump = Event("bump", 1, "2026-11-14T12:00:01Z");
            impact.peakG = 0; movement.peakG = 99;
            var projection = DisturbanceProjection.Create(new[] { impact, bump, movement });
            Assert.That(projection.TimeBasis, Is.EqualTo(DisturbanceTimeBasis.Utc));
            Assert.That(projection.Points[0].Event.sequence, Is.EqualTo(1));
            Assert.That(projection.Points[2].Event.sequence, Is.EqualTo(3));
            Assert.That(projection.Points[1].X, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(projection.Points[2].CategoryHeight, Is.GreaterThan(projection.Points[1].CategoryHeight), "Plot height is the supplied event category, not peak g.");
            Assert.That(projection.Points[2].HasReportedPeak, Is.False, "An impact category cannot invent a missing peak value.");
            impact.summary = "changed outside projection";
            Assert.That(projection.Points[2].Event.summary, Is.Not.EqualTo(impact.summary));
        }

        [TestCase(0f)]
        [TestCase(-2f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void UnknownOrInvalidPeakIsNeverPresentedAsMeasuredZero(float peak)
        {
            var item = Event("impact", 1, "2026-11-14T12:00:00Z"); item.peakG = peak;
            var projection = DisturbanceProjection.Create(new[] { item });
            Assert.That(projection.Points[0].HasReportedPeak, Is.False);
        }

        [Test]
        public void TravelerInspectionAndOtherNonDisturbanceRecordsDoNotFabricateASignal()
        {
            var inspection = Event("inspection", 1, "2026-11-14T12:00:00Z"); inspection.origin = "Traveler record";
            var projection = DisturbanceProjection.Create(new[] { inspection, Event("connection", 2, ""), Event("guard", 3, ""), Event("sensor", 4, "") });
            Assert.That(projection.Points, Is.Empty);
            Assert.That(projection.TotalEvents, Is.Zero);
            Assert.That(projection.HitTest(50, 50, 200, 100, 30), Is.EqualTo(-1));
        }

        [Test]
        public void OneKnownBootUsesElapsedTimesWhenUtcIsUnavailable()
        {
            var later = Event("movement", 2, ""); later.elapsedMs = 4000;
            var earlier = Event("bump", 1, "not a date"); earlier.elapsedMs = 1000;
            var projection = DisturbanceProjection.Create(new[] { later, earlier });
            Assert.That(projection.TimeBasis, Is.EqualTo(DisturbanceTimeBasis.DeviceElapsed));
            Assert.That(projection.Points[0].Event.sequence, Is.EqualTo(1));
            Assert.That(projection.Points[0].TimeLabel, Is.EqualTo("+1 s"));
            Assert.That(projection.AxisCaption, Does.Contain("one boot"));
        }

        [Test]
        public void CrossBootElapsedValuesUseOrdinalSpacingAndMarkTheSessionBreak()
        {
            var oldBoot = Event("movement", 10, ""); oldBoot.bootId = "old"; oldBoot.elapsedMs = 999000;
            var newBoot = Event("impact", 1, ""); newBoot.bootId = "new"; newBoot.elapsedMs = 100;
            var projection = DisturbanceProjection.Create(new[] { oldBoot, newBoot });
            Assert.That(projection.TimeBasis, Is.EqualTo(DisturbanceTimeBasis.EventOrder));
            Assert.That(projection.Points[0].Event.bootId, Is.EqualTo("old"));
            Assert.That(projection.Points[1].BreakBefore, Is.True);
            Assert.That(projection.HasSessionBreaks, Is.True);
            Assert.That(projection.AxisCaption, Does.Contain("spacing does not represent elapsed time"));
        }

        [Test]
        public void MissingTimezoneOrMissingElapsedFieldDoesNotAcquireAnInventedClockAnchor()
        {
            var item = Event("bump", 1, "2026-11-14T12:00:00"); item.elapsedMs = 0;
            var valid = Event("impact", 2, "2026-11-14T12:00:01Z"); valid.elapsedMs = 1000;
            var projection = DisturbanceProjection.Create(new[] { valid, item });
            Assert.That(projection.TimeBasis, Is.EqualTo(DisturbanceTimeBasis.EventOrder));
            Assert.That(projection.Points[0].Event.sequence, Is.EqualTo(2));
            Assert.That(projection.Points[1].TimeLabel, Is.EqualTo("Event 2"));
        }

        [Test]
        public void ExplicitTimezoneOffsetsSortByTheSameUtcClockAndEqualTimesStayOrdinal()
        {
            var first = Event("bump", 1, "2026-11-14T14:00:00+02:00");
            var next = Event("impact", 2, "2026-11-14T12:00:01Z");
            var projection = DisturbanceProjection.Create(new[] { next, first });
            Assert.That(projection.TimeBasis, Is.EqualTo(DisturbanceTimeBasis.Utc));
            Assert.That(projection.Points[0].Event.sequence, Is.EqualTo(1));
            next.recordedAtUtc = first.recordedAtUtc;
            projection = DisturbanceProjection.Create(new[] { first, next });
            Assert.That(projection.TimeBasis, Is.EqualTo(DisturbanceTimeBasis.EventOrder), "Identical times cannot justify an apparent elapsed-time interval.");
        }

        [Test]
        public void BoundedWindowKeepsLatestChronologicalEventsAndKnownGaps()
        {
            var events = new List<BagEvent>();
            var start = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
            for (int index = 149; index >= 0; index--) events.Add(Event("movement", index + 1, start.AddSeconds(index).ToString("O")));
            events[10].quality = "recording_gap";
            var projection = DisturbanceProjection.Create(events, 500);
            Assert.That(projection.Points, Has.Count.EqualTo(100));
            Assert.That(projection.TotalEvents, Is.EqualTo(150));
            Assert.That(projection.IsWindowed, Is.True);
            Assert.That(projection.Points[0].Event.sequence, Is.EqualTo(51));
            Assert.That(projection.Points[99].Event.sequence, Is.EqualTo(150));
            Assert.That(projection.HasSessionBreaks, Is.True);
        }

        [Test]
        public void StemHitMappingUsesPlotCoordinatesAndRejectsDistantOrInvalidTouches()
        {
            var projection = DisturbanceProjection.Create(new[] { Event("bump", 1, "2026-11-14T12:00:00Z"), Event("impact", 2, "2026-11-14T12:00:10Z") });
            Assert.That(projection.HitTest(198, 30, 200, 100, 12), Is.EqualTo(1), "Tapping the impact stem selects its original event.");
            Assert.That(projection.HitTest(3, 24, 200, 100, 12), Is.EqualTo(0));
            Assert.That(projection.HitTest(100, 90, 200, 100, 12), Is.EqualTo(-1));
            Assert.That(projection.HitTest(float.NaN, 0, 200, 100, 12), Is.EqualTo(-1));
            Assert.That(projection.HitTest(0, 0, 0, 100, 12), Is.EqualTo(-1));
        }

        private static BagEvent Event(string type, long sequence, string utc)
        {
            return new BagEvent { deviceId = "bag", bootId = "boot", sequence = sequence, type = type, recordedAtUtc = utc, origin = "simulated device", quality = "illustrative fixture" };
        }
    }
}

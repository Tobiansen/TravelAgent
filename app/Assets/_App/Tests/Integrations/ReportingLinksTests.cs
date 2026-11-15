using System;
using System.Globalization;
using NUnit.Framework;
using TravelAgent.Integrations.Claims;

namespace TravelAgent.Tests.Integrations
{
    public sealed class ReportingLinksTests
    {
        [TestCase("https://www.finnair.com/en/baggage-on-finnair-flights/lost--delayed-or-damaged-baggage")]
        [TestCase("https://finnair.com/")]
        [TestCase("https://www.lufthansa.com/fi/en/baggage-irregularities")]
        [TestCase("https://lufthansa.com:443/")]
        public void OfficialAirlineGuidanceRemainsCompatibleWithoutNewMetadata(string url)
        {
            Assert.That(ReportingLinks.CanOpen(new GuidanceResponse { reportingUrl = url }), Is.True);
        }

        [Test]
        public void ExactReviewedFinnairPublicReportingEntryIsAllowed()
        {
            Assert.That(ReportingLinks.CanOpen(Reviewed()), Is.True);
        }

        [TestCase("http://mybag.aero/baggage/#/pax/finnair/en-gb/create-report")]
        [TestCase("https://mybag.aero/baggage/#/pax/finnair/en-gb/damaged/dcs/login")]
        [TestCase("https://mybag.aero/baggage/#/pax/finnair/en-gb/delayed/dcs/login")]
        [TestCase("https://mybag.aero/baggage/#/pax/lufthansa/en-gb/damaged/dcs/login")]
        [TestCase("https://mybag.aero/baggage/#/pax/lufthansa/en-gb/create-report")]
        [TestCase("https://mybag.aero/baggage/#/pax/finnair/en-gb/damaged/dcs/main-menu")]
        [TestCase("https://mybag.aero/baggage/#/pax/finnair/en-gb/main-menu")]
        [TestCase("https://mybag.aero/baggage/?tracking=1#/pax/finnair/en-gb/create-report")]
        [TestCase("https://mybag.aero/baggage/#/pax/finnair/en-gb/create-report?tracking=1")]
        [TestCase("https://person@mybag.aero/baggage/#/pax/finnair/en-gb/create-report")]
        [TestCase("https://mybag.aero:444/baggage/#/pax/finnair/en-gb/create-report")]
        [TestCase("https://mybag.aero:443/baggage/#/pax/finnair/en-gb/create-report")]
        [TestCase("https://other.mybag.aero/baggage/#/pax/finnair/en-gb/create-report")]
        [TestCase("https://mybag.aero.example.com/baggage/#/pax/finnair/en-gb/create-report")]
        [TestCase("https://mybag.aero/baggage/")]
        public void PartnerExceptionDoesNotExpandToOtherUrls(string url)
        {
            var response = Reviewed(); response.reportingUrl = url;
            Assert.That(ReportingLinks.CanOpen(response), Is.False);
        }

        [TestCase("http://www.finnair.com/")]
        [TestCase("https://person@www.finnair.com/")]
        [TestCase("https://www.finnair.com:8443/")]
        [TestCase("https://finnair.com.example.com/")]
        [TestCase("https://notfinnair.com/")]
        [TestCase("https://www.lufthansa.com:80/")]
        [TestCase("https://mybag.aero/")]
        [TestCase("not a URL")]
        [TestCase("")]
        public void UnsafeOrUnreviewedTargetsAreRejected(string url)
        {
            Assert.That(ReportingLinks.CanOpen(new GuidanceResponse { reportingUrl = url }), Is.False);
        }

        [TestCase("airline", "Lufthansa")]
        [TestCase("airline", "")]
        [TestCase("issue", "delayed")]
        [TestCase("kind", "official-guidance")]
        [TestCase("kind", "")]
        [TestCase("source", "https://www.finnair.com/")]
        [TestCase("source", "https://www.finnair.com/en/baggage-on-finnair-flights/lost--delayed-or-damaged-baggage/")]
        [TestCase("source", "")]
        [TestCase("reviewed", "")]
        [TestCase("reviewed", "not-a-time")]
        [TestCase("reviewed", "2026-11-14")]
        [TestCase("reviewed", "2026-11-14T16:42:58")]
        [TestCase("reviewed", "2026-11-14T18:42:58+02:00")]
        public void PartnerLinkRequiresMatchingCaseAndReviewMetadata(string field, string value)
        {
            var response = Reviewed();
            switch (field)
            {
                case "airline": response.airline = value; break;
                case "issue": response.issueType = value; break;
                case "kind": response.reportingTargetKind = value; break;
                case "source": response.reportingSourceUrl = value; break;
                case "reviewed": response.reportingLinkVerifiedAtUtc = value; break;
            }
            Assert.That(ReportingLinks.CanOpen(response), Is.False);
        }

        [Test]
        public void MissingResponseAndFutureReviewAreRejected()
        {
            Assert.That(ReportingLinks.CanOpen(null), Is.False);
            var response = Reviewed();
            response.reportingLinkVerifiedAtUtc = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            Assert.That(ReportingLinks.CanOpen(response), Is.False);
        }

        private static GuidanceResponse Reviewed() => new GuidanceResponse {
            airline = "Finnair", issueType = "damaged", reportingUrl = ReportingLinks.FinnairReportingEntry,
            reportingTargetKind = "verified-partner", reportingSourceUrl = ReportingLinks.OfficialGuide,
            reportingLinkVerifiedAtUtc = "2026-11-14T18:15:00Z"
        };
    }
}

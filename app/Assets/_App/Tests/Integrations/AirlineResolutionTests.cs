using System;
using System.Globalization;
using NUnit.Framework;
using TravelAgent.Integrations.Claims;

namespace TravelAgent.Tests.Integrations
{
    public sealed class AirlineResolutionTests
    {
        [TestCase("Finnair", "AY", "https://www.finnair.com/", "https://company.finnair.com/en/about/history", "live", "Z")]
        [TestCase("Finnair", "AY", "https://finnair.com/", "https://www.finnair.com/en", "cached", "offset")]
        [TestCase("Lufthansa", "LH", "https://www.lufthansa.com/", "https://developer.lufthansa.com/docs", "live", "fraction")]
        [TestCase("Lufthansa", "LH", "https://lufthansa.com/", "https://www.lufthansa.com/fi/en", "cached", "Z")]
        public void VerifiedCarrierIdentityAcceptsOwnedSubdomainsAndExplicitUtc(string airline, string code, string site, string source, string origin, string timeFormat)
        {
            var result = Valid();
            result.airline = airline; result.iataCode = code; result.officialSite = site;
            result.sourceUrl = source; result.origin = origin;
            var time = DateTimeOffset.UtcNow.AddMinutes(-1);
            result.retrievedAtUtc = timeFormat == "offset" ? time.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)
                : timeFormat == "fraction" ? time.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
                : time.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
            Assert.That(result.IsVerified, Is.True);
        }

        [TestCase("Finnair", "LH")]
        [TestCase("Lufthansa", "AY")]
        [TestCase("Finnair", "ay")]
        [TestCase("finnair", "AY")]
        [TestCase("Unknown Air", "AY")]
        [TestCase("", "")]
        public void UnmatchedOrMissingCarrierCodeCannotSelectAnAirline(string airline, string code)
        {
            var result = Valid(); result.airline = airline; result.iataCode = code;
            Assert.That(result.IsVerified, Is.False);
        }

        [TestCase("site", "http://www.finnair.com/")]
        [TestCase("source", "http://company.finnair.com/en/about")]
        [TestCase("site", "https://www.finnair.com.evil.example/")]
        [TestCase("source", "https://evilfinnair.com/")]
        [TestCase("site", "https://user@www.finnair.com/")]
        [TestCase("source", "https://user:password@company.finnair.com/en/about")]
        [TestCase("site", "https://www.finnair.com:8443/")]
        [TestCase("source", "https://company.finnair.com:444/en/about")]
        [TestCase("site", "https://www.lufthansa.com/")]
        [TestCase("source", "https://developer.lufthansa.com/docs")]
        [TestCase("site", "")]
        [TestCase("source", "")]
        public void UnsafeOrUnrelatedWebsiteAndSourceAreRejected(string field, string value)
        {
            var result = Valid();
            if (field == "site") result.officialSite = value; else result.sourceUrl = value;
            Assert.That(result.IsVerified, Is.False);
        }

        [TestCase("")]
        [TestCase("not a timestamp")]
        [TestCase("2026-11-14")]
        [TestCase("2026-11-14T19:00:00")]
        [TestCase("2026-11-14T21:00:00+02:00")]
        [TestCase("future")]
        public void MissingAmbiguousOrFarFutureRetrievalTimeIsRejected(string value)
        {
            var result = Valid();
            result.retrievedAtUtc = value == "future" ? DateTimeOffset.UtcNow.AddDays(1).ToString("O", CultureInfo.InvariantCulture) : value;
            Assert.That(result.IsVerified, Is.False);
        }

        [TestCase("schema", "2")]
        [TestCase("status", "needsConfirmation")]
        [TestCase("status", "error")]
        [TestCase("origin", "fixture")]
        public void UnresolvedFixtureOrUnknownSchemaCannotCountAsExaVerification(string field, string value)
        {
            var result = Valid();
            if (field == "schema") result.schemaVersion = value;
            else if (field == "status") result.status = value;
            else result.origin = value;
            Assert.That(result.IsVerified, Is.False);
        }

        private static AirlineResolution Valid() => new AirlineResolution {
            schemaVersion = "1", airline = "Finnair", iataCode = "AY", officialSite = "https://www.finnair.com/",
            sourceUrl = "https://company.finnair.com/en/about", retrievedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture),
            origin = "live", status = "resolved", error = ""
        };
    }
}

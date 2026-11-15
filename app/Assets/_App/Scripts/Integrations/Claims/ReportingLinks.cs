using System;
using System.Globalization;

namespace TravelAgent.Integrations.Claims
{
    /// <summary>Reporting entry points only; this does not expand the official citation-source policy.</summary>
    public static class ReportingLinks
    {
        public const string OfficialGuide = "https://www.finnair.com/en/baggage-on-finnair-flights/lost--delayed-or-damaged-baggage";
        public const string FinnairReportingEntry = "https://mybag.aero/baggage/#/pax/finnair/en-gb/create-report";

        public static bool CanOpen(GuidanceResponse response)
        {
            if (response == null || !Uri.TryCreate(response.reportingUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443)
                return false;

            if (OfficialHost(uri.Host, "finnair.com") || OfficialHost(uri.Host, "lufthansa.com"))
                return true;

            // The reviewed public Finnair report-type selection is the sole partner exception.
            // Compare the original URL too: normalization must not widen the reviewed route.
            return string.Equals(response.reportingUrl, FinnairReportingEntry, StringComparison.Ordinal) &&
                string.Equals(uri.Host, "mybag.aero", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath == "/baggage/" && string.IsNullOrEmpty(uri.Query) &&
                uri.Fragment == "#/pax/finnair/en-gb/create-report" &&
                string.Equals(response.airline?.Trim(), "Finnair", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(response.issueType?.Trim(), "damaged", StringComparison.OrdinalIgnoreCase) &&
                response.reportingTargetKind == "verified-partner" &&
                response.reportingSourceUrl == OfficialGuide &&
                HasReviewedUtcTimestamp(response.reportingLinkVerifiedAtUtc);
        }

        private static bool OfficialHost(string host, string domain) =>
            string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

        private static bool HasReviewedUtcTimestamp(string value)
        {
            // An explicit UTC instant is required, rather than a local or date-only value.
            string[] formats = { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz" };
            return DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var reviewed) &&
                reviewed.Offset == TimeSpan.Zero && HasExplicitUtcOffset(value) && reviewed <= DateTimeOffset.UtcNow;
        }

        private static bool HasExplicitUtcOffset(string value) => value != null &&
            (value.EndsWith("Z", StringComparison.Ordinal) || value.EndsWith("+00:00", StringComparison.Ordinal));
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TravelAgent.Core
{
    /// <summary>Local ticket details. Ambiguous or unsupported carrier text needs manual review.</summary>
    public static class TicketImportParser
    {
        public const int MaximumInputBytes = 64 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Regex ExternalAddresses = Pattern(@"(?:[a-z][a-z0-9+.-]*://|www\.)\S+|[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}|(?<![\p{L}\p{N}_])(?:[a-z0-9][a-z0-9-]*\.)+[a-z]{2,}(?:[/?#]\S*)?");
        private static readonly Regex Finnair = Pattern(@"(?<![\p{L}\p{N}_])Finnair(?![\p{L}\p{N}_])");
        private static readonly Regex Lufthansa = Pattern(@"(?<![\p{L}\p{N}_])Lufthansa(?![\p{L}\p{N}_])");
        private static readonly Regex Flight = Pattern(@"(?<![\p{L}\p{N}_])(?<prefix>AY|LH)[ \t-]?(?<number>[0-9]{1,4})(?![\p{L}\p{N}_])");
        private static readonly Regex PrivateIdentifierLabel = Pattern(@"\b(?:booking\s*(?:reference|ref|code|number)|reservation\s*(?:reference|ref|code|number)|(?:ticket|baggage|receipt)\s*(?:reference|ref|code|number)|pnr|passenger(?:\s+name)?)\b");
        private static readonly Regex TicketContext = Pattern(@"\b(?:booking|itinerary|ticket|flight|reservation|confirmation|boarding|e-ticket)\b");
        private static readonly Regex OperatingCarrierContext = Pattern(@"\b(?:code[ -]?share|operated\s+by|marketed\s+by|operating\s+carrier\s*(?:[:\-]|\bis\b|\b(?:Finnair|Lufthansa)\b))");
        private static readonly Regex CodePair = new Regex(@"(?<![A-Z0-9])(?<from>[A-Z]{3})[ \t]*(?:->|[-–—→])[ \t]*(?<to>[A-Z]{3})(?![A-Z0-9])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex ParenthesizedPair = new Regex(@"\((?<from>[A-Z]{3})\)[ \t]*(?:->|[-–—→]|to)[^()\r\n]{0,60}\((?<to>[A-Z]{3})\)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        public static TicketImportCandidate Parse(string sharedText)
        {
            var result = new TicketImportCandidate();
            if (string.IsNullOrEmpty(sharedText))
                return WithReason(result, "No ticket text received. Enter and confirm your airline manually.");
            // A character guard avoids scanning a huge string; the actual transport limit is UTF-8 bytes.
            if (sharedText.Length > MaximumInputBytes)
                return WithReason(result, "Shared text exceeds 64 KiB. Share a shorter confirmation or enter the airline manually.");
            try
            {
                if (StrictUtf8.GetByteCount(sharedText) > MaximumInputBytes)
                    return WithReason(result, "Shared text exceeds 64 KiB. Share a shorter confirmation or enter the airline manually.");
                if (string.IsNullOrWhiteSpace(sharedText))
                    return WithReason(result, "No ticket text received. Enter and confirm your airline manually.");

                string text = ExternalAddresses.Replace(sharedText.Normalize(NormalizationForm.FormKC), " ");
                var brands = new List<string>();
                if (Finnair.IsMatch(text)) brands.Add("Finnair");
                if (Lufthansa.IsMatch(text)) brands.Add("Lufthansa");
                result.airlineCandidates = brands.ToArray();

                var flights = new List<string>();
                var prefixes = new List<string>();
                foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    foreach (Match match in Flight.Matches(line))
                    {
                        // A six-character booking reference can resemble a flight number. Do not copy labelled identifiers.
                        if (PrivateIdentifierLabel.IsMatch(line.Substring(0, match.Index))) continue;
                        string prefix = match.Groups["prefix"].Value.ToUpperInvariant();
                        string flight = prefix + match.Groups["number"].Value;
                        if (!prefixes.Contains(prefix)) prefixes.Add(prefix);
                        if (flights.Count < 2 && !flights.Contains(flight)) flights.Add(flight);
                    }
                }
                result.flightPrefixes = prefixes.ToArray();
                result.route = FindSingleRoute(text);

                if (brands.Count > 1)
                    return WithReason(result, "More than one airline is named. Choose and confirm the airline; no operating carrier was inferred.");
                if (OperatingCarrierContext.IsMatch(text))
                    return WithReason(result, "Operating or marketing carrier wording needs review. Choose and confirm the reporting airline.");
                if (brands.Count == 0)
                {
                    if (flights.Count == 1) result.flight = flights[0];
                    return WithReason(result, prefixes.Count > 0
                        ? "Flight-code hints alone do not establish the airline. Identify and confirm it before continuing."
                        : "No supported airline name was identified. Enter and confirm the airline manually.");
                }

                string expectedPrefix = brands[0] == "Finnair" ? "AY" : "LH";
                if (prefixes.Exists(prefix => prefix != expectedPrefix))
                    return WithReason(result, "Airline names and flight-code hints disagree. Review and confirm the airline manually.");
                if (!TicketContext.IsMatch(text) && flights.Count == 0)
                    return WithReason(result, "An airline name appears without clear ticket context. It may be a signature; confirm the airline manually.");

                result.suggestedAirline = brands[0];
                if (flights.Count > 1)
                    return WithReason(result, "More than one flight appears. Confirm the airline and enter the relevant flight manually.");
                if (flights.Count == 1) result.flight = flights[0];
                result.ambiguous = false;
                result.requiresConfirmation = false;
                return WithReason(result, "Flight details read locally from shared ticket text.");
            }
            catch (ArgumentException)
            {
                return WithReason(new TicketImportCandidate(), "The text contains invalid Unicode. Enter and confirm your airline manually.");
            }
            catch (RegexMatchTimeoutException)
            {
                return WithReason(new TicketImportCandidate(), "The text could not be safely parsed. Enter and confirm your airline manually.");
            }
        }

        private static TicketImportCandidate WithReason(TicketImportCandidate candidate, string reason)
        {
            candidate.reason = reason;
            return candidate;
        }

        private static string FindSingleRoute(string text)
        {
            var routes = new List<string>();
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (PrivateIdentifierLabel.IsMatch(line)) continue;
                // Bare pairs need ticket/subject context; parenthesized airport codes are already structured hints.
                if (TicketContext.IsMatch(line) || line.TrimStart().StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
                    AddRoutes(CodePair.Matches(line), routes);
                AddRoutes(ParenthesizedPair.Matches(line), routes);
                if (routes.Count > 1) return "";
            }
            return routes.Count == 1 ? routes[0] : "";
        }

        private static void AddRoutes(MatchCollection matches, List<string> routes)
        {
            foreach (Match match in matches)
            {
                string from = match.Groups["from"].Value, to = match.Groups["to"].Value;
                if (from == to) continue;
                string route = from + " → " + to;
                if (!routes.Contains(route)) routes.Add(route);
            }
        }

        private static Regex Pattern(string expression) => new Regex(expression,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}

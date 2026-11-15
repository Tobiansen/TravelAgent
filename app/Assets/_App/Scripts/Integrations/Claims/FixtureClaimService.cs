using System;
using System.Collections;

namespace TravelAgent.Integrations.Claims
{
    /// <summary>Small paraphrased reference captured from Finnair, never labelled live.</summary>
    public sealed class FixtureClaimService : IClaimService
    {
        public const string OfficialUrl = "https://www.finnair.com/en/baggage-on-finnair-flights/lost--delayed-or-damaged-baggage";
        public const string CapturedAtUtc = "2026-11-14T16:42:58Z";
        public bool FailNextRequest { get; set; }

        public IEnumerator Research(ClaimResearchRequest request, Action<GuidanceResponse> completed)
        {
            yield return null;
            if (FailNextRequest)
            {
                FailNextRequest = false;
                var failure = GuidanceResponse.Failure("Simulated backend failure. Retry or explicitly select fixture guidance.", request.airline);
                failure.origin = "fixture";
                completed(failure);
                yield break;
            }
            if (!string.Equals(request.airline?.Trim(), "Finnair", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(request.issueType?.Trim(), "damaged", StringComparison.OrdinalIgnoreCase))
            {
                completed(new GuidanceResponse {
                    airline = request.airline, issueType = request.issueType, origin = "fixture", status = "needsConfirmation",
                    retrievedAtUtc = CapturedAtUtc,
                    error = "The offline fixture covers an initial Finnair damaged-baggage report only. Choose that case or use the appropriate official airline guidance."
                });
                yield break;
            }
            completed(new GuidanceResponse {
                caseId = "demo-finnair-damaged", airline = "Finnair", issueType = "damaged", origin = "fixture",
                status = "needsConfirmation", retrievedAtUtc = CapturedAtUtc, reportingUrl = OfficialUrl,
                reportingSourceUrl = OfficialUrl, reportingTargetKind = "official-guidance",
                sources = new[] { new GuidanceSource { title = "Lost, delayed or damaged baggage | Finnair", url = OfficialUrl, retrievedAtUtc = CapturedAtUtc } },
                items = new[] {
                    Item("step", "Open Finnair's damaged-baggage guidance and use its linked reporting channel, or contact Arrival Service at the airport."),
                    Item("evidence", "Finnair lists pictures of the damaged baggage as required for the initial report. Add damage photographs; the app does not assess them."),
                    Item("field", "Prepare name/contact and flight details, baggage receipt number, bag brand, size and colour, and a damage description. Enter personal details on Finnair's reporting channel."),
                    Item("step", "Keep the report number and follow the instructions provided after reporting. Creating this local draft does not create an airline report."),
                    Item("limitation", "Repair or replacement handling follows the initial report. Finnair's repair-provider and compensation options are separate from creating this initial report."),
                    Item("limitation", "Normal wear that does not affect the bag's usability is excluded. Reporting does not guarantee compensation.")
                },
                missingContext = new[] {
                    "This is captured fixture guidance, not a new retrieval. Verify the current official page before acting.",
                    "Confirm the current damaged-baggage reporting time limit and its triggering event on Finnair's page. No due date has been calculated.",
                    "The button opens official reporting instructions. A usable Finnair-specific partner form has not been independently verified."
                }
            });
        }

        private static GuidanceItem Item(string kind, string text)
        {
            return new GuidanceItem { kind = kind, text = text, sourceUrl = OfficialUrl };
        }
    }
}

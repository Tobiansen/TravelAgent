using System;
using System.Collections;

namespace TravelAgent.Integrations.Claims
{
    [Serializable]
    public sealed class ClaimResearchRequest
    {
        public string airline = "Finnair";
        public string country = "";
        public string route = "";
        public string issueType = "damaged";
        public string collectionDate = "";
    }

    [Serializable]
    public sealed class GuidanceItem
    {
        public string kind = "step";
        public string text = "";
        public string sourceUrl = "";
    }

    [Serializable]
    public sealed class GuidanceSource
    {
        public string title = "";
        public string url = "";
        public string retrievedAtUtc = "";
    }

    [Serializable]
    public sealed class GuidanceResponse
    {
        public string schemaVersion = "1";
        public string caseId = "";
        public string airline = "";
        public string issueType = "damaged";
        public string retrievedAtUtc = "";
        public string origin = "fixture";
        public string status = "needsConfirmation";
        public string reportingUrl = "";
        // Optional schema-v1 metadata; partner verification applies to one reviewed route only.
        public string reportingSourceUrl = "";
        public string reportingTargetKind = "";
        public string reportingLinkVerifiedAtUtc = "";
        public string error = "";
        public GuidanceItem[] items = Array.Empty<GuidanceItem>();
        public GuidanceSource[] sources = Array.Empty<GuidanceSource>();
        public string[] missingContext = Array.Empty<string>();

        public static GuidanceResponse Failure(string message, string airline = "")
        {
            return new GuidanceResponse { airline = airline, origin = "live", status = "error", error = message };
        }
    }

    public interface IClaimService
    {
        IEnumerator Research(ClaimResearchRequest request, Action<GuidanceResponse> completed);
    }
}

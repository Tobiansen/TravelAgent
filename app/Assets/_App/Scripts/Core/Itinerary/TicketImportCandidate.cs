using System;

namespace TravelAgent.Core
{
    /// <summary>Transient, derived hints only. The shared message is never retained in this result.</summary>
    [Serializable]
    public sealed class TicketImportCandidate
    {
        public string suggestedAirline = "";
        public string flight = "";
        public string route = "";
        public string[] airlineCandidates = Array.Empty<string>();
        public string[] flightPrefixes = Array.Empty<string>();
        public bool ambiguous = true;
        public bool requiresConfirmation = true;
        public string reason = "Confirm the airline before using reporting guidance.";
    }
}

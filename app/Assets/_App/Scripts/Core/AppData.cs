using System;
using System.Collections.Generic;

namespace TravelAgent.Core
{
    [Serializable]
    public sealed class AppData
    {
        public int schemaVersion = 1;
        public string bagName = "My suitcase";
        public string confirmedArmSequence = "AABB";
        public string confirmedDisarmSequence = "BABA";
        public int confirmedConfigRevision = 1;
        // Keep a consumed inbox ID across demo launches if Android cleanup needs retrying; never store the message body here.
        public string retiredTicketRequestId = "";
        public JourneyRecord journey = new JourneyRecord();
        public List<BagEvent> events = new List<BagEvent>();
        // Phone-received samples only; a bounded live session, never an offline board journal.
        public List<MotionSample> motionSamples = new List<MotionSample>();
    }

    [Serializable]
    public sealed class JourneyRecord
    {
        public string id = Guid.NewGuid().ToString("N");
        public string airline = "";
        public string airlineConfirmedValue = "";
        public string airlineConfirmedAtUtc = "";
        public string airlineConfirmationSource = "";
        public string airlineSourceUrl = "";
        public string airlineIataSourceUrl = "";
        public string airlineResolvedAtUtc = "";
        public string airlineSourceOrigin = "";
        public string ticketImportRequestId = "";
        public string ticketReceivedAtUtc = "";
        public string flight = "";
        public string route = "";
        public string country = "";
        public string bagDescription = "";
        public string baggageReference = "";
        public string collectionDate = "";
        public string stage = "preparing";
        public string handedOverRecordedAtUtc = "";
        public string collectionRecordedAtUtc = "";
        public string inspectionRecordedAtUtc = "";
        public int inspectedEventCount;
        public string demoCaseId = "";
        public bool damageConfirmed;
        public string damageDescription = "";
        public string claimDraft = "";
        public string claimDraftContext = "";
        public bool claimDraftNeedsReview;
        public string claimGuidanceContext = "";
        public string submissionReference = "";
        public string submissionConfirmedAtUtc = "";
        public List<PhotoEvidence> photos = new List<PhotoEvidence>();
    }

    [Serializable]
    public sealed class PhotoEvidence
    {
        public string id = Guid.NewGuid().ToString("N");
        public string stage = "before";
        public string path = "";
        public string origin = "";
        public string capturedAtUtc = "";
        public string addedAtUtc = "";
        public string previewPath = "";
        public string sourceRequestId = "";
        public string captureRequestedAtUtc = "";
        public string captureReturnedAtUtc = "";
    }

    public interface IAppStore
    {
        AppData Load();
        void Save(AppData data);
        void Reset();
        string Export(AppData data);
    }
}

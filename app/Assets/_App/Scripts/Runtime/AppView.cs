using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    public sealed class AppView : MonoBehaviour
    {
        [Header("Shell")]
        public ScreenSelector selector;
        public TMP_Text providerLabel, connectionLabel, toast, pageTitle;
        public Button[] navigation;
        [Header("Guard")]
        public TMP_Text guardState, guardDetail, guardCodes, recordingLabel;
        public Button guardAction;
        public TMP_Text guardActionLabel;
        public RectTransform recentEvents;
        public DisturbanceTimeline timeline;
        public GameObject inspectionPromptPanel;
        public TMP_Text inspectionStatus;
        public Button inspectBagContents;
        [Header("Device and configurable controls")]
        public TMP_Text deviceStatus, configStatus, codeProgress;
        public Button scan, disconnect, useBluetooth, useDemo, authorizePhone, saveSequences, clearSequences, testFeedback;
        public RectTransform discoveredDevices;
        public TMP_InputField armCode, disarmCode, confirmArmCode, confirmDisarmCode;
        public SequenceEntryPad sequencePad;
        [Header("Journey")]
        public TMP_InputField airline, flight, route, country, baggageReference, bagDescription, collectionDate, damageDescription;
        public TMP_Text journeyStatus, beforePhotoStatus, afterPhotoStatus;
        public Image beforePhoto, afterPhoto;
        public Button beforeSample, afterSample, beforeImport, afterImport, beforeCapture, afterCapture, startTransit, handover, collection, damageYes, damageNo;
        public DemoJourneyFlow journeyFlow;
        [Header("Ticket import and reporting reveal")]
        public TMP_Text ticketStatus, ticketAirlineResult, ticketSource;
        public Button confirmAirline, discardTicket, newJourney;
        public ExaSpotlight exaSpotlight;
        [Header("Claim child flow")]
        public GameObject claimPanel;
        public TMP_Text guidanceStatus, claimStatus;
        public Button research, toggleGuidance, regenerateDraft, copyDraft, export, openForm, confirmSubmission;
        public TMP_Text guidanceProvider;
        public TMP_InputField backendUrl, backendToken, claimDraft, submissionReference;
        public RectTransform guidanceItems;
        [Header("Damage evidence and camera recovery")]
        public TMP_Text reportPhotoStatus, reportingSummary;
        public Button takeDamagePhoto, chooseDamagePhoto;
        public GameObject recoveryPanel;
        public Image recoveryPhoto;
        public TMP_Text recoveryStatus;
        public Button recoveryUse, recoveryDiscard, recoveryLater, recoveryRetry;
        [Header("History and details")]
        public TMP_Text historyStatus;
        public RectTransform historyEvents;
        public ListRowView rowPrefab;
        public GameObject detailPanel;
        public TMP_Text detailTitle, detailBody;
        public Button closeDetail, detailInspect;
        [Header("Presenter controls")]
        public GameObject demoPanel;
        public Button demoToggle, pressA, pressB, bump, movement, rolling, impact, lid, sensorFault, failure, backendFailure, reconnect, reset, loadFinnairSample;
        public TMP_Text failureLabel;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TravelAgent.Core;
using TravelAgent.Integrations.Claims;
using TravelAgent.Integrations.Import;
using UnityEngine;

namespace TravelAgent
{
    public sealed partial class AppComposition
    {
        private IIncomingShareService incomingShareService;
        private IncomingShare pendingTicket;
        private TicketImportCandidate ticketCandidate;
        private string lastSharedRequest="";
        private string lastImportError="";
        private string gatewaySettingsError="";
        private float nextSharePoll;
        private bool researchSpotlightActive;
#if UNITY_EDITOR
        public IIncomingShareService IncomingShareServiceOverride {get;set;}
#endif
        private bool AirlineConfirmed() => Data!=null && !string.IsNullOrWhiteSpace(Data.journey.airline) &&
            Data.journey.airline==Data.journey.airlineConfirmedValue && !string.IsNullOrEmpty(Data.journey.airlineConfirmedAtUtc);

        private void BindTicketFlow()
        {
            Click(view.confirmAirline,ConfirmAirline);
            Click(view.discardTicket,DiscardTicket);
            Click(view.newJourney,() => BeginFreshJourney(null));
            UnityEngine.Events.UnityAction<string> changed=_ => RefreshFlightSetup();
            view.airline.onValueChanged.AddListener(changed);
            unbind.Add(() => view.airline.onValueChanged.RemoveListener(changed));
            UnityEngine.Events.UnityAction<string> connectionChanged=_ => SaveGatewaySettings();
            view.backendUrl.onEndEdit.AddListener(connectionChanged);view.backendToken.onEndEdit.AddListener(connectionChanged);
            unbind.Add(() => {view.backendUrl.onEndEdit.RemoveListener(connectionChanged);view.backendToken.onEndEdit.RemoveListener(connectionChanged);});
        }
        private void LoadGatewaySettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var settings=AndroidGatewaySettings.Load();
            if(settings!=null && !string.IsNullOrEmpty(settings.error))gatewaySettingsError="Saved demo connection could not be opened. Re-enter it in Before → Connection settings.";
            if(settings!=null && string.IsNullOrEmpty(settings.error) && !string.IsNullOrEmpty(settings.url) && !string.IsNullOrEmpty(settings.token))
            {view.backendUrl.SetTextWithoutNotify(settings.url);view.backendToken.SetTextWithoutNotify(settings.token);fixture=false;}
#endif
        }
        private void SaveGatewaySettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!string.IsNullOrWhiteSpace(view.backendUrl.text) && !string.IsNullOrWhiteSpace(view.backendToken.text) &&
                !AndroidGatewaySettings.Save(view.backendUrl.text.Trim(),view.backendToken.text.Trim()))
                Toast("The demo connection could not be saved securely. It remains available for this app session.");
#endif
        }
        private IIncomingShareService IncomingShares()
        {
#if UNITY_EDITOR
            if(IncomingShareServiceOverride!=null)return IncomingShareServiceOverride;
#endif
            if(incomingShareService==null)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                incomingShareService=new AndroidIncomingShareService();
#else
                incomingShareService=new EditorIncomingShareService();
#endif
            }
            return incomingShareService;
        }
        private void PollIncomingTicket()
        {
            if(Time.unscaledTime<nextSharePoll || photoSelecting || reportSharing || Data==null)return;
            nextSharePoll=Time.unscaledTime+.6f;
            IncomingShare incoming;
            try {incoming=IncomingShares().Peek();}
            catch(Exception) {return;}
            if(incoming==null)return;
            if(!string.IsNullOrEmpty(incoming.error)){if(lastImportError!=incoming.error){lastImportError=incoming.error;Toast("Shared text could not be opened. Share the ticket text again.");}return;}
            if(string.IsNullOrEmpty(incoming.requestId) || incoming.requestId==lastSharedRequest)return;
            if (retiredTicketRequests.Contains(incoming.requestId)) {
                // This exact inbox item belongs to the archived run. A newly shared Gmail message has a new request ID.
                IncomingShares().Acknowledge(incoming.requestId);
                return;
            }
            if(Data.journey.ticketImportRequestId==incoming.requestId && AirlineConfirmed())
            {
                pendingTicket=incoming;lastSharedRequest=incoming.requestId;
                FinishTicketImport();RefreshFlightSetup();return;
            }
            if(Data.journey.ticketImportRequestId!=incoming.requestId && !BeginFreshJourney(incoming))return;
            lastSharedRequest=incoming.requestId;
            pendingTicket=incoming;ticketCandidate=TicketImportParser.Parse(incoming.text);
            if(!ticketCandidate.ambiguous && !string.IsNullOrWhiteSpace(ticketCandidate.suggestedAirline))
                SaveTicketAirline(ticketCandidate.suggestedAirline,"shared-text-local");
            Navigate(1);view.journeyFlow.ShowStage(0);RefreshFlightSetup();
            Toast(AirlineConfirmed() ? "Flight details imported from your email." : "Ticket received. Enter the airline operating your flight.");
        }

        /// <summary>Archive the current journey and start with no photos, carrier or saved research.</summary>
        public bool BeginFreshJourney(IncomingShare incoming=null)
        {
            if(incoming==null && pendingTicket!=null){Toast("Confirm or discard the imported ticket first.");return false;}
            if(Data==null || photoSelecting || reportSharing || recoveredPhoto!=null)
            {Toast("Finish the open photo or share action before starting a new trip.");return false;}
            SaveJourney();
            if(!Save())return false;
            var priorJourney=Data.journey;var priorEvents=Data.events;var priorMotion=Data.motionSamples;
            try
            {
                string history=Path.Combine(store.RootDirectory,"JourneyHistory");Directory.CreateDirectory(history);
                File.WriteAllText(Path.Combine(history,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".json"),JsonUtility.ToJson(Data,true));
            }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException)
            {Toast("The previous trip could not be archived. Its evidence is retained; no new trip was started.");return false;}
            Data.journey=new JourneyRecord {stage="Choose your airline",demoCaseId=simulated ? "clean-ticket-demo-v2" : "",
                ticketImportRequestId=incoming?.requestId ?? "",ticketReceivedAtUtc=incoming?.receivedAtUtc ?? ""};
            Data.events=new List<BagEvent>();
            Data.motionSamples=new List<MotionSample>();
            if(simulated)
            {
                string boot=Guid.NewGuid().ToString("N");string[] types={"bump","movement","impact"};
                for(int i=0;i<types.Length;i++)Data.events.Add(new BagEvent {deviceId="travel-demo",bootId=boot,sequence=i+1,type=types[i],
                    recordedAtUtc=DateTimeOffset.UtcNow.AddMinutes(-15+i*5).ToString("O"),origin="Demo fixture",
                    quality="Illustrative event; no acceleration measurement supplied.",summary="DEMO · handling disturbance; inspect bag and contents."});
            }
            if(!Save()){Data.journey=priorJourney;Data.events=priorEvents;Data.motionSamples=priorMotion;PopulateJourney();return false;}
            PrepareLiveMotion();
            pendingTicket=incoming;
            ticketCandidate=incoming==null ? null : TicketImportParser.Parse(incoming.text);
            ledger=new EventLedger(Data.events);InvalidateGuidance();view.exaSpotlight.Hide();PopulateJourney();RefreshHistory();
            view.journeyFlow.ShowStage(0);Navigate(1);return true;
        }
        private void StartFreshDemo()
        {
            if(!simulated){Toast("Choose Demo mode before starting a demo run.");return;}
            if(pendingTicket!=null){Toast("Confirm or discard the imported ticket before starting another run.");return;}
            if(BeginFreshJourney())Toast("New demo · no previous photos or airline selected.");
        }
        private static string SourceCaption(string url,string origin)
        {
            string host=Uri.TryCreate(url,UriKind.Absolute,out var uri) ? uri.Host.Replace("www.","") : "";
            return origin=="fixture" ? "OFFLINE REFERENCE · "+host : "EXA · "+host+(origin=="cached" ? " · cached source" : " · live source");
        }
        private void ConfirmAirline()
        {
            if(pendingTicket!=null && Data.journey.ticketImportRequestId==pendingTicket.requestId && AirlineConfirmed())
            {
                FinishTicketImport();RefreshFlightSetup();
                return;
            }
            string value=(view.airline.text??"").Trim();
            if(string.IsNullOrEmpty(value)){Toast("Enter the airline operating your flight.");return;}
            if(SaveTicketAirline(value,pendingTicket!=null ? "shared-text-manual" : "manual"))
                Toast("Flight ready. Add a before photo or continue to inspection.");
        }
        private bool SaveTicketAirline(string value,string source)
        {
            SaveJourney();var j=Data.journey;
            j.airline=value;view.airline.SetTextWithoutNotify(value);
            j.airlineConfirmedValue=value;j.airlineConfirmedAtUtc=Utc();
            // The legacy confirmation fields also gate a locally imported, usable airline.
            // Source records the distinction: this is email parsing, never Exa research or traveler review.
            j.airlineConfirmationSource=source;
            j.airlineSourceUrl="";j.airlineIataSourceUrl="";j.airlineResolvedAtUtc="";j.airlineSourceOrigin="";
            if(pendingTicket!=null && ticketCandidate!=null)
            {
                if(!string.IsNullOrEmpty(ticketCandidate.flight))j.flight=ticketCandidate.flight;
                if(!string.IsNullOrEmpty(ticketCandidate.route))j.route=ticketCandidate.route;
            }
            j.stage="Flight ready";
            if(!Save()){j.airlineConfirmedValue="";j.airlineConfirmedAtUtc="";j.airlineConfirmationSource="";j.stage="Choose your airline";PopulateJourney();return false;}
            FinishTicketImport();PopulateJourney();RefreshFlightSetup();return true;
        }
        private void FinishTicketImport()
        {
            if(pendingTicket==null)return;
            if(IncomingShares().Acknowledge(pendingTicket.requestId)){pendingTicket=null;ticketCandidate=null;}
            else Toast("Flight saved. Tap Finish import to remove the temporary shared text.");
        }
        private void DiscardTicket()
        {
            if(pendingTicket!=null && !IncomingShares().Acknowledge(pendingTicket.requestId))
            {Toast("Shared text could not be discarded. Retry; it has not been attached as evidence.");return;}
            pendingTicket=null;ticketCandidate=null;
            RefreshFlightSetup();
        }
        private void RefreshFlightSetup()
        {
            if(!view || Data==null || !view.ticketStatus)return;
            bool confirmed=AirlineConfirmed() && Data.journey.airline==view.airline.text.Trim();
            view.ticketStatus.text=confirmed ? (string.IsNullOrEmpty(Data.journey.route) ? "Flight ready" : Data.journey.route)+(string.IsNullOrEmpty(Data.journey.flight) ? "" : " · "+Data.journey.flight) : pendingTicket!=null
                ? "Check your ticket and enter the operating airline."
                : "Share ticket text from Gmail, or enter your airline.";
            view.ticketAirlineResult.text=confirmed ? Data.journey.airline : "";
            view.ticketAirlineResult.gameObject.SetActive(confirmed);
            view.airline.transform.parent.gameObject.SetActive(!confirmed);
            view.ticketSource.text=confirmed && Data.journey.airlineConfirmationSource=="shared-text-local" ? "From your email" : "";
            view.ticketSource.gameObject.SetActive(!string.IsNullOrEmpty(view.ticketSource.text));
            view.confirmAirline.interactable=confirmed || !string.IsNullOrWhiteSpace(view.airline.text);
            view.confirmAirline.gameObject.SetActive(confirmed ? pendingTicket!=null : !string.IsNullOrWhiteSpace(view.airline.text));
            view.confirmAirline.GetComponentInChildren<TMPro.TMP_Text>(true).text=confirmed ? "Finish import" : "Use this airline";
            view.discardTicket.gameObject.SetActive(pendingTicket!=null);
            view.newJourney.interactable=!photoSelecting && !reportSharing && pendingTicket==null;
            view.journeyFlow.SetAirlineConfirmed(confirmed);
        }
    }
}

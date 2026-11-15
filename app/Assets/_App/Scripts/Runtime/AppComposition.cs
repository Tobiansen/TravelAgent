using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TravelAgent.Core;
using TravelAgent.Integrations;
using TravelAgent.Integrations.Claims;
using TravelAgent.Integrations.Photos;
using TravelAgent.Integrations.Sharing;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    /// <summary>Explicit service wiring and UI coordination. Layout is authored in the saved scene.</summary>
    public sealed partial class AppComposition : MonoBehaviour
    {
        public AppConfiguration configuration;
        public AppTheme theme;
        public AppView view;
        public IBagTransport Transport { get; private set; }
        public AppData Data { get; private set; }
        public GuidanceResponse Guidance { get; private set; }
        public string LastExportPath { get; private set; }
        [Serializable] private sealed class ExportFiles { public List<ExportFile> files = new List<ExportFile>(); }
        [Serializable] private sealed class ExportFile { public string id,stage,origin,fileName,addedAtUtc,capturedAtUtc,captureRequestedAtUtc,captureReturnedAtUtc; public bool available; }
#if UNITY_EDITOR
        public static string TestStorageRoot;
        public static bool? TestFreshSessionOnLaunch;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTestState() { TestStorageRoot = null; TestFreshSessionOnLaunch = null; }
#endif
        private static string StorageRoot
        {
            get {
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(TestStorageRoot)) return TestStorageRoot;
#endif
                return Application.persistentDataPath;
            }
        }
        private JsonAppStore store;
        private EventLedger ledger;
        private bool simulated, fixture, failure, failResearch, expandedDemo;
        private string lastConfirmedConfig = "";
        private string lastDevice = "";
        private string previousRecordingState = "idle";
        private int researchVersion;
        private Coroutine researchRoutine;
        private bool photoSelecting;
        private bool reportSharing;
        private bool historyDirty;
        private float toastUntil;
        private readonly List<Action> unbind = new List<Action>();
        private readonly Dictionary<Image, Sprite> importedSprites = new Dictionary<Image, Sprite>();
        private readonly Dictionary<string, ListRowView> discoveredRows = new Dictionary<string, ListRowView>(StringComparer.OrdinalIgnoreCase);
        private bool acceptingDiscoveryResults;
        private readonly string phoneSession = Guid.NewGuid().ToString("N");
        private long phoneEventSequence;
        private readonly HashSet<bool> visitedSessionProviders = new HashSet<bool>();
        private readonly HashSet<string> retiredTicketRequests = new HashSet<string>();
        private bool FreshDemoSessionOnLaunch {
            get {
#if UNITY_EDITOR
                if (TestFreshSessionOnLaunch.HasValue) return TestFreshSessionOnLaunch.Value;
#endif
                return configuration.freshDemoSessionOnLaunch;
            }
        }

        private void Awake()
        {
            if (!view || !configuration) { Debug.LogError("Travel Agent needs its saved view and configuration."); enabled = false; return; }
            fixture = configuration.fixtureGuidance;
            view.backendUrl.SetTextWithoutNotify(configuration.backendBaseUrl);
            LoadGatewaySettings();
            if (FreshDemoSessionOnLaunch) RememberPriorTicketRequests();
            Bind();
            bool useSimulation = configuration.editorSimulatedHardware;
#if UNITY_ANDROID && !UNITY_EDITOR
            useSimulation = false;
#endif
            SetHardware(useSimulation);
            Navigate(configuration.startupScreen);
            view.detailPanel.SetActive(false);
            RefreshGuidanceProvider();
            if(!string.IsNullOrEmpty(gatewaySettingsError))Toast(gatewaySettingsError);
        }

        private IEnumerator Start() { PollIncomingTicket(); yield return RecoverCameraPhotoForSession(!FreshDemoSessionOnLaunch); }

        private void RememberPriorTicketRequests()
        {
            // A retained Android inbox is shared by both providers. Read their consumed IDs before either is reset.
            foreach (bool demo in new[] { false, true }) {
                try {
                    var previous = new JsonAppStore(StorageRoot, demo, d => JsonUtility.ToJson(d, true), json => JsonUtility.FromJson<AppData>(json)).Load();
                    if (!string.IsNullOrEmpty(previous.journey.ticketImportRequestId)) retiredTicketRequests.Add(previous.journey.ticketImportRequestId);
                    if (!string.IsNullOrEmpty(previous.retiredTicketRequestId)) retiredTicketRequests.Add(previous.retiredTicketRequestId);
                } catch (Exception) { /* The selected store is validated with a visible error by SetHardware. */ }
            }
        }

        private void Bind()
        {
            for (int i = 0; i < view.navigation.Length; i++) { int index = i; Click(view.navigation[i], () => Navigate(index)); }
            Click(view.scan, BeginScan);
            Click(view.disconnect, () => Transport.Disconnect());
            Click(view.useBluetooth, () => SetHardware(false));
            Click(view.useDemo, () => SetHardware(true));
            Click(view.authorizePhone, () => { if (Transport is AndroidBagTransport android) android.ClaimOwnership(); });
            Click(view.saveSequences, SaveSequences);
            Click(view.clearSequences, () => { if(view.sequencePad)view.sequencePad.ResetEntry(); else foreach(var field in new[] {view.armCode,view.disarmCode,view.confirmArmCode,view.confirmDisarmCode}) field.text=""; Toast("Entry reset. Bag settings unchanged."); });
            Click(view.testFeedback, () => Send("testFeedback"));
            Click(view.guardAction, () => { var state=Transport.Snapshot;if(!state.connected || state.stale){Navigate(3);return;} var c = DeviceCommand.Create("setGuardState"); c.desiredState = state.guardState == "disarmed" ? "armed" : "disarmed"; Transport.Send(c); });
            Click(view.beforeSample, () => SelectSample("before"));
            Click(view.afterSample, () => SelectSample("after"));
            Click(view.beforeImport, () => ImportPhoto("before"));
            Click(view.afterImport, () => ImportPhoto("after"));
            Click(view.beforeCapture, () => CapturePhoto("before"));
            Click(view.afterCapture, () => CapturePhoto("after"));
            Click(view.takeDamagePhoto, () => CapturePhoto("after"));
            Click(view.chooseDamagePhoto, () => ImportPhoto("after"));
            Click(view.inspectBagContents, InspectBagAndContents);
            Click(view.detailInspect, InspectBagAndContents);
            Click(view.loadFinnairSample, StartFreshDemo);
            BindTicketFlow();
            Click(view.recoveryUse, UseRecoveredPhoto);
            Click(view.recoveryDiscard, DiscardRecoveredPhoto);
            Click(view.recoveryLater, () => view.recoveryPanel.SetActive(false));
            Click(view.recoveryRetry, () => StartCoroutine(RecoverCameraPhoto()));
            Click(view.startTransit, () => { SaveJourney(); var c = DeviceCommand.Create("setRecordingMode"); c.recordingMode = "transit"; Transport.Send(c); });
            Click(view.handover, () => { Data.journey.handedOverRecordedAtUtc=Utc();JourneyStage("Handed over");RecordTravelerEvent("handover","Traveler recorded bag handover."); });
            Click(view.collection, () => { SaveJourney(); if (string.IsNullOrEmpty(view.collectionDate.text)) view.collectionDate.text = DateTimeOffset.Now.ToString("yyyy-MM-dd"); SaveJourney(); Data.journey.collectionRecordedAtUtc=Utc();if(simulated || Array.IndexOf(Transport.Snapshot.capabilities ?? Array.Empty<string>(),"setRecordingMode")>=0){var c = DeviceCommand.Create("setRecordingMode"); c.recordingMode = "complete"; Transport.Send(c);} JourneyStage("Collected · inspect your bag");RecordTravelerEvent("collection","Traveler recorded bag collection; check the confirmed device recording state separately."); });
            Click(view.damageYes, () => RecordInspection(true));
            Click(view.damageNo, () => RecordInspection(false));
            Click(view.toggleGuidance, () => { fixture = !fixture; InvalidateGuidance(); RefreshGuidanceProvider(); });
            Click(view.research, Research);
            Click(view.regenerateDraft, () => GenerateDraft(true));
            Click(view.backendFailure, () => { failResearch = true; Toast("The next research request will demonstrate a backend failure."); });
            Click(view.copyDraft, () => { SaveJourney(); GUIUtility.systemCopyBuffer = (Data.journey.claimDraftNeedsReview ? "JOURNEY DETAILS CHANGED — review this draft against current facts.\n\n" : "") + view.claimDraft.text; Toast("Draft copied. Review facts before reporting."); });
            Click(view.export, Export);
            Click(view.openForm, OpenReportingPage);
            Click(view.confirmSubmission, () => { SaveJourney(); if(!Data.journey.damageConfirmed){Toast("Inspect the bag and confirm damage before recording a damage-report reference.");return;} if (string.IsNullOrWhiteSpace(view.submissionReference.text)) { Toast("Enter the reference you received from the airline first."); return; } Data.journey.submissionReference = view.submissionReference.text.Trim(); Data.journey.submissionConfirmedAtUtc = Utc(); Save(); RefreshJourney(); Toast("Your submission confirmation was recorded locally."); });
            Click(view.closeDetail, () => view.detailPanel.SetActive(false));
            Click(view.pressA, () => Sim(s => s.Tap('A')));
            Click(view.pressB, () => Sim(s => s.Tap('B')));
            Click(view.bump, () => Sim(s => s.InjectMotion("bump")));
            Click(view.movement, () => Sim(s => s.InjectMotion("sustained")));
            Click(view.rolling, () => Sim(s => s.InjectMotion("rolling")));
            Click(view.impact, () => Sim(s => s.InjectMotion("impact")));
            Click(view.lid, () => Sim(s => s.InjectMotion("opening")));
            Click(view.sensorFault, () => Sim(s => s.InjectMotion("sensor_fault")));
            Click(view.failure, () => { failure = !failure; Sim(s => s.SetFailure(failure)); view.failureLabel.text = failure ? "Failure: ON" : "Fail next command"; });
            Click(view.reconnect, () => Transport.Connect(string.IsNullOrEmpty(lastDevice) ? SimulatedBagTransport.SimulatedDeviceId : lastDevice));
            Click(view.reset, ResetDemo);
            Click(view.demoToggle, () => { expandedDemo = !expandedDemo; view.demoPanel.SetActive(expandedDemo && simulated); });
            if(view.sequencePad)
            {
                view.sequencePad.BindTargets(view.armCode,view.disarmCode,view.confirmArmCode,view.confirmDisarmCode);
                view.sequencePad.EntryChanged += RefreshSequenceSave;
                unbind.Add(() => { if(view.sequencePad)view.sequencePad.EntryChanged -= RefreshSequenceSave; });
            }
            foreach(var field in new[]{view.armCode,view.disarmCode,view.confirmArmCode,view.confirmDisarmCode})
            {
                UnityEngine.Events.UnityAction<string> changed = _ => RefreshSequenceSave();
                field.onValueChanged.AddListener(changed);
                unbind.Add(() => field.onValueChanged.RemoveListener(changed));
            }
            if(view.timeline)
            {
                view.timeline.Selected += ShowEvent;
                unbind.Add(() => { if(view.timeline)view.timeline.Selected -= ShowEvent; });
            }
            if(view.journeyFlow)
            {
                Action<int> changed = _ => SaveJourney();
                view.journeyFlow.stageChanged += changed;
                unbind.Add(() => { if(view.journeyFlow)view.journeyFlow.stageChanged -= changed; });
            }
            foreach (var input in new[] { view.airline, view.route, view.country, view.collectionDate })
            {
                UnityEngine.Events.UnityAction<string> changed = _ => InvalidateGuidance();
                input.onValueChanged.AddListener(changed);
                unbind.Add(() => input.onValueChanged.RemoveListener(changed));
            }
        }

        private void Click(Button button, Action action)
        {
            UnityEngine.Events.UnityAction callback = () => { try { action(); } catch (Exception e) { Debug.LogException(e); Toast("Action failed: " + e.Message); } };
            button.onClick.AddListener(callback);
            unbind.Add(() => button.onClick.RemoveListener(callback));
        }

        private void Update()
        {
            Transport?.Tick(Time.unscaledDeltaTime);
            PollIncomingTicket();
            TickLiveMotion();
            if(view && view.toast && view.toast.transform.parent.gameObject.activeSelf && Time.unscaledTime>toastUntil)
                view.toast.transform.parent.gameObject.SetActive(false);
        }
        private void LateUpdate() { if(historyDirty && Data != null)RefreshHistory(); }
        private void OnApplicationPause(bool paused) { if (paused && Data != null) { SaveJourney(); Save(); } }
        private void OnDestroy()
        {
            try { if (Data != null) SaveJourney(); }
            finally {
                DetachTransport();
                foreach (var sprite in importedSprites.Values) { if (sprite) { Destroy(sprite.texture); Destroy(sprite); } }
                importedSprites.Clear();
                foreach (var action in unbind) action();
                unbind.Clear();
            }
        }

        public void SetHardware(bool useSimulation)
        {
            if(pendingTicket!=null){Toast("Confirm or discard the imported ticket before switching the bag connection.");return;}
            if (Data != null) { SaveJourney(); if(!Save())return; }
            var nextStore = new JsonAppStore(StorageRoot, useSimulation, d => JsonUtility.ToJson(d, true), json => JsonUtility.FromJson<AppData>(json));
            AppData nextData;
            bool freshSession = FreshDemoSessionOnLaunch && !visitedSessionProviders.Contains(useSimulation);
            try {
                nextData = nextStore.Load();
                if (freshSession) {
                    nextData = nextStore.StartFreshDemoSession(nextData);
                }
                if (!string.IsNullOrEmpty(nextData.retiredTicketRequestId)) retiredTicketRequests.Add(nextData.retiredTicketRequestId);
            }
            catch (Exception error) { Toast("Could not open that evidence store. Existing files were preserved. " + error.Message); return; }
            DetachTransport();
            simulated = useSimulation;
            store = nextStore;
            Data = nextData;
            visitedSessionProviders.Add(useSimulation);
            ledger = new EventLedger(Data.events);
            Transport = simulated ? (IBagTransport)new SimulatedBagTransport(Data.confirmedArmSequence, Data.confirmedDisarmSequence, Data.confirmedConfigRevision) : new AndroidBagTransport(freshSession);
            Transport.SnapshotChanged += OnSnapshot;
            Transport.EventReceived += OnEvent;
            Transport.MotionReceived += OnMotionSample;
            Transport.DeviceDiscovered += OnDiscovered;
            Transport.Notice += Toast;
            PrepareLiveMotion();
            lastConfirmedConfig = "";
            lastDevice = "";
            previousRecordingState = "idle";
            ClearDiscovery();
            InvalidateGuidance();
            PopulateJourney();
            if(view.journeyFlow)view.journeyFlow.ShowStage(0);
            if(view.sequencePad)view.sequencePad.SetConfirmed("", "");
            RestoreGuidance();
            OnSnapshot(Transport.Snapshot);
            RefreshHistory();
            view.demoPanel.SetActive(simulated && expandedDemo && configuration.showDemoControls);
            view.demoToggle.gameObject.SetActive(simulated && configuration.showDemoControls);
            RefreshRecoveredPhoto();
            Toast(simulated ? "Demo mode · sample data" : "Connect your bag in Device.");
        }

        private void DetachTransport()
        {
            if (Transport == null) return;
            Transport.SnapshotChanged -= OnSnapshot;
            Transport.EventReceived -= OnEvent;
            Transport.MotionReceived -= OnMotionSample;
            Transport.DeviceDiscovered -= OnDiscovered;
            Transport.Notice -= Toast;
            Transport.Dispose();
            Transport = null;
        }

        private void OnDiscovered(DiscoveredDevice device)
        {
            if (!acceptingDiscoveryResults || device == null || string.IsNullOrWhiteSpace(device.id)) return;
            if (!discoveredRows.TryGetValue(device.id, out var row) || !row)
            {
                row = Instantiate(view.rowPrefab, view.discoveredDevices);
                discoveredRows[device.id] = row;
            }
            string deviceId = device.id;
            row.Set(device.name, (device.simulated ? "Simulated board" : "Bluetooth LE") + " · " + device.rssi + " dBm · tap to connect", () =>
            {
                lastDevice = deviceId;
                ClearDiscovery();
                Transport.Connect(deviceId);
            });
        }

        private void BeginScan()
        {
            ClearDiscovery();
            acceptingDiscoveryResults = true;
            Transport.Scan();
        }

        private void ClearDiscovery()
        {
            acceptingDiscoveryResults = false;
            discoveredRows.Clear();
            ClearRows(view.discoveredDevices);
        }

        private void OnSnapshot(DeviceSnapshot state)
        {
            RefreshLiveMotionConnection(state);
            view.providerLabel.text = simulated ? "DEMO" : "LIVE BLE";
            view.connectionLabel.text = state.connected ? (state.stale ? "Connection stale" : "Connected") : "Disconnected";
            view.deviceStatus.text = state.connected ? (state.authenticated ? "Bag connected · authorized" : "Connected · authorize this phone")
                : (state.status ?? "").IndexOf("scanning",StringComparison.OrdinalIgnoreCase)>=0 ? "Searching for your bag…"
                : "Find your bag to connect.";
            bool pending = !string.IsNullOrEmpty(state.pendingCommandId);
            bool knownCodes = !string.IsNullOrEmpty(state.armSequence) && !string.IsNullOrEmpty(state.disarmSequence);
            view.configStatus.text = pending ? "Waiting for bag confirmation…" : !knownCodes
                ? "Connect to read the bag’s confirmed codes and revision."
                : (state.connected && !state.stale ? "Confirmed revision " : "Last confirmed revision ") + state.configRevision + " · Arm " + state.armSequence + " / Disarm " + state.disarmSequence;
            string confirmed = state.deviceId + ":" + state.configRevision + ":" + state.armSequence + ":" + state.disarmSequence;
            if (confirmed != lastConfirmedConfig && !string.IsNullOrEmpty(state.armSequence))
            {
                lastConfirmedConfig = confirmed;
                view.armCode.SetTextWithoutNotify(state.armSequence);
                view.disarmCode.SetTextWithoutNotify(state.disarmSequence);
                view.confirmArmCode.SetTextWithoutNotify("");
                view.confirmDisarmCode.SetTextWithoutNotify("");
                if(view.sequencePad)view.sequencePad.SetConfirmed(state.armSequence,state.disarmSequence);
                Data.confirmedArmSequence = state.armSequence;
                Data.confirmedDisarmSequence = state.disarmSequence;
                Data.confirmedConfigRevision = state.configRevision;
                Save();
            }
            bool canConfigure = simulated || Array.IndexOf(state.capabilities ?? Array.Empty<string>(), "setButtonSequences") >= 0;
            bool canGuard = simulated || Array.IndexOf(state.capabilities ?? Array.Empty<string>(), "setGuardState") >= 0;
            bool canRecord = simulated || Array.IndexOf(state.capabilities ?? Array.Empty<string>(), "setRecordingMode") >= 0;
            bool canEditCodes = state.connected && !state.stale && state.authenticated && state.guardState == "disarmed" && !pending && canConfigure;
            if(view.sequencePad)view.sequencePad.SetInteractable(canEditCodes);
            RefreshSequenceSave();
            if(state.connected && !canConfigure)view.configStatus.text += "\nThis firmware does not support sequence changes yet.";
            view.authorizePhone.interactable = state.connected && !state.authenticated && state.guardState == "disarmed" && !simulated;
            view.authorizePhone.gameObject.SetActive(state.connected && !state.authenticated && !simulated);
            view.scan.gameObject.SetActive(!state.connected);
            view.disconnect.gameObject.SetActive(state.connected);
            view.disconnect.interactable = state.connected;
            view.testFeedback.interactable = state.connected && state.authenticated && !pending && (simulated || Array.IndexOf(state.capabilities ?? Array.Empty<string>(), "testFeedback") >= 0);
            view.codeProgress.text = "Physical input   " + new string('●', Mathf.Clamp(state.codeProgress, 0, 4)) + new string('○', 4 - Mathf.Clamp(state.codeProgress, 0, 4));
            string guard = state.guardState ?? "unknown";
            view.guardState.text = state.connected && !state.stale ? Title(guard) : string.IsNullOrEmpty(state.lastSeenUtc) ? "Not connected" : "Last known: " + Title(guard);
            view.guardState.color = guard == "armed" || guard == "warning" || guard == "alarm" ? theme.accent : theme.ink;
            view.guardDetail.text = guard == "warning" ? "Movement detected · alarm in 2 s" : guard == "arming" ? "Keep still · settling" : guard == "alarm" ? (state.reason == "sensor_fault" ? "Sensor fault · motor inhibited" : "Movement alarm") : guard == "armed" ? "Movement monitoring active" : "Ready to guard";
            if (!state.connected || state.stale) view.guardDetail.text = string.IsNullOrEmpty(state.lastSeenUtc) ? "Connect your bag in Device." : "Current state unconfirmed · last contact " + DisplayTime(state.lastSeenUtc);
            view.guardCodes.text = string.IsNullOrEmpty(state.armSequence) ? "Connect to read the bag’s button sequences." : "Arm  " + state.armSequence + "     Disarm  " + state.disarmSequence;
            bool currentLink=state.connected && !state.stale;
            view.guardActionLabel.text = !currentLink ? "Connect bag" : pending ? "Confirming…" : guard == "disarmed" ? "Arm bag" : "Disarm";
            view.guardAction.interactable = !currentLink || (state.authenticated && !pending && canGuard && !(state.recordingState == "transit" && guard == "disarmed"));
            view.recordingLabel.text = "Recording " + Title(state.recordingState) + "  ·  Motor " + Title(state.motorState);
            view.startTransit.interactable = state.connected && state.authenticated && !state.stale && !pending && canRecord && state.recordingState != "transit";
            if (state.recordingState == "transit" && previousRecordingState != "transit") Data.journey.stage = "Transit recording · siren and dispensing quiet";
            previousRecordingState = state.recordingState;
            RefreshJourney();
            if(state.connected && !canRecord)view.journeyStatus.text += "\nTransit recording · demo only";
        }

        private void SaveSequences()
        {
            string arm = view.armCode.text.Trim().ToUpperInvariant(), disarm = view.disarmCode.text.Trim().ToUpperInvariant();
            if (!SequenceRules.Validate(arm, disarm, out string error)) { Toast(error); return; }
            if (arm != view.confirmArmCode.text.Trim().ToUpperInvariant() || disarm != view.confirmDisarmCode.text.Trim().ToUpperInvariant()) { Toast("Repeat both sequences in the confirmation fields."); return; }
            var command = DeviceCommand.Create("setButtonSequences");
            command.armSequence = arm; command.disarmSequence = disarm; command.expectedConfigRevision = Transport.Snapshot.configRevision;
            Transport.Send(command);
        }

        private void RefreshSequenceSave()
        {
            if(Transport == null || !view)return;
            var state = Transport.Snapshot;
            bool capable = simulated || Array.IndexOf(state.capabilities ?? Array.Empty<string>(), "setButtonSequences") >= 0;
            string arm = view.armCode.text.Trim().ToUpperInvariant(), disarm = view.disarmCode.text.Trim().ToUpperInvariant();
            view.saveSequences.interactable = state.connected && !state.stale && state.authenticated && state.guardState == "disarmed" && string.IsNullOrEmpty(state.pendingCommandId) && capable
                && SequenceRules.Validate(arm, disarm, out _) && arm == view.confirmArmCode.text.Trim().ToUpperInvariant() && disarm == view.confirmDisarmCode.text.Trim().ToUpperInvariant();
        }

        private void Send(string operation) => Transport.Send(DeviceCommand.Create(operation));
        private void Sim(Action<SimulatedBagTransport> action) { if (Transport is SimulatedBagTransport board) action(board); else Toast("Presenter scenarios require the simulated board."); }

        private void OnEvent(BagEvent item)
        {
            if (!ledger.Add(item)) return;
            Data.events.Add(item.Copy());
            Save(); historyDirty = true;
            RefreshInspectionPrompt();
        }
        private void RecordTravelerEvent(string type,string summary) => OnEvent(new BagEvent { deviceId="phone-local",bootId=phoneSession,sequence=++phoneEventSequence,type=type,recordedAtUtc=Utc(),origin="Traveler record",quality="Phone UTC when recorded; actual event time unverified",summary=summary });

        private void RefreshHistory()
        {
            historyDirty = false;
            if(view.timeline)view.timeline.SetEvents(Data.events, simulated);
            RefreshLiveMotionView();
            ClearRows(view.historyEvents); ClearRows(view.recentEvents);
            var events = Data.events.AsEnumerable().Reverse().Take(100).ToArray();
            view.historyStatus.text = events.Length == 0 ? "No events received yet." : Data.events.Count + " events · tap to inspect";
            int recent=0;
            for (int i = 0; i < events.Length; i++)
            {
                var item = events[i];
                AddEventRow(view.historyEvents, item);
                if (recent < 2 && (item.type == "bump" || item.type == "movement" || item.type == "impact" || item.type == "opening"))
                {AddEventRow(view.recentEvents, item);recent++;}
            }
        }
        private void AddEventRow(Transform parent, BagEvent item)
        {
            var row = Instantiate(view.rowPrefab, parent);
            row.Set(Title(item.type), EventDisplayTime(item) + " · " + item.origin, () => ShowEvent(item));
        }

        private void ShowEvent(BagEvent item)
        {
            if(item == null)return;
            view.detailTitle.text = Title(item.type);
            bool validPeak = item.peakG > 0 && !float.IsNaN(item.peakG) && !float.IsInfinity(item.peakG);
            string time=DateTimeOffset.TryParse(item.recordedAtUtc,out var recorded)?recorded.ToUniversalTime().ToString("dd MMM · HH:mm:ss 'UTC'"):
                item.elapsedMs>0 ? "Device uptime +"+TimeSpan.FromMilliseconds(item.elapsedMs).ToString(@"hh\:mm\:ss\.fff")+" · UTC unanchored" : "Time unconfirmed";
            view.detailBody.text = item.summary + "\n\nTime: " + time + (validPeak ? "\nReported peak: " + item.peakG.ToString("0.00") + " g" : "\nNo peak measurement supplied") + "\n\nSource: " + item.origin + "\n" + item.quality;
            view.detailPanel.SetActive(true);
            view.detailInspect.gameObject.SetActive(IsDisturbance(item));
        }

        public void Navigate(int index)
        {
            if (Data != null) SaveJourney();
            view.selector.Show(index);
            view.pageTitle.text = new[] { "Guard", "Journey", "Events", "Device" }[index];
            for (int i = 0; i < view.navigation.Length; i++)
            {
                var image = view.navigation[i].GetComponent<Image>();
                image.color = i == index ? theme.accent : theme.card;
                view.navigation[i].GetComponentInChildren<TMPro.TMP_Text>().color = i == index ? theme.ink : theme.muted;
            }
        }

        private void PopulateJourney()
        {
            var j = Data.journey;
            view.airline.SetTextWithoutNotify(j.airline); view.flight.SetTextWithoutNotify(j.flight);
            view.route.SetTextWithoutNotify(j.route); view.country.SetTextWithoutNotify(j.country);
            view.bagDescription.SetTextWithoutNotify(j.bagDescription); view.baggageReference.SetTextWithoutNotify(j.baggageReference);
            view.collectionDate.SetTextWithoutNotify(j.collectionDate); view.damageDescription.SetTextWithoutNotify(j.damageDescription);
            view.claimDraft.SetTextWithoutNotify(j.claimDraft); view.submissionReference.SetTextWithoutNotify(j.submissionReference);
            RefreshJourney();
        }
        public void SaveJourney()
        {
            if (Data == null) return;
            var j = Data.journey;
            j.airline = view.airline.text.Trim(); j.flight = view.flight.text.Trim(); j.route = view.route.text.Trim();
            if(j.airline!=j.airlineConfirmedValue){j.airlineConfirmedValue="";j.airlineConfirmedAtUtc="";j.airlineConfirmationSource="";j.airlineSourceUrl="";j.airlineIataSourceUrl="";j.airlineResolvedAtUtc="";j.airlineSourceOrigin="";}
            j.country = view.country.text.Trim(); j.bagDescription = view.bagDescription.text.Trim(); j.baggageReference = view.baggageReference.text.Trim();
            j.collectionDate = view.collectionDate.text.Trim(); j.damageDescription = view.damageDescription.text.Trim();
            j.claimDraft = view.claimDraft.text;
            if (!string.IsNullOrWhiteSpace(j.claimDraft) && j.claimDraftContext != DraftContext(j)) j.claimDraftNeedsReview = true;
            if (j.claimDraftNeedsReview && string.IsNullOrEmpty(j.submissionReference)) view.claimStatus.text = "JOURNEY DETAILS CHANGED · review or rebuild the draft. Nothing has been submitted.";
            Save();
        }
        private void JourneyStage(string stage) { SaveJourney(); Data.journey.stage = stage; Save(); RefreshJourney(); }
        private void RefreshJourney()
        {
            if (Data == null) return;
            var j = Data.journey;
            view.journeyStatus.text = string.IsNullOrEmpty(j.stage) ? "Prepare your bag · add condition images before handover" : j.stage;
            if(Transport!=null && Transport.Snapshot.recordingState=="transit")view.journeyStatus.text = "Transit recording · siren and motor quiet";
            view.claimPanel.SetActive(j.damageConfirmed);
            if(view.journeyFlow)view.journeyFlow.SetInspectionOutcome(string.IsNullOrEmpty(j.inspectionRecordedAtUtc) ? (bool?)null : j.damageConfirmed);
            RefreshInspectionChoices();
            RefreshFlightSetup();
            view.claimStatus.text = string.IsNullOrEmpty(j.submissionReference) ? (j.claimDraftNeedsReview ? "JOURNEY DETAILS CHANGED · review or rebuild the draft below. Nothing has been submitted." : "Initial damage report · nothing has been submitted") : "Submission confirmed by you · " + j.submissionReference + "\n" + j.submissionConfirmedAtUtc;
            ShowPhoto("before", view.beforePhoto, view.beforePhotoStatus, configuration.beforeSample);
            ShowPhoto("after", view.afterPhoto, view.afterPhotoStatus, configuration.afterSample);
            RefreshInspectionPrompt();
            RefreshPhotoActions();
            RefreshReportSummary();
        }

        private void ShowPhoto(string stage, Image image, TMPro.TMP_Text label, Sprite sample)
        {
            var item = Data.journey.photos.LastOrDefault(p => p.stage == stage);
            image.transform.parent.gameObject.SetActive(item != null);
            label.text = item == null ? "Add a condition photo" : item.origin == "fixture illustration" ? "DEMO · sample illustration" : item.origin == "local camera capture" ? "Camera photo · shutter time unverified" : "Imported photo · capture time unverified";
            image.enabled = item != null;
            if (item == null) { ReleasePhoto(image); image.sprite = null; return; }
            if (item.origin == "fixture illustration") { ReleasePhoto(image); image.sprite = sample; }
            else if (File.Exists(item.path) && (!image.sprite || image.sprite.name != item.id)) QueuePhotoPreview(item,image,label);
            else if (!File.Exists(item.path)) { ReleasePhoto(image); image.sprite = null; image.enabled = false; label.text = "Original image unavailable · " + item.origin; }
        }
        private void ReleasePhoto(Image image) { if (importedSprites.TryGetValue(image, out var sprite)) { if (sprite) { Destroy(sprite.texture); Destroy(sprite); } importedSprites.Remove(image); } }
        private void SelectSample(string stage)
        {
            if (!simulated) { Toast("Sample evidence is available only in the isolated demo store."); return; }
            SaveJourney();
            Data.journey.photos.RemoveAll(p => p.stage == stage);
            Data.journey.photos.Add(new PhotoEvidence { id = Guid.NewGuid().ToString("N"), stage = stage, path = "fixture-" + stage + ".png", origin = "fixture illustration", addedAtUtc = Utc() });
            Save(); RefreshJourney();
        }
        private void ImportPhoto(string stage)
        {
            if (photoSelecting) { Toast("An image selection is already open."); return; }
            if (recoveredPhoto != null) { RefreshRecoveredPhoto(); return; }
            SaveJourney();
#if UNITY_ANDROID && !UNITY_EDITOR
            IPhotoService service = new AndroidPhotoService();
#else
            IPhotoService service = new EditorPhotoService();
#endif
            StartCoroutine(SelectPhoto(stage,service));
        }
        private IEnumerator SelectPhoto(string stage,IPhotoService service)
        {
            photoSelecting = true; var targetData=Data; PhotoSelection selection=null;
            RefreshPhotoActions();
            try { yield return service.Select(result=>selection=result); }
            finally { photoSelecting=false; RefreshPhotoActions(); }
            if(selection==null || selection.cancelled) { Toast("Image selection cancelled. Your form is retained."); yield break; }
            if(!string.IsNullOrEmpty(selection.error)) { Toast(selection.error); yield break; }
            if(!ReferenceEquals(Data,targetData)) { Toast("The data store changed during selection. Choose the image again for this journey."); yield break; }
            if(AttachPhoto(stage,selection)) Toast("Photo saved locally. Exa does not receive your photos.");
        }

        private void InvalidateGuidance()
        {
            if(researchSpotlightActive && view && view.exaSpotlight)view.exaSpotlight.Hide();
            researchSpotlightActive=false;
            researchVersion++;
            if (researchRoutine != null) StopCoroutine(researchRoutine);
            researchRoutine = null; Guidance = null;
            if (!view) return;
            view.research.interactable = true;
            view.openForm.interactable = false;
            view.guidanceStatus.text = "Research the current requirements for this journey.";
            ClearRows(view.guidanceItems);
            RefreshReportSummary();
        }
        private void RefreshGuidanceProvider()
        {
            view.guidanceProvider.text = fixture ? "FIXTURE · offline reference" : "EXA · live research";
            view.backendUrl.gameObject.SetActive(!fixture); view.backendToken.gameObject.SetActive(!fixture);
        }
        private void Research()
        {
            SaveJourney();
            if(!AirlineConfirmed()){Navigate(1);view.journeyFlow.ShowStage(0);Toast("Confirm the airline in Before first.");return;}
            if(!Data.journey.damageConfirmed) { Toast("Inspect the bag and confirm any damage before getting reporting steps."); return; }
            if (string.IsNullOrWhiteSpace(Data.journey.airline)) { Toast("Enter the operating airline first."); return; }
            InvalidateGuidance();
            int version = researchVersion;
            view.research.interactable = false;
            if (failResearch) { failResearch = false; ApplyGuidance(GuidanceResponse.Failure("Simulated backend outage. Your evidence and draft are retained.", Data.journey.airline)); return; }
            view.guidanceStatus.text = fixture ? "Loading identified offline reference…" : "Researching official airline instructions with Exa…";
            researchSpotlightActive=true;
            view.exaSpotlight.Begin(fixture ? "OFFLINE REFERENCE" : "EXA RESEARCH",fixture ? "Loading the labelled reference…" : "Finding the airline’s reporting steps and photo requirements…");
            IClaimService service = fixture ? (IClaimService)new FixtureClaimService() : new HttpClaimService(view.backendUrl.text.Trim(), view.backendToken.text.Trim());
            var request = new ClaimResearchRequest { airline = Data.journey.airline, route = Data.journey.route, country = Data.journey.country, issueType = "damaged", collectionDate = Data.journey.collectionDate };
            researchRoutine = StartCoroutine(service.Research(request, response => { if (version == researchVersion && this) ApplyGuidance(response); }));
        }
        private void ApplyGuidance(GuidanceResponse response, bool persist = true)
        {
            if(response == null)response=GuidanceResponse.Failure("The research service returned no result. Retry; your evidence is retained.",Data.journey.airline);
            // Older saved live responses may contain synthesized deadline triggers that were not verified.
            if(response.origin!="fixture")foreach(var item in response.items ?? Array.Empty<GuidanceItem>())
            {
                bool numericLimit=Regex.IsMatch(item.text ?? "",@"\b(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|fourteen|thirty)[\s-]*(?:business\s+|calendar\s+)?(?:days?|hours?|weeks?)\b",RegexOptions.IgnoreCase);
                if((item.kind ?? "").IndexOf("deadline",StringComparison.OrdinalIgnoreCase)<0 && !numericLimit)continue;
                item.kind="needs confirmation";
                item.text="Confirm the reporting time limit and the event that starts it on the official airline page. No due date has been calculated.";
                response.status="needsConfirmation";
            }
            Guidance = response;
            view.research.interactable = true;
            string origin = response.origin == "fixture" ? "OFFLINE FIXTURE" : response.origin == "cached" ? "CACHED" : "LIVE EXA";
            view.guidanceStatus.text = response.status == "error" ? "Research failed · " + response.error + "\nRetry, or explicitly choose fixture guidance." : origin + " · " + response.airline + "\n" + (response.status == "needsConfirmation" ? "Needs confirmation · " : "") + (string.IsNullOrEmpty(response.error) ? "Retrieved " + response.retrievedAtUtc : response.error);
            ClearRows(view.guidanceItems);
            foreach (var item in response.items ?? Array.Empty<GuidanceItem>())
            {
                var row = Instantiate(view.rowPrefab, view.guidanceItems);
                string sourceLabel = Uri.TryCreate(item.sourceUrl, UriKind.Absolute, out var sourceUri) ? sourceUri.Host.Replace("www.", "") + " · Open source" : "";
                row.Set(Title(item.kind), item.text + (sourceLabel.Length>0 ? "\n" + sourceLabel : ""), string.IsNullOrEmpty(item.sourceUrl) ? null : (Action)(() => OpenOfficial(item.sourceUrl)));
            }
            foreach (var missing in response.missingContext ?? Array.Empty<string>())
            {
                var row = Instantiate(view.rowPrefab, view.guidanceItems); row.Set("Needs confirmation", missing);
            }
            view.openForm.interactable = Data.journey.damageConfirmed && response.status != "error" && ReportingLinks.CanOpen(response);
            if (persist && response.status != "error")
            {
                Data.journey.claimGuidanceContext = PolicyContext(Data.journey);
                File.WriteAllText(Path.Combine(store.RootDirectory, "last-guidance.json"), JsonUtility.ToJson(response, true));
                Save();
            }
            RefreshReportSummary();
            if(persist && researchSpotlightActive)
            {
                researchSpotlightActive=false;
                if(response.status=="error" || (response.items?.Length ?? 0)==0)view.exaSpotlight.Fail(string.IsNullOrEmpty(response.error) ? "No reporting steps passed source checks. Retry or open the official guidance." : response.error);
                else view.exaSpotlight.Complete("REPORTING STEPS FOUND",view.reportingSummary.text,SourceCaption(response.items[0].sourceUrl,response.origin));
            }
            if (Data.journey.damageConfirmed && response.status != "error" && (response.items?.Length ?? 0) > 0 && string.IsNullOrWhiteSpace(view.claimDraft.text))
            {
                GenerateDraft(false);
            }
        }
        private void GenerateDraft(bool preservePrevious)
        {
            SaveJourney(); var j = Data.journey;
            if(!j.damageConfirmed) { Toast("Confirm damage after inspecting the bag before preparing a report."); return; }
            if (preservePrevious && !string.IsNullOrWhiteSpace(j.claimDraft)) { string folder = Path.Combine(store.RootDirectory,"DraftHistory"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".txt"),j.claimDraft); }
            view.claimDraft.text = (simulated ? "DEMO DRAFT — sample journey data\n\n" : "") + "Damaged baggage report — draft for review\n\nAirline: " + j.airline + "\nFlight: " + j.flight + "\nRoute: " + j.route + "\nBag: " + j.bagDescription + "\nBaggage reference: " + j.baggageReference + "\nCollected (local date): " + j.collectionDate + "\n\nI observed the following condition after collection:\n" + (string.IsNullOrWhiteSpace(j.damageDescription) ? "[Describe the damage you personally observed.]" : j.damageDescription) + "\n\nAvailable evidence: " + j.photos.Count + " condition image(s) and " + Data.events.Count + " saved event record(s).\nHandling records do not establish the cause of damage.\n\n[Add your requested resolution and any information required by the airline.]";
            j.claimDraftContext = DraftContext(j); j.claimDraftNeedsReview = false;
            SaveJourney(); RefreshJourney();
        }
        private void RestoreGuidance()
        {
            string path = Path.Combine(store.RootDirectory,"last-guidance.json");
            if (!File.Exists(path) || Data.journey.claimGuidanceContext != PolicyContext(Data.journey)) return;
            try {
                var saved = JsonUtility.FromJson<GuidanceResponse>(File.ReadAllText(path));
                if (saved == null || saved.status == "error") return;
                if (saved.origin != "fixture") { saved.origin = "cached"; saved.status = "needsConfirmation"; }
                ApplyGuidance(saved,false);
            } catch (Exception) { Toast("Saved guidance could not be read. Research again; local evidence is retained."); }
        }
        private static string PolicyContext(JourneyRecord j) => string.Join("|", j.airline,j.route,j.country,j.collectionDate);
        private static string DraftContext(JourneyRecord j) => string.Join("|",PolicyContext(j),j.flight,j.bagDescription,j.baggageReference,j.damageDescription,j.photos.Count,j.damageConfirmed,j.inspectionRecordedAtUtc);

        private void Export()
        {
            if (reportSharing) { Toast("A report share is already open. Close it before exporting again."); return; }
            LastExportPath=null;
            SaveJourney();
            string folder = Path.Combine(StorageRoot, simulated ? "Demo" : "Real", "Exports", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "journey.json"), JsonUtility.ToJson(Data, true));
            File.WriteAllText(Path.Combine(folder, "claim-draft.txt"), (Data.journey.claimDraftNeedsReview ? "JOURNEY DETAILS CHANGED — review this draft against current facts.\n\n" : "") + (Data.journey.claimDraft ?? ""));
            if (Guidance != null) File.WriteAllText(Path.Combine(folder, "guidance.json"), JsonUtility.ToJson(Guidance, true));
            var html = new StringBuilder("<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Travel Agent evidence report</title><style>body{font:16px/1.5 system-ui;max-width:760px;margin:40px auto;padding:20px;background:#101114;color:#f4f2f1}h1,h2,a{color:#ff5264}img{max-width:100%;border-radius:12px}pre{white-space:pre-wrap;background:#1b1d22;padding:20px;border-radius:12px}p{overflow-wrap:anywhere}@media print{body{background:white;color:#181818}h1,h2,a{color:#9e1528}}</style><h1>Travel Agent · evidence report</h1>");
            html.Append("<p>Current journey: ").Append(Escape(Data.journey.airline+" · "+Data.journey.flight+" · "+Data.journey.route+" · collected "+Data.journey.collectionDate)).Append("</p>");
            if(Data.journey.claimDraftNeedsReview)html.Append("<p><strong>Journey details changed. Review the retained draft against the current facts before reporting.</strong></p>");
            html.Append("<p>Origin: ").Append(simulated ? "DEMO — sample journey data. See each record and image for its source." : "REAL — locally recorded journey evidence. See each record and image for its source.").Append("</p><pre>").Append(Escape(Data.journey.claimDraft)).Append("</pre><h2>Condition images</h2>");
            html.Append("<p>Photos were not analysed by this app or sent to Exa. Handling records do not establish damage, cause or liability. An exported report is not an airline submission.</p>");
            var manifest = new ExportFiles();
            foreach (var photo in Data.journey.photos)
            {
                string name = photo.id + ".png";
                string destination = Path.Combine(folder, name);
                if (photo.origin == "fixture illustration")
                {
                    var sprite = photo.stage == "before" ? configuration.beforeSample : configuration.afterSample;
                    if (sprite) File.WriteAllBytes(destination, sprite.texture.EncodeToPNG());
                }
                else if (File.Exists(photo.path)) { name = photo.id + Path.GetExtension(photo.path); File.Copy(photo.path, Path.Combine(folder, name)); }
                bool available=File.Exists(Path.Combine(folder,name));
                manifest.files.Add(new ExportFile {id=photo.id,stage=photo.stage,origin=photo.origin,fileName=name,available=available,addedAtUtc=photo.addedAtUtc,capturedAtUtc=photo.capturedAtUtc,captureRequestedAtUtc=photo.captureRequestedAtUtc,captureReturnedAtUtc=photo.captureReturnedAtUtc});
                html.Append("<p>").Append(Escape(photo.stage + " · " + photo.origin)).Append("</p>");
                if(!string.IsNullOrEmpty(photo.captureRequestedAtUtc) || !string.IsNullOrEmpty(photo.captureReturnedAtUtc))
                    html.Append("<p>Camera opened: ").Append(Escape(photo.captureRequestedAtUtc)).Append("<br>Camera returned: ").Append(Escape(photo.captureReturnedAtUtc)).Append("<br>These phone times do not establish the exact shutter time.</p>");
                if(available)html.Append("<img alt='Condition evidence' src='").Append(name).Append("'>");
                else html.Append("<p>Original image unavailable; see evidence-files.json.</p>");
            }
            File.WriteAllText(Path.Combine(folder,"evidence-files.json"),JsonUtility.ToJson(manifest,true));
            html.Append("<h2>Handling record</h2>");
            foreach (var item in Data.events) html.Append("<p>").Append(Escape(item.recordedAtUtc + " · " + item.type + " · " + item.summary + " · " + item.origin + " · " + item.quality)).Append("</p>");
            html.Append("<h2>Airline guidance</h2>");
            if (Guidance != null) {
                html.Append("<p>").Append(Escape(Guidance.origin+" · "+Guidance.status+" · retrieved "+Guidance.retrievedAtUtc+" · "+Guidance.error)).Append("</p>");
                foreach (var question in Guidance.missingContext ?? Array.Empty<string>()) html.Append("<p>Needs confirmation: ").Append(Escape(question)).Append("</p>");
            }
            if (Guidance != null) foreach (var item in Guidance.items ?? Array.Empty<GuidanceItem>())
            {
                html.Append("<p>").Append(Escape(item.text)).Append("<br>");
                if(IsOfficialUrl(item.sourceUrl))html.Append("<a href='").Append(Escape(item.sourceUrl)).Append("'>").Append(Escape(item.sourceUrl)).Append("</a>");
                else html.Append(Escape(item.sourceUrl)).Append(" · unverified link");
                html.Append("</p>");
            }
            html.Append("</html>");
            File.WriteAllText(Path.Combine(folder, "report.html"), html.ToString());
            LastExportPath=folder;
            Toast("Export saved locally: " + folder);
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(ShareCompletedExport(folder));
#endif
        }
        private IEnumerator ShareCompletedExport(string folder)
        {
            reportSharing = true;
            view.export.interactable = false;
            Toast("Export saved. Preparing a ZIP for Android sharing…");
            try
            {
                IReportSharing sharing = new AndroidReportSharing();
                yield return sharing.Share(folder, result =>
                {
                    if (!string.IsNullOrEmpty(result.error)) Toast(result.error);
                    else Toast(result.sheetOpened ? "Share sheet closed. Delivery was not confirmed; your local export is retained." : "Export saved locally. No sharing destination was opened.");
                }, Toast);
            }
            finally
            {
                reportSharing = false;
                if (view && view.export) view.export.interactable = true;
            }
        }
        private void ResetDemo()
        {
            if (!simulated) { Toast("Reset demo is available only in the demo store."); return; }
            store.Reset();
            Data = null;
            failure = false;
            view.failureLabel.text = "Fail next command";
            SetHardware(true);
            Toast("Demo reset. Real data is untouched.");
        }
        private bool Save()
        {
            if(store==null || Data==null)return false;
            try { store.Save(Data); return true; }
            catch(IOException) { Toast("Local save failed. Your changes remain in memory; check free space and retry an action before closing."); }
            catch(UnauthorizedAccessException) { Toast("Local storage is not writable. Your changes remain in memory; restore access before closing."); }
            return false;
        }
        private void Toast(string message)
        {
            if(!view)return;
            if((message ?? "").StartsWith("Arm command accepted",StringComparison.Ordinal))message="Arming · keep the bag still";
            view.toast.text=message;
            view.toast.transform.parent.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
            toastUntil=Time.unscaledTime+4f;
        }
        private static void ClearRows(Transform parent) { for (int i = parent.childCount - 1; i >= 0; i--) { parent.GetChild(i).gameObject.SetActive(false); Destroy(parent.GetChild(i).gameObject); } }
        private static string Utc() => DateTimeOffset.UtcNow.ToString("O");
        private static string Title(string value) => string.IsNullOrEmpty(value) ? "Unknown" : char.ToUpperInvariant(value[0]) + value.Substring(1).Replace('_', ' ');
        private static string DisplayTime(string value) => DateTimeOffset.TryParse(value, out var time) ? time.ToLocalTime().ToString("dd MMM · HH:mm:ss zzz") : "not available";
        private static string EventTime(string value) => DateTimeOffset.TryParse(value, out var time) ? time.ToUniversalTime().ToString("HH:mm:ss 'UTC'") : "Time unconfirmed";
        private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value ?? "");
        private static bool IsOfficialUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return false;
            string[] domains = { "finnair.com", "lufthansa.com" };
            return string.IsNullOrEmpty(uri.UserInfo) && uri.Port == 443 && domains.Any(d => uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
        }
        private void OpenOfficial(string url) { if (!IsOfficialUrl(url)) { Toast("This reporting link needs verification before it can be opened."); return; } Application.OpenURL(url); Toast("Official page opened. No report has been submitted by this app."); }
        private void OpenReportingPage()
        {
            if(!Data.journey.damageConfirmed || Guidance == null || Guidance.status == "error" || !ReportingLinks.CanOpen(Guidance))
            {Toast("This reporting destination needs verification. Open the cited airline instructions instead.");return;}
            Application.OpenURL(Guidance.reportingUrl);
            Toast("Airline reporting page opened. Complete its steps there; nothing has been submitted by this app.");
        }
    }
}

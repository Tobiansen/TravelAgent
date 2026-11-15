#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TravelAgent.Core;
using TravelAgent.Integrations.Claims;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TravelAgent.Tests.PlayMode
{
    /// <summary>
    /// Runs the saved application and its real UI bindings against the simulator. Every test uses
    /// an isolated temporary store; neither the user's demo store nor their real evidence is read.
    /// </summary>
    public sealed class ApplicationFlowTests
    {
        private const string AppScenePath = "Assets/_App/Scenes/App.unity";
        private AppComposition app;
        private string storageRoot;
        private FieldInfo testStorageField;
        private object previousStorageRoot;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            testStorageField = typeof(AppComposition).GetField("TestStorageRoot", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(testStorageField, Is.Not.Null, "AppComposition needs its editor-only TestStorageRoot override so tests cannot touch personal evidence.");
            storageRoot = Path.Combine(Path.GetTempPath(), "TravelAgent-playmode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storageRoot);
            previousStorageRoot = testStorageField.GetValue(null);
            // This fixture covers the durable product/recovery behavior. FreshSessionFlowTests exercises the demo launch override.
            AppComposition.TestFreshSessionOnLaunch = false;
            testStorageField.SetValue(null, storageRoot);
            Assert.That(File.Exists(Path.Combine(Application.dataPath, "_App/Scenes/App.unity")), Is.True, "Generate the saved App.unity before running application PlayMode tests.");
            yield return LoadSavedApplication();
            // Test provider selection is a runtime command; configuration assets remain unchanged.
            app.SetHardware(true);
            yield return null;
            // Legacy scenarios start with an explicitly confirmed manual test airline.
            // Blank-trip and shared-ticket gates are exercised in TicketImportFlowTests.
            ConfirmAirline("Finnair");
            app.Navigate(0);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene applicationScene = SceneManager.GetSceneByPath(AppScenePath);
            if (applicationScene.IsValid() && applicationScene.isLoaded)
            {
                Scene empty = SceneManager.CreateScene("TravelAgent-test-cleanup-" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(applicationScene);
            }
            app = null;
            AppComposition.TestFreshSessionOnLaunch = null;
            if (testStorageField != null) testStorageField.SetValue(null, previousStorageRoot);
            // This exact path was created above, independently of either persistent application store.
            if (!string.IsNullOrEmpty(storageRoot) && Directory.Exists(storageRoot)) Directory.Delete(storageRoot, true);
        }

        [UnityTest]
        public IEnumerator SavedNavigationAndViewReferencesAreConnected()
        {
            Assert.That(app.view.selector.screens, Has.Length.EqualTo(4));
            Assert.That(app.view.navigation, Has.Length.EqualTo(4));
            foreach (FieldInfo field in typeof(AppView).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                    Assert.That(field.GetValue(app.view), Is.Not.Null, "Missing saved view reference: " + field.Name);
            }
            for (int index = 0; index < 4; index++)
            {
                Click(app.view.navigation[index]);
                yield return null;
                Assert.That(app.view.selector.selectedScreen, Is.EqualTo(index));
                for (int other = 0; other < 4; other++)
                    Assert.That(app.view.selector.screens[other].activeSelf, Is.EqualTo(other == index));
                Assert.That(string.IsNullOrWhiteSpace(app.view.pageTitle.text), Is.False);
            }
            Assert.That(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(), Has.Length.EqualTo(1));
            Click(app.view.useBluetooth);
            Assert.That(app.Transport.Snapshot.armSequence, Is.Empty);
            Assert.That(app.view.configStatus.text, Is.EqualTo("Connect to read the bag’s confirmed codes and revision."),
                "An unconnected real device must not inherit demo defaults or claim revision zero was confirmed.");
            app.Navigate(0);
            Assert.That(app.view.guardActionLabel.text,Is.EqualTo("Connect bag"));
            Click(app.view.guardAction);
            Assert.That(app.view.selector.selectedScreen,Is.EqualTo(3),"The offline primary action must lead to connection setup.");
            Assert.That(app.Transport.Snapshot.pendingCommandId,Is.Empty,"Connection navigation cannot send a guard command.");
        }

        [UnityTest]
        public IEnumerator DiscoveryUpdatesExistingDeviceAndClearsResultsWhenSelectingOrChangingProvider()
        {
            app.Navigate(3);
            Click(app.view.scan);
            yield return null;
            ListRowView firstRow = app.view.discoveredDevices.GetComponentsInChildren<ListRowView>().Single();
            MethodInfo discovered = typeof(AppComposition).GetMethod("OnDiscovered", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(discovered, Is.Not.Null);
            Action<DiscoveredDevice> advertise = device => discovered.Invoke(app, new object[] { device });

            advertise(new DiscoveredDevice { id = SimulatedBagTransport.SimulatedDeviceId, name = "Same board, latest advertisement", rssi = -21, simulated = true });
            ListRowView[] rows = app.view.discoveredDevices.GetComponentsInChildren<ListRowView>();
            Assert.That(rows, Has.Length.EqualTo(1), "Repeated advertisements must update one row rather than append copies.");
            Assert.That(rows[0], Is.SameAs(firstRow));
            Assert.That(rows[0].title.text, Is.EqualTo("Same board, latest advertisement"));
            Assert.That(rows[0].detail.text, Does.Contain("-21 dBm"));

            advertise(new DiscoveredDevice { id = "other-board", name = "Different board", rssi = -52 });
            Assert.That(app.view.discoveredDevices.GetComponentsInChildren<ListRowView>(), Has.Length.EqualTo(2));
            Click(firstRow.action);
            // A late scan callback must not push authorization below fresh rows after selection.
            advertise(new DiscoveredDevice { id = "late-board", name = "Late callback", rssi = -60 });
            yield return null;
            Assert.That(app.Transport.Snapshot.connected, Is.True);
            Assert.That(app.view.discoveredDevices.GetComponentsInChildren<ListRowView>(), Is.Empty);

            Click(app.view.disconnect);
            Click(app.view.scan);
            yield return null;
            Assert.That(app.view.discoveredDevices.GetComponentsInChildren<ListRowView>(), Has.Length.EqualTo(1), "A new scan must start with a fresh cache.");
            advertise(new DiscoveredDevice { id = "other-board", name = "Different board", rssi = -49 });
            Assert.That(app.view.discoveredDevices.GetComponentsInChildren<ListRowView>(), Has.Length.EqualTo(2));

            app.SetHardware(true);
            advertise(new DiscoveredDevice { id = "previous-provider", name = "Stale provider callback", rssi = -70 });
            yield return null;
            Assert.That(app.view.discoveredDevices.GetComponentsInChildren<ListRowView>(), Is.Empty, "Changing providers clears rows/cache and ignores old callbacks.");
        }

        [UnityTest]
        public IEnumerator SequenceEditorRequiresRepeatAndWaitsForRealTransportAcknowledgement()
        {
            yield return ConnectUsingSavedUi();
            int revision = app.Transport.Snapshot.configRevision;
            string oldArm = app.Transport.Snapshot.armSequence;
            FillSequences("ABAB", "ABBA");
            app.view.confirmDisarmCode.text = "BBBB";
            Assert.That(app.view.saveSequences.interactable, Is.False, "A mismatched repeat must not enable a board write.");
            Assert.That(app.Transport.Snapshot.pendingCommandId, Is.Empty);
            Assert.That(app.Transport.Snapshot.armSequence, Is.EqualTo(oldArm));

            app.view.confirmDisarmCode.text = "ABBA";
            Click(app.view.saveSequences);
            Assert.That(app.Transport.Snapshot.pendingCommandId, Is.Not.Empty);
            Assert.That(app.Transport.Snapshot.armSequence, Is.EqualTo(oldArm));
            Assert.That(app.view.saveSequences.interactable, Is.False);
            Assert.That(app.view.configStatus.text, Does.Contain("Waiting"));
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(app.Transport.Snapshot.armSequence, Is.EqualTo("ABAB"));
            Assert.That(app.Transport.Snapshot.disarmSequence, Is.EqualTo("ABBA"));
            Assert.That(app.Transport.Snapshot.configRevision, Is.EqualTo(revision + 1));
            Assert.That(app.view.confirmArmCode.text, Is.Empty);
            Assert.That(app.view.confirmDisarmCode.text, Is.Empty);
            Assert.That(app.Data.confirmedArmSequence, Is.EqualTo("ABAB"));

            // A second edit must use fresh confirmation rather than reuse the previous successful one.
            app.view.armCode.text = "AAAA"; app.view.disarmCode.text = "BBBB";
            Assert.That(app.view.saveSequences.interactable, Is.False);
            Assert.That(app.Transport.Snapshot.pendingCommandId, Is.Empty);
            FillSequences("AAAA", "BBBB");
            Click(app.view.saveSequences);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(app.Transport.Snapshot.configRevision, Is.EqualTo(revision + 2));
            Assert.That(app.view.configStatus.text, Does.Contain("AAAA"));
            Assert.That(app.view.configStatus.text, Does.Contain("BBBB"));
            Assert.That(app.view.configStatus.text, Does.StartWith("Confirmed revision "));
            Click(app.view.disconnect);
            Assert.That(app.Transport.Snapshot.stale, Is.True);
            Assert.That(app.view.configStatus.text, Does.StartWith("Last confirmed revision " + (revision + 2)));
            Assert.That(app.view.configStatus.text, Does.Contain("AAAA"));
            Assert.That(app.view.configStatus.text, Does.Contain("BBBB"));
        }

        [UnityTest]
        public IEnumerator JourneyPersistsAndDemoResetDoesNotRestoreOldDataOrTouchRealStore()
        {
            yield return ConnectUsingSavedUi();
            app.Navigate(1);
            app.view.flight.text = "AY 321";
            app.view.bagDescription.text = "Test green suitcase";
            Click(app.view.beforeSample);
            app.SaveJourney();
            var real = new JsonAppStore(storageRoot, false, value => JsonUtility.ToJson(value), json => JsonUtility.FromJson<AppData>(json));
            var realData = new AppData(); realData.journey.flight = "REAL STORE SENTINEL"; real.Save(realData);

            yield return LoadSavedApplication();
            Assert.That(app.Data.journey.flight, Is.EqualTo("AY 321"));
            Assert.That(app.view.bagDescription.text, Is.EqualTo("Test green suitcase"));
            Assert.That(app.Data.journey.photos.Count, Is.EqualTo(1));
            Assert.That(app.Data.events.Count, Is.GreaterThan(0));

            ShowPresenter();
            Click(app.view.reset);
            yield return null;
            Assert.That(app.Data.journey.flight, Is.Empty);
            Assert.That(app.view.flight.text, Is.Empty);
            Assert.That(app.Data.journey.photos, Is.Empty);
            Assert.That(app.Data.events, Is.Empty);
            Assert.That(real.Load().journey.flight, Is.EqualTo("REAL STORE SENTINEL"));

            yield return LoadSavedApplication();
            Assert.That(app.Data.journey.flight, Is.Empty, "Scene shutdown must not save the pre-reset journey back over cleared data.");
            Assert.That(app.Data.journey.photos, Is.Empty);
            Assert.That(app.Data.events, Is.Empty);
            Assert.That(real.Load().journey.flight, Is.EqualTo("REAL STORE SENTINEL"));
        }

        [UnityTest]
        public IEnumerator FixtureResearchCreatesDraftWithoutAutomaticSubmission()
        {
            ConfirmAirline("Finnair");
            app.view.flight.text = "AY 123";
            app.view.damageDescription.text = "I observed a split seam after collection.";
            Click(app.view.beforeSample); Click(app.view.afterSample); Click(app.view.damageYes);
            if (!app.view.guidanceProvider.text.Contains("FIXTURE")) Click(app.view.toggleGuidance);
            Click(app.view.research);
            yield return new WaitForSecondsRealtime(0.15f);
            DismissCompletedSpotlight();
            Assert.That(app.Guidance, Is.Not.Null);
            Assert.That(app.Guidance.origin, Is.EqualTo("fixture"));
            Assert.That(app.Guidance.items.Length, Is.GreaterThan(0));
            Assert.That(app.view.guidanceStatus.text, Does.Contain("FIXTURE"));
            Assert.That(app.view.claimDraft.text, Does.Contain("draft for review"));
            Assert.That(app.view.claimDraft.text, Does.Contain("split seam"));
            Assert.That(app.Data.events, Has.Count.EqualTo(1));
            Assert.That(app.Data.events[0].type, Is.EqualTo("inspection"));
            Assert.That(app.Data.events[0].origin, Is.EqualTo("Traveler record"));
            Assert.That(app.view.claimDraft.text, Does.Contain("1 saved event record(s)"));
            Assert.That(app.view.claimDraft.text, Does.Not.Contain("1 handling record(s)"));
            Assert.That(app.view.claimDraft.text, Does.Contain("DEMO DRAFT — sample journey data"));
            Assert.That(app.view.claimDraft.text, Does.Not.Contain("simulated handling records"));
            Assert.That(app.Data.journey.submissionReference, Is.Empty);
            Assert.That(app.Data.journey.submissionConfirmedAtUtc, Is.Empty);
            Assert.That(app.view.claimStatus.text, Does.Contain("nothing has been submitted"));

            Click(app.view.confirmSubmission);
            Assert.That(app.Data.journey.submissionReference, Is.Empty);
            Assert.That(app.view.toast.text, Does.Contain("reference"));
            app.view.submissionReference.text = "DEMO-USER-REFERENCE";
            Click(app.view.confirmSubmission);
            Assert.That(app.Data.journey.submissionReference, Is.EqualTo("DEMO-USER-REFERENCE"));
            Assert.That(app.Data.journey.submissionConfirmedAtUtc, Is.Not.Empty);
            Assert.That(app.view.claimStatus.text, Does.Contain("confirmed by you"));
        }

        [UnityTest]
        public IEnumerator InspectionFactsSurviveTransitHeartbeatWhenStopAcknowledgementIsMissing()
        {
            yield return ConnectUsingSavedUi();
            app.Navigate(1);
            Click(app.view.startTransit);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(app.Transport.Snapshot.recordingState, Is.EqualTo("transit"));
            ((SimulatedBagTransport)app.Transport).SetFailure(true);
            Click(app.view.collection);
            Click(app.view.damageNo);
            string inspectionStage = app.Data.journey.stage;
            Assert.That(inspectionStage, Does.Contain("no damage"));
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(app.Transport.Snapshot.recordingState, Is.EqualTo("transit"), "An unacknowledged recording-stop request must not look complete.");
            Assert.That(app.Data.journey.stage, Is.EqualTo(inspectionStage), "Device recording state must not overwrite traveler-confirmed inspection facts.");
        }

        [UnityTest]
        public IEnumerator ChangedAirlineFlagsDraftAndExplicitRebuildPreservesUserTextInHistory()
        {
            yield return PrepareFixtureDraft();
            app.view.claimDraft.text += "\nTraveler's additional checked detail.";
            app.SaveJourney();
            string originalDraft = app.view.claimDraft.text;
            app.view.airline.text = "Lufthansa";
            Click(app.view.navigation[0]); // Normal navigation saves the changed journey context.
            Click(app.view.navigation[1]);
            Assert.That(app.Guidance, Is.Null);
            Assert.That(app.view.openForm.interactable, Is.False);
            Assert.That(app.Data.journey.claimDraftNeedsReview, Is.True);
            Assert.That(app.view.claimStatus.text, Does.Contain("DETAILS CHANGED"));
            Assert.That(app.view.claimDraft.text, Is.EqualTo(originalDraft), "Context changes must not silently discard user edits.");

            ConfirmAirline("Lufthansa");
            Click(app.view.regenerateDraft);
            Assert.That(app.view.claimDraft.text, Does.Contain("Airline: Lufthansa"));
            Assert.That(app.Data.journey.claimDraftNeedsReview, Is.False);
            string history = Path.Combine(storageRoot, "Demo", "DraftHistory");
            Assert.That(Directory.Exists(history), Is.True);
            Assert.That(Directory.GetFiles(history, "*.txt").Any(path => File.ReadAllText(path) == originalDraft), Is.True,
                "An explicit rebuild must retain the previous edited draft in local history.");
            Assert.That(app.Data.journey.submissionReference, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ReloadRetainsFixtureGuidanceAndMarksPreviouslyLiveGuidanceAsCached()
        {
            yield return PrepareFixtureDraft();
            string retrievedAt = app.Guidance.retrievedAtUtc;
            string reportingUrl = app.Guidance.reportingUrl;
            int itemCount = app.Guidance.items.Length;
            string guidancePath = Path.Combine(storageRoot, "Demo", "last-guidance.json");
            Assert.That(File.Exists(guidancePath), Is.True);

            yield return LoadSavedApplication();
            Assert.That(app.Guidance, Is.Not.Null);
            Assert.That(app.Guidance.origin, Is.EqualTo("fixture"));
            Assert.That(app.Guidance.retrievedAtUtc, Is.EqualTo(retrievedAt));
            Assert.That(app.Guidance.reportingUrl, Is.EqualTo(reportingUrl));
            Assert.That(app.Guidance.items.Length, Is.EqualTo(itemCount));
            Assert.That(app.view.guidanceStatus.text, Does.Contain("OFFLINE FIXTURE"));

            // Seed an identified previous live response without making a network request.
            var previousLive = JsonUtility.FromJson<GuidanceResponse>(File.ReadAllText(guidancePath));
            previousLive.origin = "live";
            previousLive.status = "ready";
            File.WriteAllText(guidancePath, JsonUtility.ToJson(previousLive, true));
            yield return LoadSavedApplication();
            Assert.That(app.Guidance, Is.Not.Null);
            Assert.That(app.Guidance.origin, Is.EqualTo("cached"));
            Assert.That(app.Guidance.status, Is.EqualTo("needsConfirmation"));
            Assert.That(app.Guidance.retrievedAtUtc, Is.EqualTo(retrievedAt), "Reloading cannot manufacture a new retrieval time.");
            Assert.That(app.Guidance.reportingUrl, Is.EqualTo(reportingUrl));
            Assert.That(app.Guidance.items.Length, Is.EqualTo(itemCount));
            Assert.That(app.view.guidanceStatus.text, Does.Contain("CACHED"));
        }

        [UnityTest]
        public IEnumerator FailedRealStoreLoadPreservesTargetAndKeepsCurrentProviderWritable()
        {
            app.Navigate(3);
            app.view.flight.text = "DEMO BEFORE FAILED SWITCH";
            app.SaveJourney();
            AppData originalData = app.Data;
            IBagTransport originalTransport = app.Transport;
            var real = new JsonAppStore(storageRoot, false, value => JsonUtility.ToJson(value), json => JsonUtility.FromJson<AppData>(json));
            var sentinel = new AppData(); sentinel.journey.flight = "REAL BACKUP SENTINEL";
            real.Save(sentinel); real.Save(sentinel);
            string realPath = Path.Combine(real.RootDirectory, "app-state.json");
            const string futureEvidence = "{\"schemaVersion\":999,\"futureEvidence\":\"DO NOT OVERWRITE\"}";
            File.WriteAllText(realPath, futureEvidence);

            Click(app.view.useBluetooth);
            Assert.That(app.Data, Is.SameAs(originalData));
            Assert.That(app.Transport, Is.SameAs(originalTransport));
            Assert.That(app.view.providerLabel.text, Is.EqualTo("DEMO"));
            Assert.That(app.view.toast.text, Does.Contain("preserved"));
            Assert.That(File.ReadAllText(realPath), Is.EqualTo(futureEvidence));
            app.view.flight.text = "DEMO AFTER FAILED SWITCH";
            app.SaveJourney();
            Assert.That(File.ReadAllText(realPath), Is.EqualTo(futureEvidence), "Subsequent saves must still target the original demo store.");

            yield return LoadSavedApplication();
            Assert.That(app.Data.journey.flight, Is.EqualTo("DEMO AFTER FAILED SWITCH"));
            Assert.That(app.Transport, Is.TypeOf<SimulatedBagTransport>());
            Assert.That(File.ReadAllText(realPath), Is.EqualTo(futureEvidence), "Scene shutdown must not overwrite the rejected target store.");
        }

        [UnityTest]
        public IEnumerator EvidenceExportContainsAvailableSampleImagesAndIdentifiedGuidance()
        {
            app.Navigate(1);
            Click(app.view.beforeSample);
            Click(app.view.afterSample);
            yield return PrepareFixtureDraft();
            Assert.That(app.Data.journey.photos.Count, Is.EqualTo(2));
            Click(app.view.export);
            Assert.That(app.LastExportPath, Is.Not.Null.And.Not.Empty);
            string folder = app.LastExportPath;
            Assert.That(Path.GetFullPath(folder).StartsWith(Path.GetFullPath(storageRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal), Is.True,
                "Export must remain in the test's isolated evidence store.");
            Assert.That(Directory.Exists(folder), Is.True);
            foreach (string name in new[] { "report.html", "journey.json", "guidance.json", "evidence-files.json", "claim-draft.txt" })
                Assert.That(File.Exists(Path.Combine(folder, name)), Is.True, "Missing export artifact: " + name);

            string[] images = Directory.GetFiles(folder, "*.png");
            Assert.That(images, Has.Length.EqualTo(2));
            var manifest = JsonUtility.FromJson<ExportManifestForTest>(File.ReadAllText(Path.Combine(folder, "evidence-files.json")));
            Assert.That(manifest.files, Has.Length.EqualTo(2));
            CollectionAssert.AreEquivalent(new[] { "before", "after" }, manifest.files.Select(file => file.stage));
            foreach (ExportEntryForTest file in manifest.files)
            {
                Assert.That(file.available, Is.True, "The sample illustration must actually be present in the export.");
                Assert.That(file.origin, Is.EqualTo("fixture illustration"));
                Assert.That(app.Data.journey.photos.Any(photo => photo.id == file.id && photo.origin == file.origin && photo.stage == file.stage), Is.True);
                byte[] imageBytes = File.ReadAllBytes(Path.Combine(folder, file.fileName));
                Assert.That(imageBytes.Length, Is.GreaterThan(8));
                CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, imageBytes.Take(8).ToArray(),
                    "A listed image must contain PNG data, not just an available filename.");
            }
            var savedGuidance = JsonUtility.FromJson<GuidanceResponse>(File.ReadAllText(Path.Combine(folder, "guidance.json")));
            Assert.That(savedGuidance.origin, Is.EqualTo("fixture"));
            Assert.That(savedGuidance.retrievedAtUtc, Is.EqualTo(app.Guidance.retrievedAtUtc));
            string htmlText = System.Net.WebUtility.HtmlDecode(File.ReadAllText(Path.Combine(folder, "report.html")));
            Assert.That(htmlText, Does.Contain("Origin: DEMO — sample journey data. See each record and image for its source."));
            Assert.That(htmlText, Does.Not.Contain("DEMO — simulated records and labeled fixture illustrations"));
            Assert.That(htmlText, Does.Contain("fixture illustration"));
            Assert.That(htmlText, Does.Contain("fixture"));
            Assert.That(htmlText, Does.Contain(app.Guidance.retrievedAtUtc));
            foreach (string question in app.Guidance.missingContext)
                Assert.That(htmlText, Does.Contain(question), "Export must retain unresolved context alongside the guidance.");
        }

        [Serializable]
        private sealed class ExportManifestForTest
        {
            public ExportEntryForTest[] files;
        }

        [Serializable]
        private sealed class ExportEntryForTest
        {
            public string id, stage, origin, fileName;
            public bool available;
        }

        private IEnumerator PrepareFixtureDraft()
        {
            ConfirmAirline("Finnair");
            app.view.flight.text = "AY 123";
            app.view.damageDescription.text = "I observed a split seam after collection.";
            Click(app.view.damageYes);
            if (!app.view.guidanceProvider.text.Contains("FIXTURE")) Click(app.view.toggleGuidance);
            Click(app.view.research);
            yield return new WaitForSecondsRealtime(0.15f);
            DismissCompletedSpotlight();
            Assert.That(app.Guidance, Is.Not.Null);
            Assert.That(app.Guidance.origin, Is.EqualTo("fixture"));
            Assert.That(app.view.claimDraft.text, Is.Not.Empty);
        }

        private void ConfirmAirline(string airline)
        {
            app.Navigate(1);
            app.view.journeyFlow.ShowStage(0);
            if (app.Data.journey.airline == airline && app.Data.journey.airlineConfirmedValue == airline &&
                !string.IsNullOrEmpty(app.Data.journey.airlineConfirmedAtUtc) && app.view.airline.text.Trim() == airline)
                return; // The real UI intentionally hides Use after this exact airline has been confirmed.
            app.view.airline.text = airline;
            Click(app.view.confirmAirline);
            Assert.That(app.Data.journey.airlineConfirmedValue, Is.EqualTo(airline));
            Assert.That(app.Data.journey.airlineConfirmedAtUtc, Is.Not.Empty);
        }

        private void DismissCompletedSpotlight()
        {
            var spotlight = app.view.exaSpotlight;
            Assert.That(spotlight, Is.Not.Null);
            Assert.That(spotlight.IsPending, Is.False, "The fixture request must finish before continuing the legacy UI scenario.");
            if (spotlight.gameObject.activeInHierarchy) Click(spotlight.continueButton);
            Assert.That(spotlight.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator VirtualSequencePadCommitsOnlyAfterFourCompletedPhasesAndBoardAck()
        {
            yield return ConnectUsingSavedUi();
            int revision=app.Transport.Snapshot.configRevision;
            var pad=app.view.sequencePad;
            foreach(char symbol in "ABABABABABBAABBA")Click(symbol=='A'?pad.aButton:pad.bButton);
            Assert.That(pad.IsComplete,Is.True);
            Assert.That(app.view.saveSequences.interactable,Is.True);
            Assert.That(app.Transport.Snapshot.configRevision,Is.EqualTo(revision));
            Click(app.view.saveSequences);
            Assert.That(pad.aButton.interactable,Is.False);
            Assert.That(app.Transport.Snapshot.pendingCommandId,Is.Not.Empty);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(app.Transport.Snapshot.armSequence,Is.EqualTo("ABAB"));
            Assert.That(app.Transport.Snapshot.disarmSequence,Is.EqualTo("ABBA"));
            Assert.That(app.Transport.Snapshot.configRevision,Is.EqualTo(revision+1));
            Assert.That(pad.IsComplete,Is.False,"Confirmed readback starts a fresh capture, rather than reusing confirmations.");
            Assert.That(app.view.saveSequences.interactable,Is.False);
        }

        [UnityTest]
        public IEnumerator JourneyStagesPreservePhotosAndOnlyInspectionOpensReport()
        {
            app.Navigate(1);
            var flow=app.view.journeyFlow;
            Click(flow.stageButtons[0]);
            app.view.flight.text="AY DEMO";
            Click(app.view.beforeSample);
            Click(flow.nextButton);
            Assert.That(flow.SelectedStage,Is.EqualTo(1));
            Assert.That(app.Data.journey.damageConfirmed,Is.False);
            Assert.That(app.Transport.Snapshot.recordingState,Is.EqualTo("idle"),"Stage navigation cannot manufacture a recording acknowledgement.");
            Click(app.view.afterSample);
            Click(app.view.damageYes);
            Assert.That(flow.SelectedStage,Is.EqualTo(2));
            Assert.That(app.view.claimPanel.activeInHierarchy,Is.True);
            Assert.That(app.Data.journey.photos,Has.Count.EqualTo(2));
            Click(flow.backButton);
            Assert.That(app.view.flight.text,Is.EqualTo("AY DEMO"));
            Assert.That(app.Data.journey.photos,Has.Count.EqualTo(2));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TimelineSpikeHitOpensTheOriginalEventAndItsProvenance()
        {
            yield return ConnectUsingSavedUi();
            ((SimulatedBagTransport)app.Transport).InjectMotion("impact");
            app.Navigate(0);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var timeline=app.view.timeline;
            Rect rect=timeline.rectTransform.rect;
            float inset=Mathf.Min(timeline.padding,Mathf.Min(rect.width,rect.height)*.2f);
            Vector2 local=new Vector2(rect.center.x,rect.yMin+inset+(rect.height-inset*2)*.5f);
            var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                {position=RectTransformUtility.WorldToScreenPoint(null,timeline.rectTransform.TransformPoint(local))};
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer,hits);
            Assert.That(hits,Is.Not.Empty,"The spike must be in the visible, raycastable scroll viewport.");
            Assert.That(hits[0].gameObject,Is.EqualTo(timeline.gameObject),"A label or overlay must not block tapping the chart.");
            timeline.OnPointerClick(pointer);
            Assert.That(app.view.detailPanel.activeSelf,Is.True);
            Assert.That(app.view.detailTitle.text,Is.EqualTo("Impact"));
            Assert.That(app.view.detailBody.text,Does.Contain("simulated device"));
            Assert.That(app.view.detailBody.text,Does.Contain("not a physical measurement"));
            Assert.That(app.view.detailBody.text,Does.Contain("2.40 g"));
            Click(app.view.closeDetail);
            Assert.That(app.view.detailPanel.activeSelf,Is.False);
        }

        [UnityTest]
        public IEnumerator ExpandedPresenterControlsFitInsideTheirAvailablePanel()
        {
            app.Navigate(0);
            ShowPresenter();
            yield return null;
            var fitter=app.view.demoPanel.GetComponent<PresenterGridFit>();
            Assert.That(fitter,Is.Not.Null);
            Canvas.ForceUpdateCanvases();fitter.RefreshLayout();Canvas.ForceUpdateCanvases();
            yield return null;
            var panel=(RectTransform)app.view.demoPanel.transform;
            Rect bounds=panel.rect;
            foreach(var button in app.view.demoPanel.GetComponentsInChildren<Button>())
            {
                var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);
                foreach(var corner in corners)
                {
                    Vector3 local=panel.InverseTransformPoint(corner);
                    Assert.That(local.x,Is.InRange(bounds.xMin-1,bounds.xMax+1),button.name+" extends past the available width.");
                    Assert.That(local.y,Is.InRange(bounds.yMin-1,bounds.yMax+1),button.name+" extends past the available height.");
                }
            }
        }

        private IEnumerator LoadSavedApplication()
        {
            AsyncOperation loading = EditorSceneManager.LoadSceneAsyncInPlayMode(AppScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            while (!loading.isDone) yield return null;
            yield return null;
            app = UnityEngine.Object.FindAnyObjectByType<AppComposition>();
            Assert.That(app, Is.Not.Null, "The saved application scene must contain AppComposition.");
            Assert.That(app.view, Is.Not.Null);
            Assert.That(app.Transport, Is.TypeOf<SimulatedBagTransport>(), "Application PlayMode tests require the saved Editor simulator default.");
        }

        private IEnumerator ConnectUsingSavedUi()
        {
            app.Navigate(3);
            Click(app.view.scan);
            yield return null;
            ListRowView[] rows = app.view.discoveredDevices.GetComponentsInChildren<ListRowView>();
            Assert.That(rows, Has.Length.EqualTo(1));
            Click(rows[0].action);
            yield return null;
            Assert.That(app.Transport.Snapshot.connected, Is.True);
            Assert.That(app.Transport.Snapshot.authenticated, Is.True);
            Assert.That(app.view.connectionLabel.text, Is.EqualTo("Connected"));
            Assert.That(app.view.sequencePad.aButton.interactable, Is.True);
            Assert.That(app.view.saveSequences.interactable, Is.False, "A fresh confirmed configuration still requires new code entry/repeats before saving.");
        }

        private void FillSequences(string arm, string disarm)
        {
            app.view.armCode.text = arm; app.view.disarmCode.text = disarm;
            app.view.confirmArmCode.text = arm; app.view.confirmDisarmCode.text = disarm;
        }

        private void ShowPresenter()
        {
            if (!app.view.demoPanel.activeSelf) Click(app.view.demoToggle);
        }

        private void Click(Button button)
        {
            Assert.That(button, Is.Not.Null);
            // Traverse the saved stage/disclosure controls, as a presenter would,
            // before exercising controls moved out of the primary demo path.
            if(!button.gameObject.activeInHierarchy)
            {
                for(int index=0;index<app.view.selector.screens.Length;index++)
                    if(button.transform.IsChildOf(app.view.selector.screens[index].transform))app.Navigate(index);
                var flow=app.view.journeyFlow;
                if(flow)for(int stage=0;stage<flow.stages.Length;stage++)
                    if(button.transform.IsChildOf(flow.stages[stage].transform))flow.stageButtons[stage].onClick.Invoke();
                var disclosures=app.gameObject.scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<DemoDisclosure>(true))
                    .Where(section=>section.content && button.transform.IsChildOf(section.content.transform))
                    .OrderBy(section=>section.transform.GetComponentsInParent<Transform>(true).Length);
                foreach(var section in disclosures)if(!section.expanded)section.toggle.onClick.Invoke();
            }
            Assert.That(button.gameObject.activeInHierarchy, Is.True, "The intended control is hidden: " + button.name);
            Assert.That(button.interactable, Is.True, "The intended control is disabled: " + button.name);
            button.onClick.Invoke();
        }
    }
}
#endif

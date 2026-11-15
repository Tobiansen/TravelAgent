#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TravelAgent.Core;
using TravelAgent.Integrations;
using TravelAgent.Integrations.Claims;
using TravelAgent.Integrations.Import;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TravelAgent.Tests.PlayMode
{
    /// <summary>Cold demo sessions start visually blank; ordinary pauses and provider visits retain current work.</summary>
    public sealed class FreshSessionFlowTests
    {
        private const string ScenePath = "Assets/_App/Scenes/App.unity";
        private AppComposition app;
        private string root;
        private string original;
        private string demoJourneyId, realJourneyId;
        private static readonly MethodInfo pause = typeof(AppComposition).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo receive = typeof(AndroidBagTransport).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo pollTicket = typeof(AppComposition).GetMethod("PollIncomingTicket", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo nextTicketPoll = typeof(AppComposition).GetField("nextSharePoll", BindingFlags.Instance | BindingFlags.NonPublic);

        [Serializable] private sealed class Packet
        {
            public string type;
            public int protocolVersion = 1;
            public bool connected;
            public DeviceSnapshot snapshot;
            public MotionSample sample;
        }

        [UnitySetUp] public IEnumerator SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "TravelAgent-fresh-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            original = Path.Combine(root, "original-photo.bin");
            File.WriteAllBytes(original, new byte[] { 1, 2, 3, 4, 5 });
            demoJourneyId = Seed(true);
            realJourneyId = Seed(false);
            AppComposition.TestStorageRoot = root;
            AppComposition.TestFreshSessionOnLaunch = null;
            yield return Reload();
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("Fresh-session-cleanup-" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            app = null;
            AppComposition.TestStorageRoot = null;
            AppComposition.TestFreshSessionOnLaunch = null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private JsonAppStore Store(bool demo) => new JsonAppStore(root, demo, value => JsonUtility.ToJson(value, true), json => JsonUtility.FromJson<AppData>(json));

        private string Seed(bool demo)
        {
            var data = new AppData { bagName = "Retained test bag", confirmedArmSequence = "AABB", confirmedDisarmSequence = "BBAA", confirmedConfigRevision = 17 };
            data.journey.airline = data.journey.airlineConfirmedValue = "Finnair";
            data.journey.airlineConfirmedAtUtc = "2026-11-14T20:00:00Z";
            data.journey.airlineConfirmationSource = "shared-text-local";
            data.journey.ticketImportRequestId = demo ? "retired-demo-ticket" : "retired-real-ticket";
            data.journey.route = "CPH → HEL";
            data.journey.damageConfirmed = true;
            data.journey.inspectionRecordedAtUtc = "2026-11-14T20:01:00Z";
            data.journey.damageDescription = "Old test damage";
            data.journey.claimDraft = "Old test report";
            data.journey.stage = "Damage confirmed by traveler";
            data.journey.claimGuidanceContext = "Finnair|CPH → HEL||";
            // Fixture origin avoids decoding the sentinel; its original file must still survive archival.
            data.journey.photos.Add(new PhotoEvidence { stage = "before", path = original, origin = "fixture illustration" });
            data.events.Add(new BagEvent { protocolVersion = 1, deviceId = "fresh-test-bag", bootId = "same-boot", sequence = 1,
                elapsedMs = 1000, type = "movement", origin = "Live device", quality = "live_connected" });
            data.motionSamples.Add(Sample(1, 1000));
            var store = Store(demo);
            store.Save(data);
            File.WriteAllText(Path.Combine(store.RootDirectory, "last-guidance.json"), JsonUtility.ToJson(new GuidanceResponse { origin = "fixture", status = "needsConfirmation" }));
            return data.journey.id;
        }

        private IEnumerator Reload()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            app = UnityEngine.Object.FindAnyObjectByType<AppComposition>();
            Assert.That(app, Is.Not.Null);
        }

        private void ConfirmAirline(string airline)
        {
            app.Navigate(1);
            app.view.airline.text = airline;
            app.view.confirmAirline.onClick.Invoke();
        }

        private static MotionSample Sample(long sequence, long elapsed) => new MotionSample {
            protocolVersion = 1, deviceId = "fresh-test-bag", bootId = "same-boot", sequence = sequence,
            elapsedMs = elapsed, deltaG = .2f, gyroDps = 30, sensorFresh = true, sampleAgeMs = 5 };

        private void ConnectAndSample()
        {
            var adapter = (AndroidBagTransport)app.Transport;
            receive.Invoke(adapter, new object[] { JsonUtility.ToJson(new Packet { type = "transport", connected = true }) });
            receive.Invoke(adapter, new object[] { JsonUtility.ToJson(new Packet { type = "snapshot", snapshot = new DeviceSnapshot {
                connected = true, authenticated = true, deviceId = "fresh-test-bag", bootId = "same-boot", guardState = "disarmed",
                armSequence = "AABB", disarmSequence = "BBAA", configRevision = 17, capabilities = new[] { "liveMotion", "liveEvents", "setGuardState", "setButtonSequences" } } }) });
            receive.Invoke(adapter, new object[] { JsonUtility.ToJson(new Packet { type = "motion", sample = Sample(2, 1200) }) });
        }

        private void AssertBlank()
        {
            Assert.That(app.Data.journey.airline, Is.Empty);
            Assert.That(app.Data.journey.damageConfirmed, Is.False);
            Assert.That(app.Data.journey.inspectionRecordedAtUtc, Is.Empty);
            Assert.That(app.Data.journey.claimDraft, Is.Empty);
            Assert.That(app.Data.journey.photos, Is.Empty);
            Assert.That(app.Data.events, Is.Empty);
            Assert.That(app.Data.motionSamples, Is.Empty);
            Assert.That(app.LiveMotionHistory.Count, Is.Zero);
            Assert.That(app.Guidance, Is.Null);
            Assert.That(app.view.airline.text, Is.Empty);
            Assert.That(app.view.route.text, Is.Empty);
            Assert.That(app.view.damageDescription.text, Is.Empty);
            Assert.That(app.view.claimDraft.text, Is.Empty);
            Assert.That(app.view.ticketAirlineResult.gameObject.activeSelf, Is.False);
            Assert.That(app.view.beforePhoto.enabled, Is.False);
            Assert.That(app.view.afterPhoto.enabled, Is.False);
            Assert.That(app.view.claimPanel.activeSelf, Is.False);
            Assert.That(app.view.journeyFlow.InspectionOutcome, Is.Null);
            Assert.That(app.view.journeyFlow.SelectedStage, Is.Zero);
            Assert.That(app.view.damageYes.targetGraphic.color, Is.Not.EqualTo(app.theme.accent));
            Assert.That(app.view.damageNo.targetGraphic.color, Is.Not.EqualTo(app.theme.accent));
            Assert.That(app.view.damageYes.targetGraphic.color, Is.EqualTo(app.view.damageNo.targetGraphic.color));
            Assert.That(app.view.historyStatus.text, Does.Contain("No events"));
        }

        [UnityTest] public IEnumerator DefaultLaunchAndFirstProviderVisitsArchiveAndClearOnlyTripData()
        {
            Assert.That(app.configuration.freshDemoSessionOnLaunch, Is.True, "The saved demo configuration must opt in.");
            AssertBlank();
            Assert.That(app.Data.journey.id, Is.Not.EqualTo(demoJourneyId));
            Assert.That(Directory.GetFiles(Path.Combine(root, "Demo", "JourneyHistory")).Any(path => File.ReadAllText(path).Contains(demoJourneyId)), Is.True);
            app.SetHardware(false);
            AssertBlank();
            Assert.That(app.Data.journey.id, Is.Not.EqualTo(realJourneyId));
            Assert.That(Directory.GetFiles(Path.Combine(root, "Real", "JourneyHistory")).Any(path => File.ReadAllText(path).Contains(realJourneyId)), Is.True);
            Assert.That(app.Data.bagName, Is.EqualTo("Retained test bag"));
            Assert.That(app.Data.confirmedArmSequence, Is.EqualTo("AABB"));
            Assert.That(app.Data.confirmedDisarmSequence, Is.EqualTo("BBAA"));
            Assert.That(app.Data.confirmedConfigRevision, Is.EqualTo(17));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(original));
            yield return null;
        }

        [UnityTest] public IEnumerator SameInstanceProviderRoundTripKeepsEachCurrentSessionAndGatewayFields()
        {
            ConfirmAirline("Lufthansa");
            app.view.backendUrl.SetTextWithoutNotify("http://fixture-laptop:8788");
            app.view.backendToken.SetTextWithoutNotify("fixture-only-token");
            string currentDemo = app.Data.journey.id;
            app.view.damageYes.onClick.Invoke();
            app.SetHardware(false);
            ConfirmAirline("Finnair");
            string currentReal = app.Data.journey.id;
            ConnectAndSample();
            app.SetHardware(true);
            Assert.That(app.Data.journey.id, Is.EqualTo(currentDemo));
            Assert.That(app.Data.journey.airline, Is.EqualTo("Lufthansa"));
            Assert.That(app.Data.journey.damageConfirmed, Is.True);
            app.SetHardware(false);
            Assert.That(app.Data.journey.id, Is.EqualTo(currentReal));
            Assert.That(app.Data.journey.airline, Is.EqualTo("Finnair"));
            Assert.That(app.Data.motionSamples.Count, Is.EqualTo(1));
            Assert.That(app.view.backendUrl.text, Is.EqualTo("http://fixture-laptop:8788"));
            Assert.That(app.view.backendToken.text, Is.EqualTo("fixture-only-token"));
            yield return null;
        }

        [UnityTest] public IEnumerator ColdSceneReopenDropsPreviousRealSelectionsAndTrace()
        {
            app.SetHardware(false);
            ConfirmAirline("Finnair");
            app.view.damageYes.onClick.Invoke();
            ConnectAndSample();
            string previous = app.Data.journey.id;
            app.SaveJourney();
            yield return Reload();
            app.SetHardware(false);
            AssertBlank();
            Assert.That(app.Data.journey.id, Is.Not.EqualTo(previous));
            Assert.That(app.Data.confirmedConfigRevision, Is.EqualTo(17));
            Assert.That(Directory.GetFiles(Path.Combine(root, "Real", "JourneyHistory")).Any(path => File.ReadAllText(path).Contains(previous)), Is.True);
        }

        [UnityTest] public IEnumerator PauseAndResumePreserveCurrentInspectionAndMotion()
        {
            app.SetHardware(false);
            ConfirmAirline("Finnair");
            ConnectAndSample();
            app.view.damageYes.onClick.Invoke();
            string current = app.Data.journey.id;
            pause.Invoke(app, new object[] { true });
            pause.Invoke(app, new object[] { false });
            Assert.That(app.Data.journey.id, Is.EqualTo(current));
            Assert.That(app.Data.journey.airline, Is.EqualTo("Finnair"));
            Assert.That(app.Data.journey.damageConfirmed, Is.True);
            Assert.That(app.view.damageYes.targetGraphic.color, Is.EqualTo(app.theme.accent));
            Assert.That(app.Data.motionSamples.Count, Is.EqualTo(1));
            Assert.That(app.LiveMotionHistory.Count, Is.EqualTo(1));
            Assert.That(Store(false).Load().journey.id, Is.EqualTo(current));
            yield return null;
        }

        [UnityTest] public IEnumerator InspectionChoicesAreNeutralUntilRecordedAndOnlyTheChosenAnswerIsAccented()
        {
            AssertBlank();
            ConfirmAirline("Finnair");
            app.view.damageYes.onClick.Invoke();
            Assert.That(app.view.damageYes.targetGraphic.color, Is.EqualTo(app.theme.accent));
            Assert.That(app.view.damageNo.targetGraphic.color, Is.Not.EqualTo(app.theme.accent));
            Assert.That(app.view.journeyFlow.InspectionOutcome, Is.True);
            app.view.damageNo.onClick.Invoke();
            Assert.That(app.view.damageNo.targetGraphic.color, Is.EqualTo(app.theme.accent));
            Assert.That(app.view.damageYes.targetGraphic.color, Is.Not.EqualTo(app.theme.accent));
            Assert.That(app.view.journeyFlow.InspectionOutcome, Is.False);
            Assert.That(app.BeginFreshJourney(), Is.True);
            Assert.That(app.view.journeyFlow.InspectionOutcome, Is.Null);
            Assert.That(app.view.damageYes.targetGraphic.color, Is.Not.EqualTo(app.theme.accent));
            Assert.That(app.view.damageNo.targetGraphic.color, Is.Not.EqualTo(app.theme.accent));
            yield return null;
        }

        [UnityTest] public IEnumerator PersistenceOptOutStillRestoresTheUnvisitedStore()
        {
            AppComposition.TestFreshSessionOnLaunch = false;
            yield return Reload();
            app.SetHardware(false);
            Assert.That(app.Data.journey.id, Is.EqualTo(realJourneyId));
            Assert.That(app.Data.journey.airline, Is.EqualTo("Finnair"));
            Assert.That(app.view.airline.text, Is.EqualTo("Finnair"));
            Assert.That(app.Data.journey.damageConfirmed, Is.True);
            Assert.That(app.Data.journey.photos.Count, Is.EqualTo(1));
            Assert.That(app.Data.motionSamples.Count, Is.EqualTo(1));
            Assert.That(app.view.damageYes.targetGraphic.color, Is.EqualTo(app.theme.accent));
            Assert.That(Directory.Exists(Path.Combine(root, "Real", "JourneyHistory")), Is.False);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(original));
        }

        [UnityTest] public IEnumerator FailedOldInboxCleanupStaysRetiredAcrossConsecutiveColdReopensWhileNewSharesImport()
        {
            var otherProvider = new SharedTicket("retired-real-ticket", true);
            app.IncomingShareServiceOverride = otherProvider;
            nextTicketPoll.SetValue(app, 0f);
            pollTicket.Invoke(app, null);
            AssertBlank();
            Assert.That(otherProvider.acknowledgeAttempts, Is.GreaterThanOrEqualTo(1),
                "A prior imported inbox item from the unvisited provider must not become a new trip in this provider.");
            Assert.That(Store(false).Load().journey.id, Is.EqualTo(realJourneyId),
                "Remembering the other provider's consumed request must not reset its unvisited store.");

            var old = new SharedTicket("retired-demo-ticket", true);
            app.IncomingShareServiceOverride = old;
            nextTicketPoll.SetValue(app, 0f);
            pollTicket.Invoke(app, null);
            AssertBlank();
            Assert.That(old.acknowledgeAttempts, Is.GreaterThanOrEqualTo(1));
            Assert.That(app.Data.retiredTicketRequestId, Is.EqualTo("retired-demo-ticket"));

            yield return Reload();
            app.IncomingShareServiceOverride = old;
            nextTicketPoll.SetValue(app, 0f);
            pollTicket.Invoke(app, null);
            AssertBlank();
            Assert.That(old.acknowledgeAttempts, Is.GreaterThanOrEqualTo(2));
            Assert.That(app.Data.retiredTicketRequestId, Is.EqualTo("retired-demo-ticket"),
                "A clean saved trip must retain the consumed request ID even if native cleanup failed on the prior launch.");

            // A different incoming request remains a legitimate new share, not an old-trip replay.
            var fresh = new SharedTicket("new-demo-ticket", false);
            app.IncomingShareServiceOverride = fresh;
            nextTicketPoll.SetValue(app, 0f);
            pollTicket.Invoke(app, null);
            Assert.That(app.Data.journey.airline, Is.EqualTo("Finnair"));
            Assert.That(app.Data.journey.route, Is.EqualTo("CPH → HEL"));
            Assert.That(app.Data.journey.ticketImportRequestId, Is.EqualTo("new-demo-ticket"));
            Assert.That(app.Data.journey.damageConfirmed, Is.False);
            Assert.That(app.Data.journey.photos, Is.Empty);
            Assert.That(fresh.acknowledgeAttempts, Is.EqualTo(1));
            Assert.That(fresh.Peek(), Is.Null);
        }

        private sealed class SharedTicket : IIncomingShareService
        {
            private IncomingShare pending;
            private readonly bool rejectAcknowledgement;
            public int acknowledgeAttempts;
            public SharedTicket(string requestId, bool rejectAcknowledgement)
            {
                this.rejectAcknowledgement = rejectAcknowledgement;
                pending = new IncomingShare { requestId = requestId, origin = "fixture selected text", receivedAtUtc = DateTime.UtcNow.ToString("o"),
                    text = "Your boarding pass for CPH-HEL, Sun 8 Nov\nYour flight: Copenhagen (CPH)–Helsinki (HEL), Sun 8 Nov\nDownload the Finnair app" };
            }
            public IncomingShare Peek() => pending;
            public bool Acknowledge(string requestId)
            {
                acknowledgeAttempts++;
                if (rejectAcknowledgement || pending == null || pending.requestId != requestId) return false;
                pending = null;
                return true;
            }
        }
    }
}
#endif

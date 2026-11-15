#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TravelAgent.Core;
using TravelAgent.Integrations.Import;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TravelAgent.Tests.PlayMode
{
    public sealed class TicketImportFlowTests
    {
        private AppComposition app;
        private string root;
        private const string Ticket="Your boarding pass for CPH-HEL, Sun 8 Nov\nYour boarding pass is attached to this email\nPassenger: Sample Traveler\nYour flight: Copenhagen (CPH)–Helsinki (HEL), Sun 8 Nov\nDownload the Finnair app\nContact the operating carrier for information about prohibited materials.";
        [UnitySetUp] public IEnumerator SetUp()
        {
            root=Path.Combine(Path.GetTempPath(),"TravelAgent-ticket-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            AppComposition.TestStorageRoot=root;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_App/Scenes/App.unity",new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;app=UnityEngine.Object.FindAnyObjectByType<AppComposition>();app.SetHardware(true);yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            var scene=app.gameObject.scene;SceneManager.SetActiveScene(SceneManager.CreateScene("Ticket-cleanup-"+Guid.NewGuid().ToString("N")));
            yield return SceneManager.UnloadSceneAsync(scene);app=null;AppComposition.TestStorageRoot=null;
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
        [UnityTest] public IEnumerator BeforeStartsBlankAndRequiresExplicitAirlineConfirmation()
        {
            Assert.That(app.Data.journey.airline,Is.Empty);Assert.That(app.view.airline.text,Is.Empty);
            Assert.That(app.Data.journey.photos,Is.Empty);Assert.That(app.Guidance,Is.Null);
            app.view.journeyFlow.ShowStage(1);Assert.That(app.view.journeyFlow.SelectedStage,Is.Zero);
            Assert.That(app.view.journeyFlow.nextButton.interactable,Is.False);
            app.view.airline.text="Finnair";Assert.That(app.view.journeyFlow.nextButton.interactable,Is.False);
            app.view.confirmAirline.onClick.Invoke();Assert.That(app.Data.journey.airlineConfirmedValue,Is.EqualTo("Finnair"));
            Assert.That(app.view.journeyFlow.nextButton.interactable,Is.True);
            app.view.airline.text="Lufthansa";Assert.That(app.view.journeyFlow.nextButton.interactable,Is.False);
            yield return null;
        }
        [UnityTest] public IEnumerator SharedBoardingEmailImmediatelyDisplaysLocalFlightDetailsAfterCleanStart()
        {
            app.LoadFinnairSample();string previousId=app.Data.journey.id;
            var shared=new FakeIncoming(Ticket);app.IncomingShareServiceOverride=shared;
            Poll();
            Assert.That(app.Data.journey.id,Is.Not.EqualTo(previousId));Assert.That(app.Data.journey.photos,Is.Empty);
            Assert.That(app.view.beforePhoto.enabled,Is.False);Assert.That(app.view.afterPhoto.enabled,Is.False);
            Assert.That(app.view.airline.text,Is.EqualTo("Finnair"));Assert.That(app.view.ticketAirlineResult.gameObject.activeSelf,Is.True);
            Assert.That(app.view.ticketAirlineResult.text,Does.Contain("Finnair"));
            Assert.That(app.Guidance,Is.Null);Assert.That(shared.acknowledged,Is.EqualTo(1));Assert.That(shared.pending,Is.Null);
            Assert.That(Directory.GetFiles(Path.Combine(root,"Demo","JourneyHistory")),Is.Not.Empty);
            Assert.That(app.Data.journey.route,Is.EqualTo("CPH → HEL"));Assert.That(app.Data.journey.flight,Is.Empty);
            Assert.That(app.view.route.text,Is.EqualTo("CPH → HEL"));
            Assert.That(app.Data.journey.collectionDate,Is.Empty,"Flight date is not baggage collection date.");
            Assert.That(app.Data.journey.photos,Is.Empty);Assert.That(app.Data.journey.airlineConfirmationSource,Is.EqualTo("shared-text-local"));
            Assert.That(app.Data.journey.airlineConfirmedValue,Is.EqualTo("Finnair"));
            Assert.That(app.Data.journey.airlineConfirmedAtUtc,Is.Not.Empty);
            Assert.That(app.Data.journey.airlineSourceUrl,Is.Empty);Assert.That(app.Data.journey.airlineIataSourceUrl,Is.Empty);
            Assert.That(app.Data.journey.airlineSourceOrigin,Is.Empty,"Local parsing cannot be labelled as an Exa source.");
            AssertNoEarlyExaControl();
            Assert.That(app.view.confirmAirline.gameObject.activeSelf,Is.False,"A successful email import needs no extra confirmation step.");
            Assert.That(app.view.exaSpotlight.gameObject.activeSelf,Is.False,"Before does not begin Exa research or show an Exa presentation.");
            Assert.That(JsonUtility.ToJson(app.Data),Does.Not.Contain("Sample Traveler"));
            Assert.That(app.view.journeyFlow.nextButton.interactable,Is.True);
            yield return null;
        }
        [UnityTest] public IEnumerator UnsupportedSharedTicketAllowsManualAirlineWithoutExa()
        {
            var shared=new FakeIncoming("Unknown Air booking confirmation\nYour flight: Copenhagen (CPH)–Helsinki (HEL)");app.IncomingShareServiceOverride=shared;
            Poll();
            Assert.That(app.view.airline.text,Is.Empty);Assert.That(app.Data.journey.airline,Is.Empty);
            Assert.That(app.view.airline.transform.parent.gameObject.activeSelf,Is.True);
            AssertNoEarlyExaControl();Assert.That(shared.acknowledged,Is.Zero);
            Assert.That(app.view.journeyFlow.nextButton.interactable,Is.False);
            app.view.airline.text="Unknown Air";Assert.That(app.view.confirmAirline.interactable,Is.True);
            app.view.confirmAirline.onClick.Invoke();
            Assert.That(app.Data.journey.airlineConfirmedValue,Is.EqualTo("Unknown Air"));Assert.That(shared.acknowledged,Is.EqualTo(1));
            Assert.That(app.Data.journey.airlineConfirmationSource,Does.Not.Contain("exa"));
            Assert.That(app.view.exaSpotlight.gameObject.activeSelf,Is.False);Assert.That(app.view.journeyFlow.nextButton.interactable,Is.True);
            yield return null;
        }
        [UnityTest] public IEnumerator AmbiguousSharedTicketDoesNotSilentlyChooseAnAirline()
        {
            var shared=new FakeIncoming("Finnair / Lufthansa booking confirmation\nFlight AY1334 operated by Lufthansa");app.IncomingShareServiceOverride=shared;
            Poll();
            Assert.That(app.Data.journey.airline,Is.Empty);Assert.That(app.view.airline.text,Is.Empty);
            Assert.That(app.view.journeyFlow.nextButton.interactable,Is.False);Assert.That(shared.acknowledged,Is.Zero);
            AssertNoEarlyExaControl();
            app.view.airline.text="Lufthansa";app.view.confirmAirline.onClick.Invoke();
            Assert.That(app.Data.journey.airlineConfirmedValue,Is.EqualTo("Lufthansa"));Assert.That(shared.acknowledged,Is.EqualTo(1));
            Assert.That(app.view.exaSpotlight.gameObject.activeSelf,Is.False);
            yield return null;
        }
        [UnityTest] public IEnumerator SavedLocalImportRetriesCleanupWithoutRestartingTheJourney()
        {
            var shared=new FakeIncoming(Ticket) {failAcknowledgement=true};app.IncomingShareServiceOverride=shared;Poll();
            string journey=app.Data.journey.id;int archives=Directory.GetFiles(Path.Combine(root,"Demo","JourneyHistory")).Length;
            Assert.That(app.Data.journey.airline,Is.EqualTo("Finnair"));Assert.That(app.Data.journey.route,Is.EqualTo("CPH → HEL"));
            Assert.That(shared.pending,Is.Not.Null);Assert.That(shared.acknowledged,Is.Zero);
            Assert.That(app.view.confirmAirline.gameObject.activeSelf,Is.True,"Finish import retries private shared-text cleanup.");
            Assert.That(app.BeginFreshJourney(),Is.False,"Do not archive the trip while its import cleanup is pending.");
            Poll();Assert.That(app.Data.journey.id,Is.EqualTo(journey));
            shared.failAcknowledgement=false;app.view.confirmAirline.onClick.Invoke();
            Assert.That(shared.pending,Is.Null);Assert.That(shared.acknowledged,Is.EqualTo(1));
            Assert.That(app.Data.journey.id,Is.EqualTo(journey));
            Assert.That(Directory.GetFiles(Path.Combine(root,"Demo","JourneyHistory")).Length,Is.EqualTo(archives));
            Assert.That(app.view.confirmAirline.gameObject.activeSelf,Is.False);Assert.That(app.view.exaSpotlight.gameObject.activeSelf,Is.False);
            yield return null;
        }
        [UnityTest] public IEnumerator PendingShareCannotBeConfirmedIntoAnotherDataStore()
        {
            app.IncomingShareServiceOverride=new FakeIncoming("Finnair / Lufthansa booking confirmation");Poll();var transport=app.Transport;string journey=app.Data.journey.id;
            app.SetHardware(false);Assert.That(app.Transport,Is.SameAs(transport));Assert.That(app.Data.journey.id,Is.EqualTo(journey));
            app.view.discardTicket.onClick.Invoke();app.SetHardware(false);Assert.That(app.Transport,Is.Not.SameAs(transport));
            yield return null;
        }
        [UnityTest] public IEnumerator RepeatedSharePollingDoesNotRestartOrOverwriteActiveImport()
        {
            var shared=new FakeIncoming("Finnair / Lufthansa booking confirmation");app.IncomingShareServiceOverride=shared;Poll();string journey=app.Data.journey.id;int archives=Directory.GetFiles(Path.Combine(root,"Demo","JourneyHistory")).Length;
            Poll();Poll();Assert.That(app.Data.journey.id,Is.EqualTo(journey));Assert.That(Directory.GetFiles(Path.Combine(root,"Demo","JourneyHistory")).Length,Is.EqualTo(archives));
            Assert.That(app.BeginFreshJourney(),Is.False);Assert.That(shared.acknowledged,Is.Zero);yield return null;
        }
        [UnityTest] public IEnumerator NewDemoArchivesPhotosAndGuidanceInsteadOfPrefillingNextRun()
        {
            app.LoadFinnairSample();string previousId=app.Data.journey.id;
            Assert.That(app.Data.journey.photos.Count,Is.EqualTo(1));app.view.loadFinnairSample.onClick.Invoke();
            Assert.That(app.Data.journey.id,Is.Not.EqualTo(previousId));Assert.That(app.Data.journey.airline,Is.Empty);
            Assert.That(app.Data.journey.photos,Is.Empty);Assert.That(app.Guidance,Is.Null);Assert.That(app.Data.journey.damageConfirmed,Is.False);
            Assert.That(app.Data.events.Count,Is.EqualTo(3));Assert.That(app.view.journeyFlow.SelectedStage,Is.Zero);
            Assert.That(app.view.ticketAirlineResult.gameObject.activeSelf,Is.False);yield return null;
        }
        private void Poll()
        {
            typeof(AppComposition).GetField("nextSharePoll",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,0f);
            typeof(AppComposition).GetMethod("PollIncomingTicket",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(app,null);
        }
        private void AssertNoEarlyExaControl()
        {
            Assert.That(app.gameObject.scene.GetRootGameObjects().SelectMany(item=>item.GetComponentsInChildren<Transform>(true))
                .Any(item=>item.name=="IdentifyTicketAirline"),Is.False,"Before contains no airline-identification Exa button.");
        }
        private sealed class FakeIncoming : IIncomingShareService
        {
            public IncomingShare pending;public int acknowledged;public bool failAcknowledgement;
            public FakeIncoming(string text){pending=new IncomingShare {requestId=Guid.NewGuid().ToString("N"),text=text,origin="Android shared text / unverified source",receivedAtUtc=DateTime.UtcNow.ToString("O")};}
            public IncomingShare Peek()=>pending;
            public bool Acknowledge(string id){if(failAcknowledgement || pending==null || pending.requestId!=id)return false;pending=null;acknowledged++;return true;}
        }
    }
}
#endif

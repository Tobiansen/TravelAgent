#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TravelAgent.Core;
using TravelAgent.Integrations.Claims;
using TravelAgent.Integrations.Photos;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TravelAgent.Tests.PlayMode
{
    public sealed class FinnairFlowIntegrationTests
    {
        private AppComposition app;
        private string root;
        [UnitySetUp] public IEnumerator SetUp()
        {
            root=Path.Combine(Path.GetTempPath(),"TravelAgent-Finnair-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);AppComposition.TestStorageRoot=root;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_App/Scenes/App.unity",new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;app=UnityEngine.Object.FindAnyObjectByType<AppComposition>();
            app.SetHardware(true);yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            var scene=app.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("Finnair-cleanup-"+Guid.NewGuid().ToString("N")));
            yield return SceneManager.UnloadSceneAsync(scene);app=null;AppComposition.TestStorageRoot=null;
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
        [UnityTest] public IEnumerator SampleCasePreservesPreviousDemoAndRealEvidenceAndNeedsInspection()
        {
            app.view.flight.text="Earlier sample notes";app.SaveJourney();
            var real=new JsonAppStore(root,false,d=>JsonUtility.ToJson(d),s=>JsonUtility.FromJson<AppData>(s));
            var sentinel=new AppData();sentinel.journey.flight="REAL SENTINEL";real.Save(sentinel);
            app.LoadFinnairSample();
            Assert.That(app.Data.journey.damageConfirmed,Is.False);
            Assert.That(app.Data.journey.inspectionRecordedAtUtc,Is.Empty);
            Assert.That(app.Data.events.Count,Is.EqualTo(3));
            Assert.That(app.Data.events.All(e=>e.origin=="Demo fixture" && e.peakG==0),Is.True);
            Assert.That(app.Guidance,Is.Null);
            Assert.That(app.view.inspectionStatus.text,Does.Contain("3 disturbances"));
            Assert.That(Directory.GetFiles(Path.Combine(root,"Demo","CaseHistory"),"*.json").Any(f=>File.ReadAllText(f).Contains("Earlier sample notes")),Is.True);
            string id=app.Data.journey.id;app.LoadFinnairSample();
            Assert.That(app.Data.journey.id,Is.EqualTo(id));Assert.That(app.Data.events.Count,Is.EqualTo(3));
            Assert.That(real.Load().journey.flight,Is.EqualTo("REAL SENTINEL"));
            app.view.inspectBagContents.onClick.Invoke();
            Assert.That(app.view.selector.selectedScreen,Is.EqualTo(1));
            Assert.That(app.view.journeyFlow.SelectedStage,Is.EqualTo(1));
            Assert.That(app.Data.journey.damageConfirmed,Is.False);
            yield return null;
        }
        [UnityTest] public IEnumerator NoDamageStopsResearchAndDraftGenerationEvenWhenInvokedProgrammatically()
        {
            app.LoadFinnairSample();app.view.inspectBagContents.onClick.Invoke();
            app.view.damageNo.onClick.Invoke();
            Assert.That(app.Data.journey.damageConfirmed,Is.False);
            Assert.That(app.view.journeyFlow.nextButton.gameObject.activeSelf,Is.False);
            Assert.That(app.view.inspectBagContents.transform.parent.gameObject.activeSelf,Is.False);
            Invoke("Research");Invoke("GenerateDraft",true);
            Assert.That(app.Guidance,Is.Null);Assert.That(app.Data.journey.claimDraft,Is.Empty);
            Assert.That(app.Data.journey.submissionReference,Is.Empty);
            // Restoring a previously saved result cannot silently create a report for a completed no-damage inspection.
            Invoke("ApplyGuidance",new GuidanceResponse {airline="Finnair",origin="fixture",status="ready",items=new[]{new GuidanceItem {kind="evidence",text="Damage photos"}}},false);
            Assert.That(app.Data.journey.claimDraft,Is.Empty);
            Assert.That(app.view.openForm.interactable,Is.False);
            Assert.That(app.view.claimPanel.activeSelf,Is.False);
            yield return null;
        }
        [UnityTest] public IEnumerator ConfirmedDamageGetsInitialReportStepsAndKeepsPhotosOutOfTheRequest()
        {
            app.LoadFinnairSample();app.view.inspectBagContents.onClick.Invoke();app.view.damageYes.onClick.Invoke();
            if(!app.view.guidanceProvider.text.Contains("FIXTURE"))app.view.toggleGuidance.onClick.Invoke();
            app.view.research.onClick.Invoke();yield return new WaitForSecondsRealtime(.2f);
            Assert.That(app.Guidance,Is.Not.Null);Assert.That(app.Guidance.origin,Is.EqualTo("fixture"));
            Assert.That(app.Guidance.reportingTargetKind,Is.EqualTo("official-guidance"));
            Assert.That(app.Guidance.reportingUrl,Does.StartWith("https://www.finnair.com/"));
            Assert.That(app.Guidance.items.Any(item=>(item.text??"").IndexOf("photo",StringComparison.OrdinalIgnoreCase)>=0),Is.True);
            Assert.That(app.view.takeDamagePhoto.gameObject.activeInHierarchy,Is.True);
            Assert.That(app.view.chooseDamagePhoto.gameObject.activeInHierarchy,Is.True);
            Assert.That(app.Data.journey.submissionConfirmedAtUtc,Is.Empty);
            CollectionAssert.AreEquivalent(new[]{"airline","country","route","issueType","collectionDate"},typeof(ClaimResearchRequest).GetFields().Select(field=>field.Name));
        }
        [UnityTest] public IEnumerator RecoveredCameraPhotoRequiresExplicitReviewThenExportsUntouchedOriginal()
        {
            app.Navigate(1);app.view.damageYes.onClick.Invoke();
            var fake=new CameraStub {recovery=MakePhoto("demo:"+app.Data.journey.id+":after")};app.CameraServiceOverride=fake;
            var original=fake.recovery;
            yield return (IEnumerator)Invoke("RecoverCameraPhoto");
            Assert.That(app.Data.journey.photos,Is.Empty);
            Assert.That(app.view.recoveryPanel.activeSelf,Is.True);Assert.That(app.view.recoveryUse.interactable,Is.True);
            app.view.recoveryUse.onClick.Invoke();
            Assert.That(app.Data.journey.photos,Has.Count.EqualTo(1));Assert.That(fake.released,Is.EqualTo("capture-1"));
            var photo=app.Data.journey.photos[0];Assert.That(photo.origin,Is.EqualTo("local camera capture"));Assert.That(photo.capturedAtUtc,Is.Empty);
            Assert.That(photo.captureRequestedAtUtc,Is.EqualTo(original.captureRequestedAtUtc));
            Assert.That(photo.captureReturnedAtUtc,Is.EqualTo(original.captureReturnedAtUtc));
            CollectionAssert.AreEqual(File.ReadAllBytes(original.path),File.ReadAllBytes(photo.path));
            app.view.export.onClick.Invoke();
            string exported=Path.Combine(app.LastExportPath,photo.id+".png");
            CollectionAssert.AreEqual(File.ReadAllBytes(original.path),File.ReadAllBytes(exported));
            Assert.That(File.ReadAllText(Path.Combine(app.LastExportPath,"evidence-files.json")),Does.Contain(original.captureRequestedAtUtc));
            Assert.That(app.Data.journey.submissionReference,Is.Empty);
            yield return null;
        }
        [UnityTest] public IEnumerator MismatchedOrUnconfirmedRecoveryCannotAttachAndCanBeDeferredOrDiscarded()
        {
            var fake=new CameraStub {recovery=MakePhoto("real:another-journey:after")};app.CameraServiceOverride=fake;
            yield return (IEnumerator)Invoke("RecoverCameraPhoto");
            Assert.That(app.view.recoveryUse.interactable,Is.False);
            app.view.recoveryUse.onClick.Invoke();Assert.That(app.Data.journey.photos,Is.Empty);
            app.view.recoveryLater.onClick.Invoke();Assert.That(app.view.recoveryPanel.activeSelf,Is.False);
            app.view.beforeCapture.onClick.Invoke();Assert.That(app.view.recoveryPanel.activeSelf,Is.True);
            app.view.recoveryDiscard.onClick.Invoke();Assert.That(fake.released,Is.EqualTo("capture-1"));
            yield return null;yield return null;
            fake.recovery=new PhotoSelection {requestId="pending-2",context="demo:"+app.Data.journey.id+":after",pending=true,recovered=true};
            yield return (IEnumerator)Invoke("RecoverCameraPhoto");
            Assert.That(app.view.recoveryUse.interactable,Is.False);Assert.That(app.Data.journey.photos,Is.Empty);
            app.view.recoveryDiscard.onClick.Invoke();Assert.That(fake.released,Is.EqualTo("pending-2"));
        }
        [UnityTest] public IEnumerator CancelledCameraRetainsExistingEvidenceAndReenablesPhotoActions()
        {
            app.Navigate(1);app.view.beforeSample.onClick.Invoke();int count=app.Data.journey.photos.Count;
            app.CameraServiceOverride=new CameraStub {capture=new PhotoSelection {cancelled=true}};
            app.view.beforeCapture.onClick.Invoke();yield return null;yield return null;
            Assert.That(app.Data.journey.photos.Count,Is.EqualTo(count));
            Assert.That(app.view.beforeCapture.interactable,Is.True);
            Assert.That(app.view.toast.text,Does.Contain("cancelled"));
        }
        [UnityTest] public IEnumerator FailedCurrentSavePreventsAStoreSwitchAndPreservesTheForm()
        {
            var data=app.Data;var transport=app.Transport;
            app.SaveJourney();string state=Path.Combine(root,"Demo","app-state.json");
            using(var locked=new FileStream(state,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                app.view.flight.text="UNSAVED FORM SENTINEL";
                app.SetHardware(false);
                Assert.That(app.Data,Is.SameAs(data));Assert.That(app.Transport,Is.SameAs(transport));
                Assert.That(app.view.flight.text,Is.EqualTo("UNSAVED FORM SENTINEL"));
                Assert.That(app.view.providerLabel.text,Is.EqualTo("DEMO"));
            }
            app.SaveJourney();yield return null;
        }
        [UnityTest] public IEnumerator LegacyEvidenceExclusionDoesNotBecomeThePhotoInstructionSummary()
        {
            app.Navigate(1);app.view.damageYes.onClick.Invoke();
            Invoke("ApplyGuidance",new GuidanceResponse {airline="Finnair",origin="cached",status="needsConfirmation",items=new[]{new GuidanceItem {kind="evidence",text="Normal wear is excluded from compensation."}}},false);
            Assert.That(app.view.reportingSummary.text,Does.Not.Contain("Normal wear"));
            Assert.That(app.view.reportingSummary.text,Does.Contain("Review the source"));
            yield return null;
        }
        private object Invoke(string method,params object[] arguments) => typeof(AppComposition).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(app,arguments);
        private PhotoSelection MakePhoto(string context)
        {
            string path=Path.Combine(root,Guid.NewGuid().ToString("N")+".png");
            var image=new Texture2D(2,2);File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.Destroy(image);
            return new PhotoSelection {path=path,previewPath=path,requestId="capture-1",context=context,origin="local camera capture",recovered=true,captureRequestedAtUtc="2026-11-14T17:00:00Z",captureReturnedAtUtc="2026-11-14T17:00:10Z"};
        }
        private sealed class CameraStub : ICameraService
        {
            public PhotoSelection capture,recovery;public string released;
            public IEnumerator Capture(string context,Action<PhotoSelection> completed) {completed(capture ?? new PhotoSelection {cancelled=true});yield break;}
            public IEnumerator Recover(Action<PhotoSelection> completed) {completed(recovery);yield break;}
            public void Release(string requestId) {released=requestId;if(recovery?.requestId == requestId)recovery=null;}
        }
    }
}
#endif

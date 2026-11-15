using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TravelAgent.Core;
using TravelAgent.Integrations.Photos;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    public sealed partial class AppComposition
    {
        private readonly HashSet<Image> loadingPreviews = new HashSet<Image>();
        private PhotoSelection recoveredPhoto;
        private ICameraService cameraService;
#if UNITY_EDITOR
        public ICameraService CameraServiceOverride { get; set; }
#endif

        private static bool IsDisturbance(BagEvent item) => item != null &&
            (item.type == "bump" || item.type == "movement" || item.type == "impact" || item.type == "opening");

        private int PendingDisturbances() => Data.events.Skip(Math.Max(0, Data.journey.inspectedEventCount)).Count(IsDisturbance);

        private void RefreshInspectionPrompt()
        {
            if(Data == null || !view || !view.inspectionStatus)return;
            int count = PendingDisturbances();
            view.inspectionStatus.text = count > 0
                ? (simulated ? "DEMO · " : "") + count + (count == 1 ? " disturbance recorded" : " disturbances recorded") + "\nCheck your suitcase and its contents."
                : string.IsNullOrEmpty(Data.journey.inspectionRecordedAtUtc) ? "Check your bag and contents after collection."
                : Data.journey.damageConfirmed ? "Inspection recorded · damage reported by you" : "Inspection complete · no damage reported";
            view.inspectBagContents.transform.parent.gameObject.SetActive(count > 0);
        }

        private void InspectBagAndContents()
        {
            if(!AirlineConfirmed()){Navigate(1);view.journeyFlow.ShowStage(0);Toast("Confirm the airline in Before first.");return;}
            view.detailPanel.SetActive(false);
            Navigate(1);
            view.journeyFlow.ShowStage(1);
        }

        private void RecordInspection(bool damageFound)
        {
            SaveJourney();
            var journey = Data.journey;
            journey.damageConfirmed = damageFound;
            journey.inspectionRecordedAtUtc = Utc();
            journey.stage = damageFound ? "Damage confirmed by traveler" : "Complete · no damage reported";
            RecordTravelerEvent("inspection", damageFound
                ? "Traveler reported finding damage after checking the bag and contents."
                : "Traveler reported no damage after checking the bag and contents.");
            journey.inspectedEventCount = Data.events.Count;
            // A request already in flight cannot prepare a report after a no-damage decision.
            if(!damageFound) InvalidateGuidance();
            if(!string.IsNullOrEmpty(journey.claimDraft))journey.claimDraftNeedsReview=true;
            Save(); RefreshJourney();
            if(damageFound) view.journeyFlow.ShowStage(2);
            Toast(damageFound ? "Damage noted. Get the airline’s reporting steps." : "Inspection complete. Your evidence is retained.");
        }

        private void RefreshInspectionChoices()
        {
            bool recorded = !string.IsNullOrEmpty(Data.journey.inspectionRecordedAtUtc);
            foreach (var button in new[] { view.damageYes, view.damageNo }) {
                bool selected = recorded && (button == view.damageYes ? Data.journey.damageConfirmed : !Data.journey.damageConfirmed);
                if (button && button.targetGraphic) button.targetGraphic.color = selected ? theme.accent : theme.secondary;
            }
        }

        private void RefreshReportSummary()
        {
            if(!view || Data == null)return;
            int count = Data.journey.photos.Count(p => p.stage == "after");
            int samples = Data.journey.photos.Count(p => p.stage == "after" && p.origin == "fixture illustration");
            view.reportPhotoStatus.text = count == 0 ? "No damage photos added · take or choose a photo"
                : count + (count == 1 ? " after-collection photo" : " after-collection photos") + (samples > 0 ? " · includes DEMO illustration" : " · saved locally");
            if(!Data.journey.damageConfirmed) view.reportingSummary.text = "Inspect your bag and contents before preparing a damage report.";
            else if(Guidance == null) view.reportingSummary.text = "Get the airline’s instructions, add the requested evidence, then continue on its website.";
            else if(Guidance.status == "error") view.reportingSummary.text = "Research is unavailable. Your photos and notes are retained; retry or use the labelled offline reference.";
            else
            {
                var evidence = (Guidance.items ?? Array.Empty<TravelAgent.Integrations.Claims.GuidanceItem>())
                    .FirstOrDefault(item => (item.kind ?? "").IndexOf("evidence",StringComparison.OrdinalIgnoreCase)>=0 &&
                        System.Text.RegularExpressions.Regex.IsMatch(item.text ?? "",@"\b(?:photos?|photographs?|pictures?|images?)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                view.reportingSummary.text = evidence == null ? "Reporting steps ready. Review the source and requested evidence, then open the airline’s website."
                    : evidence.text;
            }
            var label=view.openForm.GetComponentInChildren<TMPro.TMP_Text>(true);
            view.reportingSummary.overflowMode=TMPro.TextOverflowModes.Ellipsis;
            view.reportingSummary.maxVisibleLines=3;
            if(label)label.text = Guidance?.reportingTargetKind == "verified-partner" ? "Open Finnair reporting page" : Guidance?.reportingTargetKind == "official-guidance" ? "Open airline reporting instructions" : "Open airline reporting page";
            view.openForm.interactable = Data.journey.damageConfirmed && Guidance != null && Guidance.status != "error" && TravelAgent.Integrations.Claims.ReportingLinks.CanOpen(Guidance);
            view.openForm.gameObject.SetActive(view.openForm.interactable);
            view.reportingSummary.gameObject.SetActive(Guidance!=null);
            view.guidanceStatus.gameObject.SetActive(Guidance!=null);
            view.reportPhotoStatus.transform.parent.gameObject.SetActive(Guidance!=null || count>0);
            var requirements=view.guidanceItems.GetComponentInParent<DemoDisclosure>(true);
            if(requirements)requirements.gameObject.SetActive(Guidance!=null);
        }

        /// <summary>Explicit presenter action: archive the prior Demo state, never operate a board.</summary>
        public void LoadFinnairSample()
        {
            if(!simulated) { Toast("Switch to Demo mode to load the sample Finnair case."); return; }
            if(photoSelecting || recoveredPhoto != null) { Toast("Finish the open photo action before loading a sample case."); return; }
            if(Data.journey.demoCaseId == "finnair-cracked-wheel-v1") { Navigate(0); Toast("Finnair sample already loaded. Tap a disturbance to inspect."); return; }
            SaveJourney();
            string history=Path.Combine(store.RootDirectory,"CaseHistory");
            try
            {
                Directory.CreateDirectory(history);
                File.WriteAllText(Path.Combine(history,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".json"),JsonUtility.ToJson(Data,true));
            }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException)
            {Toast("Could not archive the previous demo. Its data is retained; the sample case was not loaded.");return;}
            var priorJourney=Data.journey;var priorEvents=Data.events;
            Data.journey=new JourneyRecord {
                demoCaseId="finnair-cracked-wheel-v1", airline="Finnair",flight="AY · SAMPLE FLIGHT",route="Helsinki → Copenhagen (sample)",country="Denmark",
                bagDescription="DEMO · charcoal suitcase with red trim",collectionDate=DateTimeOffset.Now.ToString("yyyy-MM-dd"),
                stage="DEMO · bag collected; inspect bag and contents",
                damageDescription="DEMO SCENARIO · After collection, I found a crack in the shell beside the left wheel. I checked the contents and found no visible damage."
            };
            Data.journey.airlineConfirmedValue="Finnair";Data.journey.airlineConfirmedAtUtc=Utc();Data.journey.airlineConfirmationSource="demo-fixture";
            Data.journey.photos.Add(new PhotoEvidence {stage="before",origin="fixture illustration",path="fixture-before.png",addedAtUtc=Utc()});
            Data.events=new List<BagEvent>();
            string boot=Guid.NewGuid().ToString("N");
            string[] kinds={"bump","movement","impact"};
            for(int index=0;index<kinds.Length;index++) Data.events.Add(new BagEvent {
                deviceId="finnair-demo",bootId=boot,sequence=index+1,type=kinds[index],recordedAtUtc=DateTimeOffset.UtcNow.AddMinutes(-15+index*5).ToString("O"),
                origin="Demo fixture",quality="Illustrative event; not received from a board. No acceleration measurement supplied.",
                summary=index==2 ? "DEMO · handling disturbance. Check your suitcase and its contents." : "DEMO · illustrative handling event."
            });
            if(!Save()) {Data.journey=priorJourney;Data.events=priorEvents;PopulateJourney();return;}
            ledger=new EventLedger(Data.events);
            InvalidateGuidance();PopulateJourney();RefreshHistory();
            view.journeyFlow.ShowStage(1);Navigate(0);
            Toast("Finnair DEMO loaded. Tap a disturbance, then inspect the bag.");
        }

        private ICameraService CameraService()
        {
#if UNITY_EDITOR
            if(CameraServiceOverride != null)return CameraServiceOverride;
#endif
            if(cameraService == null)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                cameraService=new AndroidCameraService();
#else
                cameraService=new EditorCameraService();
#endif
            }
            return cameraService;
        }
        private string PhotoContext(string stage) => (simulated ? "demo:" : "real:")+Data.journey.id+":"+stage;
        private string RecoveredStage()
        {
            if(recoveredPhoto == null || recoveredPhoto.pending)return null;
            foreach(string stage in new[]{"before","after"})if(recoveredPhoto.context == PhotoContext(stage))return stage;
            return null;
        }
        private void RefreshPhotoActions()
        {
            bool available=!photoSelecting;
            foreach(var button in new[]{view.beforeCapture,view.afterCapture,view.beforeImport,view.afterImport,view.takeDamagePhoto,view.chooseDamagePhoto})
                if(button)button.interactable=available;
        }
        private void CapturePhoto(string stage)
        {
            if(photoSelecting) {Toast("Finish the open photo action first.");return;}
            if(recoveredPhoto != null) {RefreshRecoveredPhoto();return;}
            SaveJourney();
            StartCoroutine(CapturePhotoRoutine(stage));
        }
        private IEnumerator CapturePhotoRoutine(string stage)
        {
            photoSelecting=true;RefreshPhotoActions();
            string context=PhotoContext(stage);var target=Data;PhotoSelection selection=null;
            try { yield return CameraService().Capture(context,result=>selection=result); }
            finally { photoSelecting=false;RefreshPhotoActions(); }
            if(selection == null || selection.cancelled){Toast("Camera cancelled. Your form and previous photos are retained.");yield break;}
            if(selection.recovered) {yield return RecoverCameraPhoto();yield break;}
            if(!string.IsNullOrEmpty(selection.error)){Toast(selection.error);yield return RecoverCameraPhoto();yield break;}
            if(!ReferenceEquals(target,Data) || selection.context != PhotoContext(stage))
            {recoveredPhoto=selection;RefreshRecoveredPhoto();yield break;}
            if(AttachPhoto(stage,selection)) { CameraService().Release(selection.requestId);Toast("Camera photo saved locally. Exa does not receive your photos."); }
            else {recoveredPhoto=selection;RefreshRecoveredPhoto();}
        }
        private IEnumerator RecoverCameraPhoto() => RecoverCameraPhotoForSession(true);
        private IEnumerator RecoverCameraPhotoForSession(bool showOtherJourneys)
        {
            view.recoveryRetry.interactable=false;
            PhotoSelection selection=null;
            try {yield return CameraService().Recover(result=>selection=result);}
            finally {view.recoveryRetry.interactable=true;}
            if(selection == null){recoveredPhoto=null;RefreshRecoveredPhoto();yield break;}
            if(selection.cancelled){recoveredPhoto=null;RefreshRecoveredPhoto();Toast("Earlier camera session was cancelled. Existing evidence is retained.");yield break;}
            if(!string.IsNullOrEmpty(selection.error) && string.IsNullOrEmpty(selection.requestId)){Toast(selection.error);yield break;}
            if(!string.IsNullOrEmpty(selection.error) && !selection.pending){recoveredPhoto=null;RefreshRecoveredPhoto();Toast(selection.error);yield break;}
            recoveredPhoto=selection;
            if (!showOtherJourneys && RecoveredStage() == null) { recoveredPhoto = null; view.recoveryPanel.SetActive(false); yield break; }
            RefreshRecoveredPhoto();
        }
        private void RefreshRecoveredPhoto()
        {
            if(!view || !view.recoveryPanel)return;
            view.recoveryPanel.SetActive(recoveredPhoto != null);
            if(recoveredPhoto == null){RefreshPhotoActions();return;}
            string stage=RecoveredStage();
            view.recoveryUse.interactable=stage != null && string.IsNullOrEmpty(recoveredPhoto.error) && File.Exists(recoveredPhoto.path);
            view.recoveryStatus.text = recoveredPhoto.pending ? recoveredPhoto.status == "processing" ? "The camera photo is still being prepared. Check again in a moment; it has not been attached." : "An earlier camera session was interrupted. No completed photo was confirmed. Discard it to take another photo."
                : stage == null ? "A photo was retained from another journey or data mode. Switch back to that journey to review it, or discard this pending capture. It has not been attached."
                : "Recovered "+stage+" photo · review it before adding it to this "+(simulated?"DEMO":"real")+" journey. It has not been attached.";
            view.recoveryPhoto.enabled=false;ReleasePhoto(view.recoveryPhoto);view.recoveryPhoto.sprite=null;
            view.recoveryPhoto.gameObject.SetActive(false);
            if(!string.IsNullOrEmpty(recoveredPhoto.previewPath) && File.Exists(recoveredPhoto.previewPath))
                SetPreview(view.recoveryPhoto,recoveredPhoto.previewPath,"recovery-"+recoveredPhoto.requestId);
            if(!recoveredPhoto.pending && !view.recoveryPhoto.enabled)
                view.recoveryStatus.text += "\nPreview unavailable. The original is retained; use it only if you want to keep this capture.";
            if(!string.IsNullOrEmpty(recoveredPhoto.previewWarning))view.recoveryStatus.text += "\n"+recoveredPhoto.previewWarning;
            RefreshPhotoActions();
        }
        private void UseRecoveredPhoto()
        {
            string stage=RecoveredStage();
            if(stage == null || !view.recoveryUse.interactable)return;
            if(AttachPhoto(stage,recoveredPhoto)) {CameraService().Release(recoveredPhoto.requestId);recoveredPhoto=null;RefreshRecoveredPhoto();Toast("Reviewed photo added to this journey.");}
        }
        private void DiscardRecoveredPhoto()
        {
            if(recoveredPhoto == null)return;
            StartCoroutine(DiscardCameraPhoto());
        }
        private IEnumerator DiscardCameraPhoto()
        {
            view.recoveryDiscard.interactable=false;
            try
            {
                CameraService().Release(recoveredPhoto.requestId);
                yield return RecoverCameraPhoto();
                Toast(recoveredPhoto == null ? "Pending capture discarded. Your saved evidence is retained." : "The photo is still being prepared. Check again before discarding it.");
            }
            finally {view.recoveryDiscard.interactable=true;}
        }
        private bool AttachPhoto(string stage,PhotoSelection selection)
        {
            if(!string.IsNullOrEmpty(selection.requestId) && Data.journey.photos.Any(p=>p.sourceRequestId == selection.requestId))return true;
            string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(store.RootDirectory,"Images");
            try
            {
                Directory.CreateDirectory(directory);
                string destination=Path.Combine(directory,id+Path.GetExtension(selection.path));
                File.Copy(selection.path,destination);
                string preview="";
                if(!string.IsNullOrEmpty(selection.previewPath) && File.Exists(selection.previewPath))
                {string previews=Path.Combine(directory,"Previews");Directory.CreateDirectory(previews);preview=Path.Combine(previews,id+".jpg");File.Copy(selection.previewPath,preview);}
                var photo=new PhotoEvidence {id=id,stage=stage,path=destination,previewPath=preview,sourceRequestId=selection.requestId ?? "",origin=string.IsNullOrEmpty(selection.origin)?"local imported image":selection.origin,addedAtUtc=Utc(),captureRequestedAtUtc=selection.captureRequestedAtUtc ?? "",captureReturnedAtUtc=selection.captureReturnedAtUtc ?? ""};
                Data.journey.photos.Add(photo);
                if(!Save()){Data.journey.photos.Remove(photo);return false;}
                RefreshJourney();return true;
            }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {Toast("Could not save the photo. Existing evidence is retained. "+error.Message);return false;}
        }

        private void QueuePhotoPreview(PhotoEvidence photo,Image image,TMPro.TMP_Text label)
        {
            if(loadingPreviews.Contains(image))return;
            ReleasePhoto(image);image.sprite=null;image.enabled=false;
            if(!string.IsNullOrEmpty(photo.previewPath) && File.Exists(photo.previewPath))
            {if(SetPreview(image,photo.previewPath,photo.id))return;photo.previewPath="";}
            StartCoroutine(PreparePhotoPreview(photo,image,label,Data));
        }
        private IEnumerator PreparePhotoPreview(PhotoEvidence photo,Image image,TMPro.TMP_Text label,AppData target)
        {
            loadingPreviews.Add(image);PhotoSelection preview=null;
            yield return PhotoPreview.Prepare(photo.path,result=>preview=result);
            loadingPreviews.Remove(image);
            if(!ReferenceEquals(Data,target) || Data.journey.photos.LastOrDefault(p=>p.stage == photo.stage)?.id != photo.id)
            {ShowPhoto(photo.stage,image,label,photo.stage == "before"?configuration.beforeSample:configuration.afterSample);yield break;}
            if(preview == null || !string.IsNullOrEmpty(preview.error) || !File.Exists(preview.previewPath)) {label.text="Preview unavailable · original retained";yield break;}
            photo.previewPath=preview.previewPath;Save();SetPreview(image,photo.previewPath,photo.id);
        }
        private bool SetPreview(Image image,string path,string id)
        {
            var file=new FileInfo(path);if(!file.Exists || file.Length>4*1024*1024)return false;
            var texture=new Texture2D(2,2);
            if(!texture.LoadImage(File.ReadAllBytes(path)) || texture.width>1536 || texture.height>1536){Destroy(texture);return false;}
            var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Vector2.one*.5f);sprite.name=id;
            image.sprite=sprite;image.gameObject.SetActive(true);image.enabled=true;importedSprites[image]=sprite;return true;
        }
    }
}

using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TravelAgent.Core;
using UnityEditor;
using UnityEngine;

namespace TravelAgent.Editor
{
    public static class EditorVerificationScenarios
    {
        public static void StartUiPreview(string screen)
        {
            var app=UnityEngine.Object.FindAnyObjectByType<AppComposition>();
            if(!EditorApplication.isPlaying || !app)throw new InvalidOperationException("Enter Play before the isolated UI preview.");
            app.StartCoroutine(UiPreview(app,screen));
        }
        private static IEnumerator UiPreview(AppComposition app,string screen)
        {
            AppComposition.TestStorageRoot=Path.GetFullPath("Artifacts/UiPolishPreview/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            app.SetHardware(true);app.Navigate(3);
            app.view.scan.onClick.Invoke();yield return null;
            app.view.discoveredDevices.GetComponentInChildren<ListRowView>().action.onClick.Invoke();
            yield return null;
            var board=(SimulatedBagTransport)app.Transport;
            board.InjectMotion("bump");yield return new WaitForSecondsRealtime(.5f);
            board.InjectMotion("movement");yield return new WaitForSecondsRealtime(.5f);
            board.InjectMotion("impact");
            app.Navigate(1);
            app.view.airline.text="Finnair";app.view.flight.text="AY 123 · DEMO";
            app.view.route.text="Helsinki → Frankfurt";app.view.country.text="Finland";
            app.view.bagDescription.text="Demo suitcase · charcoal with red trim";
            app.view.collectionDate.text="2026-11-14";
            app.view.damageDescription.text="DEMO · split seam noticed at collection.";
            app.view.beforeSample.onClick.Invoke();app.view.afterSample.onClick.Invoke();
            app.view.damageYes.onClick.Invoke();
            if(!app.view.guidanceProvider.text.Contains("FIXTURE"))app.view.toggleGuidance.onClick.Invoke();
            app.view.research.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.3f);
            if(screen=="before" || screen=="inspect" || screen=="report")
            {app.Navigate(1);app.view.journeyFlow.ShowStage(screen=="before"?0:screen=="inspect"?1:2);}
            else if(screen=="events")app.Navigate(2);
            else if(screen=="device")app.Navigate(3);
            else {app.Navigate(0);app.view.guardAction.onClick.Invoke();yield return new WaitForSecondsRealtime(6);}
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/ui-preview-ready.txt",DateTime.UtcNow.ToString("O")+"\n"+screen+"\nIsolated simulated data and offline guidance only.");
        }

        public static void SetResolution(string value)
        {
            string[] parts=value.Split('x');int width=int.Parse(parts[0]),height=int.Parse(parts[1]);
            if(width<320||height<320||width>3840||height>3840)throw new ArgumentOutOfRangeException(nameof(value));
            // Editor-only presentation setting. Fail explicitly if this editor changes its view-size API.
            var assembly=typeof(EditorWindow).Assembly;
            var sizesType=assembly.GetType("UnityEditor.GameViewSizes",true);
            var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var instance=singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
            var groupType=assembly.GetType("UnityEditor.GameViewSizeGroupType",true);
            string groupName=EditorUserBuildSettings.activeBuildTarget==BuildTarget.Android?"Android":"Standalone";
            var group=sizesType.GetMethod("GetGroup").Invoke(instance,new[]{Enum.Parse(groupType,groupName)});
            var groupClass=group.GetType();
            int count=(int)groupClass.GetMethod("GetBuiltinCount").Invoke(group,null)+(int)groupClass.GetMethod("GetCustomCount").Invoke(group,null);
            int selected=-1;
            for(int i=0;i<count;i++)
            {
                var size=groupClass.GetMethod("GetGameViewSize").Invoke(group,new object[]{i});
                if((int)size.GetType().GetProperty("width").GetValue(size)==width&&(int)size.GetType().GetProperty("height").GetValue(size)==height){selected=i;break;}
            }
            if(selected<0)
            {
                var sizeType=assembly.GetType("UnityEditor.GameViewSize",true);
                var kind=assembly.GetType("UnityEditor.GameViewSizeType",true);
                var size=Activator.CreateInstance(sizeType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{Enum.Parse(kind,"FixedResolution"),(object)width,height,"Travel Agent "+value},null);
                groupClass.GetMethod("AddCustomSize").Invoke(group,new[]{size});selected=count;
            }
            var gameType=assembly.GetType("UnityEditor.GameView",true);
            var window=EditorWindow.GetWindow(gameType);
            gameType.GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(window,selected);
            var zoomField=gameType.GetField("m_ZoomArea",BindingFlags.Instance|BindingFlags.NonPublic);
            var zoom=zoomField?.GetValue(window);
            if(zoom!=null)
            {
                float scale=Mathf.Min((window.position.width-24)/width,(window.position.height-55)/height);
                FieldInfo scaleField=null,translationField=null;
                for(var type=zoom.GetType();type!=null;type=type.BaseType)
                {
                    scaleField=scaleField??type.GetField("m_Scale",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                    translationField=translationField??type.GetField("m_Translation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                }
                if(scaleField?.FieldType==typeof(Vector2))scaleField.SetValue(zoom,Vector2.one*Mathf.Clamp(scale,.1f,1f));
                if(translationField?.FieldType==typeof(Vector2))translationField.SetValue(zoom,Vector2.zero);
            }
            window.Repaint();
        }

        public static void StartLiveProbe(string airline)
        {
            if(string.IsNullOrEmpty(airline))airline="Finnair";
            if(airline!="Finnair" && airline!="Lufthansa")throw new ArgumentException("The live verification supports Finnair or Lufthansa.");
            var app=UnityEngine.Object.FindAnyObjectByType<AppComposition>();
            if(!EditorApplication.isPlaying||!app)throw new InvalidOperationException("Enter Play before live verification.");
            app.StartCoroutine(LiveProbe(app,airline));
        }
        private static IEnumerator LiveProbe(AppComposition app,string airline)
        {
            AppComposition.TestStorageRoot=Path.GetFullPath("Artifacts/LiveVerificationData");
            app.SetHardware(true);app.Navigate(3);
            app.view.scan.onClick.Invoke();yield return null;
            app.view.discoveredDevices.GetComponentInChildren<ListRowView>().action.onClick.Invoke();
            app.Navigate(1);app.view.airline.text=airline;app.view.flight.text=(airline=="Finnair"?"AY":"LH")+" 123 (sample)";app.view.route.text="Helsinki to Frankfurt";app.view.country.text="Finland";app.view.collectionDate.text="2026-11-14";
            app.view.bagDescription.text="Sample green suitcase";app.view.damageDescription.text="Sample split seam observed after collection.";
            app.view.beforeSample.onClick.Invoke();app.view.afterSample.onClick.Invoke();app.view.damageYes.onClick.Invoke();
            if(app.view.guidanceProvider.text.Contains("FIXTURE"))app.view.toggleGuidance.onClick.Invoke();
            app.view.research.onClick.Invoke();
            float deadline=Time.realtimeSinceStartup+120;
            while(app.Guidance==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Directory.CreateDirectory("Artifacts");
            if(app.Guidance==null)File.WriteAllText("Artifacts/unity-live-check.txt","FAILED: UI research timed out.");
            else {
                if(app.Data.journey.claimDraftNeedsReview)app.view.regenerateDraft.onClick.Invoke();
                string result=JsonUtility.ToJson(app.Guidance,true)+"\nUI status: "+app.view.guidanceStatus.text+"\nDraft: "+app.view.claimDraft.text;
                File.WriteAllText("Artifacts/unity-live-check.txt",result);
                File.WriteAllText("Artifacts/unity-live-"+airline+".txt",result);
                app.view.export.onClick.Invoke();
                File.WriteAllText("Artifacts/unity-export-path.txt",app.LastExportPath ?? "");
            }
        }
    }
}

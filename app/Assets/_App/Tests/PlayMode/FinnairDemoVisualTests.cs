#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TravelAgent.Integrations.Photos;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TravelAgent.Tests.PlayMode
{
    /// <summary>Board-free rendered acceptance artifacts from the actual saved scene and runtime flow.</summary>
    public sealed class FinnairDemoVisualTests
    {
        private string storage;
        private AppComposition app;
        [UnitySetUp] public IEnumerator SetUp()
        {
            storage=Path.Combine(Path.GetTempPath(),"TravelAgent-Finnair-visual-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storage);AppComposition.TestStorageRoot=storage;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_App/Scenes/App.unity",new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;app=UnityEngine.Object.FindAnyObjectByType<AppComposition>();app.SetHardware(true);
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            var scene=app.gameObject.scene;SceneManager.SetActiveScene(SceneManager.CreateScene("Visual-cleanup-"+Guid.NewGuid().ToString("N")));
            yield return SceneManager.UnloadSceneAsync(scene);app=null;AppComposition.TestStorageRoot=null;
            if(Directory.Exists(storage))Directory.Delete(storage,true);
        }
        [UnityTest] public IEnumerator RenderSavedFinnairStoryAndRecoveryAtPortraitAndLandscape()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Visual artifact generation requires a graphics-enabled Editor test run.");
            app.LoadFinnairSample();app.view.toast.transform.parent.gameObject.SetActive(false);
            yield return null;Capture("finnair-guard-1080x1920",1080,1920);
            app.view.inspectBagContents.onClick.Invoke();app.view.toast.transform.parent.gameObject.SetActive(false);
            yield return null;Capture("finnair-inspect-1080x1920",1080,1920);
            app.view.damageYes.onClick.Invoke();
            if(!app.view.guidanceProvider.text.Contains("FIXTURE"))app.view.toggleGuidance.onClick.Invoke();
            app.view.research.onClick.Invoke();yield return new WaitForSecondsRealtime(.2f);
            app.view.toast.transform.parent.gameObject.SetActive(false);
            Capture("finnair-report-1080x1920",1080,1920);
            Capture("finnair-report-360x800",360,800);
            Capture("finnair-report-1920x1080",1920,1080);
            app.view.journeyFlow.ShowStage(1);app.view.damageNo.onClick.Invoke();
            app.view.toast.transform.parent.gameObject.SetActive(false);yield return null;
            Capture("finnair-no-damage-1080x1920",1080,1920);
            app.view.recoveryPanel.SetActive(true);app.view.recoveryPhoto.enabled=false;
            app.view.recoveryPhoto.gameObject.SetActive(false);
            app.view.recoveryStatus.text="An earlier camera session was interrupted. No completed photo was confirmed. Discard it to take another photo.";
            Capture("finnair-recovery-1080x1920",1080,1920);
            Capture("finnair-recovery-1920x1080",1920,1080);
        }
        private void Capture(string name,int width,int height)
        {
            var canvas=app.gameObject.scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Canvas>(true)).First(c=>c.isRootCanvas);
            var camera=Camera.main;var oldTarget=camera.targetTexture;var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;
            float oldPlane=canvas.planeDistance;var oldActive=RenderTexture.active;
            var output=new RenderTexture(width,height,24);output.Create();var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=output;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                Canvas.ForceUpdateCanvases();
                // Target-texture sizes change synchronously; run the saved shell's normal next-frame width update before rendering.
                foreach(var shell in canvas.GetComponentsInChildren<ResponsiveShell>(true))shell.SendMessage("LateUpdate");
                Canvas.ForceUpdateCanvases();
                foreach(var layout in canvas.GetComponentsInChildren<RectTransform>(true))if(layout.gameObject.activeInHierarchy)LayoutRebuilder.ForceRebuildLayoutImmediate(layout);
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=output;
                foreach(var button in app.view.navigation)
                {
                    var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);
                    foreach(var corner in corners)Assert.That(camera.WorldToScreenPoint(corner).x,Is.InRange(-1f,width+1f),"Saved navigation must fit the target width.");
                }
                texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
                Directory.CreateDirectory("Artifacts/FinnairDemo");File.WriteAllBytes("Artifacts/FinnairDemo/"+name+".png",texture.EncodeToPNG());
                Assert.That(texture.GetPixels32().Select(pixel=>(pixel.r<<16)|(pixel.g<<8)|pixel.b).Distinct().Count(),Is.GreaterThan(30),"The rendered acceptance artifact must contain the saved UI, not an empty target.");
            }
            finally
            {
                camera.targetTexture=oldTarget;canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=oldPlane;
                RenderTexture.active=oldActive;output.Release();UnityEngine.Object.Destroy(output);UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TravelAgent.Core;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TravelAgent.Tests.PlayMode
{
    /// <summary>Saved-interface layout review with explicitly injected measurement fixtures, never a live-device assertion.</summary>
    public sealed class LiveMotionVisualTests
    {
        private AppComposition app;
        private string storage, previousStorage;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            storage = Path.Combine(Path.GetTempPath(), "TravelAgent-live-motion-visual-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storage); previousStorage = AppComposition.TestStorageRoot; AppComposition.TestStorageRoot = storage;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_App/Scenes/App.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            app = UnityEngine.Object.FindAnyObjectByType<AppComposition>(); Assert.That(app, Is.Not.Null);
            app.SetHardware(false); app.Navigate(0); app.view.toast.transform.parent.gameObject.SetActive(false);
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (app)
            {
                Scene scene = app.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("LiveMotion-visual-cleanup-" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            app = null; AppComposition.TestStorageRoot = previousStorage;
            if (!string.IsNullOrEmpty(storage) && Directory.Exists(storage)) Directory.Delete(storage, true);
        }

        [UnityTest, Explicit("Renders three measured-chart fixtures in a graphics-enabled Editor."), Category("LiveMotionVisual")]
        public IEnumerator RenderLiveMotionAtThreeSizes()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            Assert.That(app.view.timeline.yAxisLabels, Has.Length.EqualTo(4), "Saved measured-axis labels must be bound by authoring.");
            var history = new MotionHistory();
            for (int index = 0; index < 150; index++)
            {
                float change = .015f + .035f * Mathf.Abs(Mathf.Sin(index * .37f));
                if (index >= 29 && index <= 32) change = .75f;
                if (index >= 104 && index <= 106) change = .35f;
                history.Add(new MotionSample { protocolVersion = 1, deviceId = "render-bag", bootId = "render-boot", sequence = index + 1,
                    elapsedMs = 100000 + index * 200 + (index >= 75 ? 6000 : 0), deltaG = change, gyroDps = index % 4,
                    sensorFresh = true, sampleAgeMs = 10 });
            }
            app.view.timeline.SetEvents(new[] { Marker(1, 106000), Marker(2, 127000) }, false);
            app.view.timeline.SetMotionHistory(history, true);
            app.view.timeline.titleLabel.text = "Live motion · TEST RENDER";
            app.view.timeline.captionLabel.text = "TEST FIXTURE · measured-chart layout · gaps shown";
            Assert.That(app.view.timeline.scaleLabel.text, Is.EqualTo("Acceleration change · g"));
            Assert.That(app.view.timeline.yAxisLabels[3].text, Is.EqualTo("1"));
            foreach (Vector2Int size in new[] { new Vector2Int(1080, 1920), new Vector2Int(360, 800), new Vector2Int(1920, 1080) })
            {
                yield return null; Capture(size.x, size.y); yield return null;
            }
        }
        private static BagEvent Marker(long sequence, long elapsed) => new BagEvent { deviceId = "render-bag", bootId = "render-boot",
            sequence = sequence, elapsedMs = elapsed, type = "movement", origin = "TEST FIXTURE", quality = "Injected visual acceptance data",
            summary = "TEST RENDER movement flag" };

        private void Capture(int width, int height)
        {
            var canvas = app.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>(true)).First(item => item.isRootCanvas);
            var camera = Camera.main; Assert.That(camera, Is.Not.Null);
            var oldTarget = camera.targetTexture; var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
            float oldPlane = canvas.planeDistance; var oldActive = RenderTexture.active;
            var output = new RenderTexture(width, height, 24); output.Create();
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = output; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Rebuild(canvas); camera.Render(); RenderTexture.active = output;
                foreach (var label in app.view.timeline.yAxisLabels.Concat(new[] { app.view.timeline.titleLabel, app.view.timeline.scaleLabel, app.view.timeline.captionLabel }))
                    AssertFitsWidth(label.rectTransform, camera, width);
                AssertFitsWidth(app.view.timeline.rectTransform, camera, width);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                var distinct = new HashSet<int>(); var colors = pixels.GetPixels32();
                for (int index = 0; index < colors.Length && distinct.Count <= 30; index += 37)
                    distinct.Add((colors[index].r << 16) | (colors[index].g << 8) | colors[index].b);
                Assert.That(distinct.Count, Is.GreaterThan(30), "The target must contain the saved interface and graph.");
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "LiveMotion"));
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "live-motion-test-" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
                RenderTexture.active = oldActive; output.Release(); UnityEngine.Object.Destroy(output); UnityEngine.Object.Destroy(pixels); Rebuild(canvas);
            }
        }
        private static void Rebuild(Canvas canvas)
        {
            MethodInfo handle = typeof(CanvasScaler).GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handle, Is.Not.Null);
            foreach (var scaler in canvas.GetComponentsInChildren<CanvasScaler>(true)) handle.Invoke(scaler, null);
            Canvas.ForceUpdateCanvases(); foreach (var shell in canvas.GetComponentsInChildren<ResponsiveShell>(true)) shell.SendMessage("LateUpdate");
            Canvas.ForceUpdateCanvases();
            foreach (var rect in canvas.GetComponentsInChildren<RectTransform>(true)) if (rect.gameObject.activeInHierarchy) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            Canvas.ForceUpdateCanvases();
        }
        private static void AssertFitsWidth(RectTransform rect, Camera camera, int width)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            foreach (var corner in corners) Assert.That(camera.WorldToScreenPoint(corner).x, Is.InRange(-2f, width + 2f), rect.name + " extends outside target width.");
        }
    }
}
#endif

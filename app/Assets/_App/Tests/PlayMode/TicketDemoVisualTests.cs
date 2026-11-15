#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TravelAgent.Integrations.Import;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TravelAgent.Tests.PlayMode
{
    /// <summary>Explicit, offscreen saved-UI review. Overlay responses are labelled test presentations, not network results.</summary>
    public sealed class TicketDemoVisualTests
    {
        private const string ScenePath = "Assets/_App/Scenes/App.unity";
        private AppComposition app;
        private string storage, previousStorage;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            storage = Path.Combine(Path.GetTempPath(), "TravelAgent-ticket-visual-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storage);
            previousStorage = AppComposition.TestStorageRoot;
            AppComposition.TestStorageRoot = storage;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            app = UnityEngine.Object.FindAnyObjectByType<AppComposition>();
            Assert.That(app, Is.Not.Null);
            app.SetHardware(true);
            app.Navigate(1);
            app.view.journeyFlow.ShowStage(0);
            app.view.toast.transform.parent.gameObject.SetActive(false);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                Scene empty = SceneManager.CreateScene("Ticket-visual-cleanup-" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            app = null;
            AppComposition.TestStorageRoot = previousStorage;
            // Only this fixture's exact, newly created temporary store is removed.
            if (!string.IsNullOrEmpty(storage) && Directory.Exists(storage)) Directory.Delete(storage, true);
        }

        [UnityTest, Explicit("Writes fifteen rendered acceptance artifacts; run this exact test in a graphics-enabled Editor."), Category("TicketVisual")]
        public IEnumerator RenderTicketFirstScreensAtThreeSizes()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null),
                "Run visual review with graphics enabled; a null-graphics pass cannot establish layout acceptance.");
            Assert.That(app.Data.journey.airline, Is.Empty, "The saved Before screen cannot prefill Finnair.");
            Assert.That(app.view.airline.text, Is.Empty);
            Assert.That(app.Data.journey.photos, Is.Empty);
            Assert.That(app.Guidance, Is.Null);
            yield return CaptureSizes("ticket-before-empty");

            // A Gmail handoff immediately shows locally read details. No Exa lookup is part of Before.
            app.IncomingShareServiceOverride = new VisualIncoming();
            typeof(AppComposition).GetField("nextSharePoll", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, 0f);
            typeof(AppComposition).GetMethod("PollIncomingTicket", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, null);
            Assert.That(app.Data.journey.airline, Is.EqualTo("Finnair"));
            Assert.That(app.Data.journey.route, Is.EqualTo("CPH → HEL"));
            Assert.That(app.Data.journey.airlineConfirmationSource, Is.EqualTo("shared-text-local"));
            Assert.That(app.view.exaSpotlight.gameObject.activeSelf, Is.False);
            Assert.That(app.view.journeyFlow.nextButton.interactable, Is.True);
            yield return CaptureSizes("ticket-before-imported");

            var spotlight = app.view.exaSpotlight;
            spotlight.Begin("EXA RESEARCH", "TEST RENDER · waiting for injected reporting requirements.");
            Assert.That(spotlight.IsPending, Is.True);
            Assert.That(spotlight.continueButton.gameObject.activeSelf, Is.False);
            yield return CaptureSizes("ticket-exa-pending-test");

            spotlight.Complete("Reporting steps ready", "TEST RENDER · injected reporting requirements.\nPhotos of the damaged bag are required.", "TEST FIXTURE · finnair.com");
            Assert.That(spotlight.IsPending, Is.False);
            Assert.That(spotlight.continueButton.gameObject.activeSelf, Is.True);
            Assert.That(app.Guidance, Is.Null, "A presentation-only result cannot create real reporting guidance.");
            Assert.That(spotlight.titleLabel.richText || spotlight.detailLabel.richText || spotlight.sourceLabel.richText, Is.False);
            yield return CaptureSizes("ticket-exa-reveal-test");
            spotlight.continueButton.onClick.Invoke();
            Assert.That(spotlight.gameObject.activeSelf, Is.False);

            // Exercise the saved inspection controls after local ticket import, without a camera or network call.
            app.view.journeyFlow.ShowStage(1);
            app.view.damageYes.onClick.Invoke();
            Assert.That(app.Data.journey.airlineConfirmedValue, Is.EqualTo("Finnair"));
            Assert.That(app.view.journeyFlow.SelectedStage, Is.EqualTo(2));
            Assert.That(app.view.claimPanel.activeInHierarchy, Is.True);
            Assert.That(app.Data.journey.photos, Is.Empty, "Report must not create sample photos.");
            Assert.That(app.Guidance, Is.Null, "This visual test never starts policy research.");
            Assert.That(app.view.beforePhoto.enabled || app.view.afterPhoto.enabled, Is.False);
            yield return CaptureSizes("ticket-report-no-photos");
        }

        private sealed class VisualIncoming : IIncomingShareService
        {
            private IncomingShare pending = new IncomingShare { requestId = Guid.NewGuid().ToString("N"),
                text = "Your Finnair boarding pass\nYour flight: Copenhagen (CPH)–Helsinki (HEL)",
                origin = "TEST FIXTURE · shared text", receivedAtUtc = DateTime.UtcNow.ToString("O") };
            public IncomingShare Peek() => pending;
            public bool Acknowledge(string id)
            {
                if (pending == null || pending.requestId != id) return false;
                pending = null; return true;
            }
        }

        private IEnumerator CaptureSizes(string state)
        {
            foreach (Vector2Int size in new[] { new Vector2Int(1080, 1920), new Vector2Int(360, 800), new Vector2Int(1920, 1080) })
            {
                yield return null;
                Capture(state + "-" + size.x + "x" + size.y, size.x, size.y);
                yield return null; // Release destroyed render resources before the next target size.
            }
        }

        private void Capture(string name, int width, int height)
        {
            var canvas = app.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>(true)).First(item => item.isRootCanvas);
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var oldTarget = camera.targetTexture;
            var oldMode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            float oldPlane = canvas.planeDistance;
            var oldActive = RenderTexture.active;
            var output = new RenderTexture(width, height, 24);
            output.Create();
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = output;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                RebuildForTarget(canvas);
                camera.Render();
                RenderTexture.active = output;

                foreach (var button in app.view.navigation) AssertFitsWidth((RectTransform)button.transform, camera, width);
                foreach (var button in app.view.selector.screens[app.view.selector.selectedScreen].GetComponentsInChildren<Button>())
                    AssertFitsWidth((RectTransform)button.transform, camera, width);
                if (app.view.exaSpotlight.gameObject.activeInHierarchy)
                {
                    foreach (var label in new[] { app.view.exaSpotlight.titleLabel, app.view.exaSpotlight.detailLabel, app.view.exaSpotlight.sourceLabel })
                        if (label.gameObject.activeInHierarchy) AssertFitsWidth(label.rectTransform, camera, width);
                    if (app.view.exaSpotlight.continueButton.gameObject.activeInHierarchy)
                        AssertFitsWidth((RectTransform)app.view.exaSpotlight.continueButton.transform, camera, width);
                }
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                var distinct = new HashSet<int>();
                Color32[] pixels = image.GetPixels32();
                for (int index = 0; index < pixels.Length && distinct.Count <= 30; index += 37)
                    distinct.Add((pixels[index].r << 16) | (pixels[index].g << 8) | pixels[index].b);
                Assert.That(distinct.Count, Is.GreaterThan(30), "The capture must contain saved text/controls, not an empty render target.");
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "TicketDemo"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                canvas.renderMode = oldMode;
                canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldPlane;
                RenderTexture.active = oldActive;
                output.Release();
                UnityEngine.Object.Destroy(output);
                UnityEngine.Object.Destroy(image);
                RebuildForTarget(canvas);
            }
        }

        private static void RebuildForTarget(Canvas canvas)
        {
            // This uGUI version scales in preWillRenderCanvases, rather than an Update receiver.
            // Invoke the actual scaler handler so a synchronous target-size change is reflected immediately.
            MethodInfo handle = typeof(CanvasScaler).GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handle, Is.Not.Null, "The installed CanvasScaler handler must exist for offscreen layout review.");
            foreach (var scaler in canvas.GetComponentsInChildren<CanvasScaler>(true)) handle.Invoke(scaler, null);
            Canvas.ForceUpdateCanvases();
            foreach (var shell in canvas.GetComponentsInChildren<ResponsiveShell>(true)) shell.SendMessage("LateUpdate");
            Canvas.ForceUpdateCanvases();
            foreach (var rect in canvas.GetComponentsInChildren<RectTransform>(true))
                if (rect.gameObject.activeInHierarchy) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            Canvas.ForceUpdateCanvases();
        }

        private static void AssertFitsWidth(RectTransform rect, Camera camera, int width)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            foreach (var corner in corners)
                Assert.That(camera.WorldToScreenPoint(corner).x, Is.InRange(-2f, width + 2f), rect.name + " extends outside the target width.");
        }
    }
}
#endif

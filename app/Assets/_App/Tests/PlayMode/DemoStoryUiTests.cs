#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TravelAgent.Tests.PlayMode
{
    /// <summary>Saved story controls and stage navigation, without camera, network or hardware actions.</summary>
    public sealed class DemoStoryUiTests
    {
        private const string AppScenePath = "Assets/_App/Scenes/App.unity";
        private AppComposition app;
        private string storageRoot;
        private FieldInfo storageField;
        private object previousStorageRoot;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            storageField = typeof(AppComposition).GetField("TestStorageRoot", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(storageField, Is.Not.Null, "The saved UI must run against an isolated test store.");
            storageRoot = Path.Combine(Path.GetTempPath(), "TravelAgent-story-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storageRoot);
            previousStorageRoot = storageField.GetValue(null);
            storageField.SetValue(null, storageRoot);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(AppScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            app = UnityEngine.Object.FindAnyObjectByType<AppComposition>();
            Assert.That(app, Is.Not.Null);
            app.SetHardware(true);
            yield return null;
            // These saved-layout/inspection tests assume a reviewed manual fixture airline.
            app.Navigate(1);
            app.view.journeyFlow.ShowStage(0);
            app.view.airline.text = "Finnair";
            Assert.That(app.view.confirmAirline.interactable, Is.True);
            app.view.confirmAirline.onClick.Invoke();
            Assert.That(app.Data.journey.airlineConfirmedValue, Is.EqualTo("Finnair"));
            Assert.That(app.Data.journey.airlineConfirmedAtUtc, Is.Not.Empty);
            app.Navigate(0);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene scene = SceneManager.GetSceneByPath(AppScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                Scene empty = SceneManager.CreateScene("TravelAgent-story-ui-cleanup-" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            app = null;
            if (storageField != null) storageField.SetValue(null, previousStorageRoot);
            // Only the exact temporary directory created by this fixture is removed.
            if (!string.IsNullOrEmpty(storageRoot) && Directory.Exists(storageRoot)) Directory.Delete(storageRoot, true);
        }

        [UnityTest]
        public IEnumerator SavedInspectionAndReportingActionsRemainFirstClassWithExplicitRecoveryChoices()
        {
            var view = app.view;
            Assert.That(view.inspectionPromptPanel, Is.Not.Null);
            AssertWithin(view.inspectionStatus, view.inspectionPromptPanel.transform);
            AssertWithin(view.inspectBagContents, view.inspectionPromptPanel.transform);
            AssertWithin(view.detailInspect, view.detailPanel.transform);
            AssertWithin(view.beforeCapture, view.journeyFlow.stages[0].transform);
            AssertWithin(view.afterCapture, view.journeyFlow.stages[1].transform);
            AssertWithin(view.airline, view.journeyFlow.stages[0].transform);
            Assert.That(view.airline.transform.IsChildOf(view.claimPanel.transform), Is.False,
                "Required airline confirmation belongs in Before, before Report is accessible.");
            Assert.That(view.damageYes.transform.parent.parent, Is.EqualTo(view.afterPhoto.transform.parent.parent));
            Assert.That(view.damageYes.transform.parent.GetSiblingIndex(), Is.LessThan(view.afterPhoto.transform.parent.GetSiblingIndex()),
                "The inspection decision must precede optional photo evidence.");
            AssertWithin(view.takeDamagePhoto, view.claimPanel.transform);
            AssertWithin(view.chooseDamagePhoto, view.claimPanel.transform);
            AssertWithin(view.reportPhotoStatus, view.claimPanel.transform);
            AssertWithin(view.reportingSummary, view.claimPanel.transform);
            foreach (Component primaryAction in new Component[] { view.takeDamagePhoto, view.chooseDamagePhoto, view.openForm, view.reportingSummary })
                Assert.That(primaryAction.GetComponentsInParent<DemoDisclosure>(true), Is.Empty,
                    primaryAction.name + " must be reachable without expanding optional draft/settings sections.");

            var packetDisclosure = view.export.GetComponentsInParent<DemoDisclosure>(true)
                .FirstOrDefault(section => section.content && view.export.transform.IsChildOf(section.content.transform));
            Assert.That(packetDisclosure, Is.Not.Null, "The optional evidence packet belongs in an explicit disclosure.");
            Assert.That(packetDisclosure.expanded, Is.False);
            AssertWithin(view.loadFinnairSample, view.demoPanel.transform);
            Assert.That(view.loadFinnairSample.GetComponentInChildren<TMPro.TMP_Text>(true).text, Is.EqualTo("New demo"));

            Assert.That(view.recoveryPanel, Is.Not.Null);
            foreach (Component choice in new Component[] { view.recoveryPhoto, view.recoveryStatus, view.recoveryUse, view.recoveryRetry, view.recoveryLater, view.recoveryDiscard })
                AssertWithin(choice, view.recoveryPanel.transform);
            Assert.That(view.recoveryPanel.activeSelf, Is.False, "A fresh journey has no pending recovery to attach.");
            Assert.That(view.recoveryUse.interactable, Is.False, "Use requires a validated pending capture context.");
            Assert.That(view.recoveryPhoto.color, Is.EqualTo(Color.white), "Repeated authoring must not tint the recovered evidence preview.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator NoDamageEndsInspectNavigationUntilTravelerChangesTheOutcome()
        {
            app.Navigate(1);
            var flow = app.view.journeyFlow;
            flow.SetInspectionOutcome(null);
            flow.ShowStage(2);
            Assert.That(flow.SelectedStage, Is.EqualTo(1), "An unconfirmed inspection cannot enter Report.");
            Assert.That(flow.nextButton.gameObject.activeSelf, Is.False);
            Assert.That(flow.stageButtons[2].interactable, Is.False);
            flow.nextButton.onClick.Invoke();
            Assert.That(flow.SelectedStage, Is.EqualTo(1), "Invoking a hidden Next action cannot bypass confirmation.");
            flow.SetInspectionOutcome(true);
            flow.ShowStage(2);
            Assert.That(flow.SelectedStage, Is.EqualTo(2));

            flow.SetInspectionOutcome(false);
            Assert.That(flow.SelectedStage, Is.EqualTo(1), "A no-damage outcome returns an open Report stage to Inspect.");
            Assert.That(flow.nextButton.gameObject.activeSelf, Is.False, "No Prepare report action should follow a no-damage inspection.");
            Assert.That(flow.stageButtons[2].interactable, Is.False);
            flow.ShowStage(2);
            Assert.That(flow.SelectedStage, Is.EqualTo(1), "Programmatic navigation must respect the same no-damage boundary.");
            flow.stageButtons[0].onClick.Invoke();
            flow.stageButtons[1].onClick.Invoke();
            Assert.That(flow.nextButton.gameObject.activeSelf, Is.False, "Changing stage must not forget the inspection outcome.");
            Assert.That(flow.stages[1].activeSelf, Is.True);
            Assert.That(flow.stages[2].activeSelf, Is.False);

            flow.SetInspectionOutcome(true);
            Assert.That(flow.nextButton.gameObject.activeSelf, Is.True);
            Assert.That(flow.stageButtons[2].interactable, Is.True);
            flow.nextButton.onClick.Invoke();
            Assert.That(flow.SelectedStage, Is.EqualTo(2), "An explicitly changed damage outcome restores report navigation.");
            yield return null;
        }

        private static void AssertWithin(Component component, Transform parent)
        {
            Assert.That(component, Is.Not.Null);
            Assert.That(component.transform.IsChildOf(parent), Is.True, component.name + " is outside its saved story section.");
        }
    }
}
#endif

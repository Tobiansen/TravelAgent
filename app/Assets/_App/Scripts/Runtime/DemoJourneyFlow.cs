using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    /// <summary>Three saved stages, with explicit presenter/user navigation and no timer.</summary>
    public sealed class DemoJourneyFlow : MonoBehaviour
    {
        public GameObject[] stages = new GameObject[3];
        public Button[] stageButtons = new Button[3];
        public Button backButton, nextButton;
        public TMP_Text nextLabel;
        public ScrollRect scroll;
        public GameObject reportContent, reportHint;
        public Color selectedColor = new Color32(255, 51, 71, 255);
        public Color otherColor = new Color32(40, 43, 50, 255);
        [SerializeField, Range(0, 2)] private int selectedStage;
        public int SelectedStage { get { return selectedStage; } }
        public event Action<int> stageChanged;
        public bool? InspectionOutcome { get { return inspectionOutcome; } }
        private UnityEngine.Events.UnityAction[] stageActions;
        private bool lastReportVisible;
        private bool? inspectionOutcome;
        private bool airlineConfirmed=true;

        private void OnEnable()
        {
            stageActions = new UnityEngine.Events.UnityAction[stageButtons.Length];
            for (int i = 0; i < stageButtons.Length; i++)
            {
                int stage = i; stageActions[i] = () => ShowStage(stage);
                if (stageButtons[i]) stageButtons[i].onClick.AddListener(stageActions[i]);
            }
            if (backButton) backButton.onClick.AddListener(Back);
            if (nextButton) nextButton.onClick.AddListener(Next);
            ShowStage(selectedStage);
        }
        private void OnDisable()
        {
            for (int i = 0; stageActions != null && i < stageActions.Length; i++) if (stageButtons[i]) stageButtons[i].onClick.RemoveListener(stageActions[i]);
            if (backButton) backButton.onClick.RemoveListener(Back);
            if (nextButton) nextButton.onClick.RemoveListener(Next);
        }
        private void LateUpdate()
        {
            bool visible = reportContent && reportContent.activeSelf;
            if (lastReportVisible != visible) RefreshReportHint();
        }
        public void ShowStage(int stage)
        {
            int next = Mathf.Clamp(stage, 0, Mathf.Max(0, stages.Length - 1));
            if(Application.isPlaying && !airlineConfirmed && next>0)next=0;
            if (Application.isPlaying && inspectionOutcome != true && next == 2) next = 1;
            bool changed = next != selectedStage; selectedStage = next;
            for (int i = 0; i < stages.Length; i++) if (stages[i]) stages[i].SetActive(i == next);
            for (int i = 0; i < stageButtons.Length; i++)
                if (stageButtons[i] && stageButtons[i].targetGraphic) stageButtons[i].targetGraphic.color = i == next ? selectedColor : otherColor;
            RefreshNavigation();
            RefreshReportHint();
            if (scroll) { scroll.StopMovement(); Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1; }
            if (changed) stageChanged?.Invoke(next);
        }

        /// <summary>Runtime inspection facts determine whether there is a report path; navigation never infers damage.</summary>
        public void SetInspectionOutcome(bool? damageFound)
        {
            inspectionOutcome = damageFound;
            if (Application.isPlaying && damageFound != true && selectedStage == 2) { ShowStage(1); return; }
            RefreshNavigation();
            RefreshReportHint();
        }
        public void SetAirlineConfirmed(bool confirmed)
        {
            airlineConfirmed=confirmed;
            if(Application.isPlaying && !confirmed && selectedStage>0){ShowStage(0);return;}
            RefreshNavigation();
        }

        private void RefreshNavigation()
        {
            if (backButton){backButton.interactable=selectedStage>0;backButton.gameObject.SetActive(selectedStage>0);}
            bool canReport = !Application.isPlaying || inspectionOutcome == true;
            if (nextButton) nextButton.gameObject.SetActive(selectedStage < stages.Length - 1 && (selectedStage != 1 || canReport) && (selectedStage!=0 || !Application.isPlaying || airlineConfirmed));
            if(nextButton)nextButton.interactable=selectedStage!=0 || !Application.isPlaying || airlineConfirmed;
            if(stageButtons.Length>1 && stageButtons[1])stageButtons[1].interactable=!Application.isPlaying || airlineConfirmed;
            if (nextLabel) nextLabel.text = selectedStage == 0 ? "Inspect bag and contents" : "Prepare damage report";
            if (stageButtons.Length > 2 && stageButtons[2]) stageButtons[2].interactable = canReport;
        }
        private void RefreshReportHint() { lastReportVisible = reportContent && reportContent.activeSelf; if (reportHint) reportHint.SetActive(!lastReportVisible); }
        private void Back() { ShowStage(selectedStage - 1); }
        private void Next() { ShowStage(selectedStage + 1); }
        [ContextMenu("Preview/Before")] private void PreviewBefore() { Preview(0); }
        [ContextMenu("Preview/Inspect")] private void PreviewInspect() { Preview(1); }
        [ContextMenu("Preview/Report")] private void PreviewReport() { Preview(2); }
        private void Preview(int stage)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Preview journey stage");
#endif
            ShowStage(stage);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }
    }
}

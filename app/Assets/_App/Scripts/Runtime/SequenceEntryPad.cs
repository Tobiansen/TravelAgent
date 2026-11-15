using System;
using TMPro;
using TravelAgent.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    /// <summary>Saved A/B settings-entry view. Save/authorization/acknowledgement remain in AppComposition.</summary>
    public sealed class SequenceEntryPad : MonoBehaviour
    {
        [Header("Saved virtual controls")]
        public Button aButton;
        public Button bButton;
        public Button undoButton;
        public Button restartButton;
        public TMP_Text instruction;
        public TMP_Text progress;
        public TMP_Text summary;

        [Header("Hidden validation targets — never keyboard entry")]
        public TMP_InputField armTarget;
        public TMP_InputField disarmTarget;
        public TMP_InputField confirmArmTarget;
        public TMP_InputField confirmDisarmTarget;

        public event Action EntryChanged;
        public bool IsComplete => capturing && entry.IsComplete;
        public bool HasEntry => capturing && entry.CanUndo;
        public string Status => InstructionText();

        private readonly SequenceEntryModel entry = new SequenceEntryModel();
        private bool capturing;
        private bool inputEnabled = true;
        private bool listenersBound;
        private string confirmedArm = "";
        private string confirmedDisarm = "";

        private void Awake() { BindButtons(); PrepareTargets(); Render(); }
        private void OnEnable() { BindButtons(); Render(); }
        private void OnDisable() { UnbindButtons(); }
        private void OnDestroy() { UnbindButtons(); }

        /// <summary>Order matches AppView's existing fields; references remain inspectable in the saved scene.</summary>
        public void BindTargets(TMP_InputField arm, TMP_InputField disarm,
            TMP_InputField repeatArm, TMP_InputField repeatDisarm)
        {
            armTarget = arm;
            disarmTarget = disarm;
            confirmArmTarget = repeatArm;
            confirmDisarmTarget = repeatDisarm;
            PrepareTargets();
            WriteTargets();
            Render();
        }

        /// <summary>Call only for a new provider or confirmed configuration. No command is sent.</summary>
        public void SetConfirmed(string arm, string disarm)
        {
            if (SequenceRules.Validate(arm, disarm, out _))
            {
                confirmedArm = arm;
                confirmedDisarm = disarm;
            }
            else { confirmedArm = ""; confirmedDisarm = ""; }
            ResetEntry();
        }

        /// <summary>Return to the confirmed baseline; the first new tap replaces all four hidden entries.</summary>
        public void ResetEntry()
        {
            entry.Reset();
            capturing = false;
            WriteTargets();
            Render();
            EntryChanged?.Invoke();
        }

        /// <summary>Locking for pending/disconnected/armed states preserves every captured tap.</summary>
        public void SetInteractable(bool interactable)
        {
            inputEnabled = interactable;
            Render();
        }

        public void TapA() => Enter('A');
        public void TapB() => Enter('B');

        public void Undo()
        {
            if (!inputEnabled || !capturing || !entry.Undo()) return;
            Changed();
        }

        private void Restart()
        {
            if (inputEnabled) ResetEntry();
        }

        private void Enter(char symbol)
        {
            if (!inputEnabled || entry.IsComplete) return;
            if (!capturing) { entry.Reset(); capturing = true; }
            entry.Tap(symbol);
            Changed();
        }

        private void Changed()
        {
            WriteTargets();
            Render();
            EntryChanged?.Invoke();
        }

        private void PrepareTargets()
        {
            foreach (var target in new[] { armTarget, disarmTarget, confirmArmTarget, confirmDisarmTarget })
            {
                if (!target) continue;
                target.readOnly = true;
                target.interactable = false;
            }
        }

        private void WriteTargets()
        {
            if (armTarget) armTarget.SetTextWithoutNotify(capturing ? entry.ArmSequence : confirmedArm);
            if (disarmTarget) disarmTarget.SetTextWithoutNotify(capturing ? entry.DisarmSequence : confirmedDisarm);
            if (confirmArmTarget) confirmArmTarget.SetTextWithoutNotify(capturing ? entry.RepeatArmSequence : "");
            if (confirmDisarmTarget) confirmDisarmTarget.SetTextWithoutNotify(capturing ? entry.RepeatDisarmSequence : "");
        }

        private void Render()
        {
            if (aButton) aButton.interactable = inputEnabled && !entry.IsComplete;
            if (bButton) bButton.interactable = inputEnabled && !entry.IsComplete;
            if (undoButton) undoButton.interactable = inputEnabled && HasEntry;
            if (restartButton) restartButton.interactable = inputEnabled && capturing;
            if (instruction) instruction.text = InstructionText();
            if (progress)
            {
                int count = capturing ? entry.Progress : 0;
                progress.text = string.Join("  ", new[] {
                    count > 0 ? "●" : "○", count > 1 ? "●" : "○",
                    count > 2 ? "●" : "○", count > 3 ? "●" : "○"
                });
            }
            if (summary)
            {
                if (!HasEntry) summary.text = inputEnabled ? "Tap A or B to begin" : "";
                else
                {
                    string proposed = "New: arm " + Preview(entry.ArmSequence) + "  ·  disarm " + Preview(entry.DisarmSequence);
                    string repeat = entry.Phase == SequenceEntryPhase.RepeatArm ? "Repeat arm: " + Preview(entry.RepeatArmSequence)
                        : entry.Phase == SequenceEntryPhase.RepeatDisarm ? "Repeat disarm: " + Preview(entry.RepeatDisarmSequence)
                        : "";
                    summary.text = proposed + (string.IsNullOrEmpty(repeat) ? "" : "\n" + repeat);
                }
            }
        }

        private string InstructionText()
        {
            if (!inputEnabled) return HasEntry ? "Paused · entry kept" : "Connect and disarm to edit";
            if (entry.IsComplete) return "Ready · save both sequences below";
            string stage = ((int)entry.Phase + 1) + "/4 · ";
            if (!string.IsNullOrEmpty(entry.Message)) return stage + entry.Message;
            switch (entry.Phase)
            {
                case SequenceEntryPhase.Arm: return stage + "Enter the new arm sequence";
                case SequenceEntryPhase.RepeatArm: return stage + "Repeat the same arm sequence";
                case SequenceEntryPhase.Disarm: return stage + "Enter a different disarm sequence";
                default: return stage + "Repeat the same disarm sequence";
            }
        }

        private static string Preview(string value) => (value ?? "").PadRight(4, '○');

        private void BindButtons()
        {
            if (listenersBound) return;
            if (aButton) aButton.onClick.AddListener(TapA);
            if (bButton) bButton.onClick.AddListener(TapB);
            if (undoButton) undoButton.onClick.AddListener(Undo);
            if (restartButton) restartButton.onClick.AddListener(Restart);
            listenersBound = true;
        }

        private void UnbindButtons()
        {
            if (!listenersBound) return;
            if (aButton) aButton.onClick.RemoveListener(TapA);
            if (bButton) bButton.onClick.RemoveListener(TapB);
            if (undoButton) undoButton.onClick.RemoveListener(Undo);
            if (restartButton) restartButton.onClick.RemoveListener(Restart);
            listenersBound = false;
        }
    }
}

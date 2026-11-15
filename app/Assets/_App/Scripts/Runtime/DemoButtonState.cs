using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    /// <summary>Text remains legible when controller acknowledgements disable an authored button.</summary>
    public sealed class DemoButtonState : MonoBehaviour
    {
        public Button button;
        public TMP_Text label;
        public Color normalText = new Color32(244, 242, 241, 255);
        public Color disabledText = new Color32(133, 134, 144, 255);
        private bool previous;
        private bool initialized;
        private void OnEnable() { initialized = false; Refresh(); }
        private void LateUpdate() { Refresh(); }
        private void Refresh()
        {
            if (!button || !label) return;
            bool state = button.IsInteractable();
            if (!initialized || state != previous) { label.color = state ? normalText : disabledText; previous = state; initialized = true; }
        }
    }
}

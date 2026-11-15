using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    /// <summary>Saved optional controls remain inspectable; runtime only changes visibility.</summary>
    public sealed class DemoDisclosure : MonoBehaviour
    {
        public Button toggle;
        public TMP_Text label;
        public GameObject content;
        public string title = "Details";
        public bool expanded;
        private void OnEnable() { if (toggle) toggle.onClick.AddListener(Toggle); SetExpanded(expanded); }
        private void OnDisable() { if (toggle) toggle.onClick.RemoveListener(Toggle); }
        public void Toggle() { SetExpanded(!expanded); }
        public void SetExpanded(bool value)
        {
            expanded = value;
            if (content) content.SetActive(value);
            if (label) label.text = title + (value ? "  -" : "  +");
        }
    }
}

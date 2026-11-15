using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    public sealed class ListRowView : MonoBehaviour
    {
        public TMP_Text title;
        public TMP_Text detail;
        public Button action;
        public void Set(string heading, string body, System.Action clicked = null)
        {
            title.text = heading;
            detail.text = body;
            action.interactable = clicked != null;
            action.onClick.RemoveAllListeners();
            if (clicked != null) action.onClick.AddListener(() => clicked());
        }
    }
}

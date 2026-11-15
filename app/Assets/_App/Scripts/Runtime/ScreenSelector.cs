using UnityEngine;

namespace TravelAgent
{
    public sealed class ScreenSelector : MonoBehaviour
    {
        [Tooltip("Saved screens remain editable before Play. Select through the context menu or Tools menu.")]
        public GameObject[] screens;
        [Range(0, 3)] public int selectedScreen = 3;
        public void Show(int index)
        {
            selectedScreen = Mathf.Clamp(index, 0, screens.Length - 1);
            for (var i = 0; i < screens.Length; i++) if (screens[i]) screens[i].SetActive(i == selectedScreen);
        }
        [ContextMenu("Preview Guard")] private void Guard() => Show(0);
        [ContextMenu("Preview Journey")] private void Journey() => Show(1);
        [ContextMenu("Preview History")] private void History() => Show(2);
        [ContextMenu("Preview Device")] private void Device() => Show(3);
    }
}

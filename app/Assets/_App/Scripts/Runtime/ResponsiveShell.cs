using UnityEngine;

namespace TravelAgent
{
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ResponsiveShell : MonoBehaviour
    {
        public float maximumWidth = 1120;
        private void LateUpdate()
        {
            var rect = (RectTransform)transform;
            if (!(rect.parent is RectTransform parent)) return;
            rect.anchorMin = new Vector2(.5f, 0); rect.anchorMax = new Vector2(.5f, 1);
            rect.pivot = new Vector2(.5f, .5f);
            var size = new Vector2(Mathf.Min(maximumWidth, parent.rect.width), 0);
            if (rect.sizeDelta != size) rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }
    }
}

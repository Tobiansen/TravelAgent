using UnityEngine;

namespace TravelAgent
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        private Rect previous;
        private Vector2Int previousSize;
        private void Update()
        {
            var safe = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (safe == previous && size == previousSize) return;
            previous = safe;
            previousSize = size;
            if (size.x == 0 || size.y == 0) return;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / size.x, safe.yMin / size.y);
            rect.anchorMax = new Vector2(safe.xMax / size.x, safe.yMax / size.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

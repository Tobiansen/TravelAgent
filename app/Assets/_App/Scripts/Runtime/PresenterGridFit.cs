using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    /// <summary>Fits the existing saved presenter controls to their available panel width.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(GridLayoutGroup), typeof(LayoutElement))]
    public sealed class PresenterGridFit : MonoBehaviour
    {
        public GridLayoutGroup grid;
        public LayoutElement panel;
        [Min(1)] public int portraitColumns = 3;
        [Min(1)] public int landscapeColumns = 6;
        [Min(1)] public float minimumCellWidth = 160;
        [Min(1)] public float cellHeight = 80;

        private void OnEnable() { RefreshLayout(); }
        private void LateUpdate() { RefreshLayout(); }

        public void RefreshLayout()
        {
            if (!grid) grid = GetComponent<GridLayoutGroup>();
            if (!panel) panel = GetComponent<LayoutElement>();
            var rect = (RectTransform)transform;
            if (!grid || !panel || rect.rect.width <= 0) return;

            var canvas = GetComponentInParent<Canvas>();
            Rect viewport = canvas ? ((RectTransform)canvas.rootCanvas.transform).rect : new Rect(0, 0, Screen.width, Screen.height);
            bool landscape = viewport.width > viewport.height;
            int maximumColumns = Mathf.Clamp(landscape ? landscapeColumns : portraitColumns, 1, landscape ? 6 : 3);
            float available = Mathf.Max(1, rect.rect.width - grid.padding.horizontal);
            float gap = Mathf.Max(0, grid.spacing.x);
            int columns = Mathf.Clamp(Mathf.FloorToInt((available + gap) / (Mathf.Max(1, minimumCellWidth) + gap)), 1, maximumColumns);
            int count = 0;
            foreach (Transform child in transform)
            {
                var element = child.GetComponent<LayoutElement>();
                if (child.gameObject.activeSelf && (!element || !element.ignoreLayout)) count++;
            }
            columns = Mathf.Min(columns, Mathf.Max(1, count));
            float width = Mathf.Max(1, (available - gap * (columns - 1)) / columns);
            float height = Mathf.Max(1, cellHeight);
            int rows = Mathf.CeilToInt(count / (float)columns);
            float panelHeight = grid.padding.vertical + rows * height + Mathf.Max(0, rows - 1) * Mathf.Max(0, grid.spacing.y);

            if (grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount) grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            if (grid.constraintCount != columns) grid.constraintCount = columns;
            if (grid.startAxis != GridLayoutGroup.Axis.Horizontal) grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            if (grid.startCorner != GridLayoutGroup.Corner.UpperLeft) grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            if (grid.childAlignment != TextAnchor.UpperLeft) grid.childAlignment = TextAnchor.UpperLeft;
            var size = new Vector2(width, height);
            if ((grid.cellSize - size).sqrMagnitude > .001f) grid.cellSize = size;
            if (!Mathf.Approximately(panel.minHeight, panelHeight)) panel.minHeight = panelHeight;
            if (!Mathf.Approximately(panel.preferredHeight, panelHeight)) panel.preferredHeight = panelHeight;
        }
    }
}

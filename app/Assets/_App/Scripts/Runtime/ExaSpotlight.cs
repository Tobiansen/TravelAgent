using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    /// <summary>Displays the actual asynchronous request state; the moving mark is indeterminate.</summary>
    public sealed class ExaSpotlight : MonoBehaviour
    {
        public TMP_Text titleLabel, detailLabel, sourceLabel;
        public Button continueButton;
        public RectTransform activityTrack, activityMarker;
        public Image activityImage;
        public ScrollRect contentScroll;
        public event Action Dismissed;
        public bool IsPending { get; private set; }

        private void OnEnable()
        {
            if (continueButton) continueButton.onClick.AddListener(Continue);
            PlainText();
        }

        private void OnDisable()
        {
            if (continueButton) continueButton.onClick.RemoveListener(Continue);
        }

        public void Begin(string title, string detail)
        {
            IsPending = true;
            SetText(title, detail, "");
            if (continueButton) continueButton.gameObject.SetActive(false);
            if (activityTrack) activityTrack.gameObject.SetActive(true);
            Reveal();
        }

        public void Complete(string title, string detail, string source)
        {
            IsPending = false;
            SetText(title, detail, source);
            if (activityTrack) activityTrack.gameObject.SetActive(false);
            if (continueButton) continueButton.gameObject.SetActive(true);
            Reveal();
        }

        public void Fail(string detail)
        {
            Complete("Research needs attention", detail, "");
        }

        public void Hide()
        {
            IsPending = false;
            gameObject.SetActive(false);
        }

        private void Continue()
        {
            if (IsPending) return;
            Hide();
            Dismissed?.Invoke();
        }

        private void Update()
        {
            if (!IsPending) return;
            float pulse = (Mathf.Sin(Time.unscaledTime * 2.4f) + 1) * .5f;
            if (activityImage) { Color color = activityImage.color; color.a = .55f + pulse * .45f; activityImage.color = color; }
            if (activityMarker && activityTrack)
            {
                float distance = Mathf.Max(0, (activityTrack.rect.width - activityMarker.rect.width) * .5f);
                activityMarker.anchoredPosition = new Vector2(Mathf.Sin(Time.unscaledTime * 1.5f) * distance, 0);
            }
        }

        private void SetText(string title, string detail, string source)
        {
            PlainText();
            if (titleLabel) titleLabel.text = title ?? "";
            if (detailLabel) detailLabel.text = detail ?? "";
            if (sourceLabel) { sourceLabel.text = source ?? ""; sourceLabel.gameObject.SetActive(!string.IsNullOrEmpty(source)); }
        }

        private void PlainText()
        {
            if (titleLabel) titleLabel.richText = false;
            if (detailLabel) detailLabel.richText = false;
            if (sourceLabel) sourceLabel.richText = false;
        }

        private void Reveal()
        {
            gameObject.SetActive(true);
            if (!contentScroll) return;
            Canvas.ForceUpdateCanvases();
            contentScroll.StopMovement();
            contentScroll.verticalNormalizedPosition = 1;
        }
    }
}

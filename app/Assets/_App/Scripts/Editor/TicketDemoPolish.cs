using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent.Editor
{
    /// <summary>Saved ticket-first authoring, applied after the compact demo pass and before saving the scene.</summary>
    public static class TicketDemoPolish
    {
        private static AppTheme theme;
        private static Sprite rounded;

        public static void Apply(AppView view, AppTheme palette)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before authoring ticket UI.");
            if (!view || !palette || !view.journeyFlow) throw new InvalidOperationException("Apply the saved compact UI first.");
            theme = palette;
            rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_App/Art/Images/RoundedPanel.png");
            Before(view);
            Report(view);
            Spotlight(view);
            StyleButton(view.loadFinnairSample,false,80,"New demo",27);
            if(view.backendToken.placeholder is TMP_Text tokenPlaceholder)tokenPlaceholder.text="Saved securely on this phone";
            foreach (var input in new[] { view.airline, view.flight, view.route, view.country, view.baggageReference, view.bagDescription, view.collectionDate, view.damageDescription })
                if (input && input.textComponent) input.textComponent.richText = false;
            EditorUtility.SetDirty(view);
        }

        private static void Before(AppView view)
        {
            Transform stage = view.journeyFlow.stages[0].transform;
            Transform card = Find(stage, "BeforeHandoverCard");
            Transform trip = Find(stage, "JourneyDetailsCard");
            var advanced = Find(stage, "DemoPolishOptionalTrip").GetComponent<DemoDisclosure>();
            advanced.SetExpanded(false);
            card.SetAsFirstSibling(); Vertical(card, 12, 20);
            var heading = Text("TicketYourFlightTitle", card, "Your flight", 42, 58); heading.fontStyle = FontStyles.Bold;
            view.ticketStatus = Text("TicketImportStatus", card, "Share ticket text from Gmail, or enter your airline.", 27, 76); view.ticketStatus.color = theme.muted;

            Transform airlineField = view.airline.transform.parent; Move(airlineField, card);
            view.airline.SetTextWithoutNotify("");
            var fieldLabel = airlineField.Find("FieldLabel")?.GetComponent<TMP_Text>();
            if (fieldLabel) { fieldLabel.text = "Operating airline"; fieldLabel.richText = false; }
            if (view.airline.placeholder is TMP_Text placeholder) { placeholder.text = "Enter the airline on your ticket"; placeholder.richText = false; }
            var oldLookup = Find(card, "IdentifyTicketAirline");
            if (oldLookup) Undo.DestroyObjectImmediate(oldLookup.gameObject);
            view.ticketAirlineResult = Text("TicketAirlineResult", card, "", 44, 74); view.ticketAirlineResult.fontStyle = FontStyles.Bold; view.ticketAirlineResult.color = theme.accent;
            view.ticketAirlineResult.gameObject.SetActive(false);
            view.ticketSource = Text("TicketAirlineSource", card, "", 24, 62); view.ticketSource.color = theme.muted; view.ticketSource.gameObject.SetActive(false);
            view.confirmAirline = Button("ConfirmTicketAirline", card, "Use this airline", true, 88, 30);
            view.confirmAirline.interactable = false; view.confirmAirline.gameObject.SetActive(false);
            if (view.discardTicket) Move(view.discardTicket.transform, card);
            view.discardTicket = Button("DiscardSharedTicket", card, "Discard import", false, 64, 25); view.discardTicket.gameObject.SetActive(false);
            if (view.newJourney) Move(view.newJourney.transform, advanced.content.transform);
            view.newJourney = Button("StartNewJourney", advanced.content.transform, "New trip", false, 76, 25);
            var oldSecondary = Find(card, "TicketSecondaryActions");
            if (oldSecondary && oldSecondary.childCount == 0) Undo.DestroyObjectImmediate(oldSecondary.gameObject);

            var photos = Disclosure("TicketOptionalBeforePhotos", card, "Before photos (optional)");
            Move(view.beforePhoto.transform.parent, photos.content.transform);
            Move(view.beforePhotoStatus.transform, photos.content.transform);
            Move(view.beforeImport.transform.parent, photos.content.transform);

            // Retain existing controls and bindings in an explicit secondary section.
            var existingExtra = Find(stage, "TicketOptionalJourneyRecords");
            if (existingExtra) Move(existingExtra, advanced.content.transform);
            var extra = Disclosure("TicketOptionalJourneyRecords", advanced.content.transform, "Additional journey records");
            foreach (var button in new[] { view.startTransit, view.handover, view.collection, view.beforeSample, view.afterSample })
                Move(button.transform, extra.content.transform);

            // The original pass creates this under Report; preserve its fields and keep one instance after repeated authoring.
            var settings = view.backendUrl.GetComponentsInParent<DemoDisclosure>(true)
                .First(section => section.name == "DemoPolishResearchSettings");
            Move(settings.transform, advanced.content.transform); settings.title = "Connection settings"; settings.SetExpanded(false);
            Move(view.guidanceProvider.transform, settings.content.transform); view.guidanceProvider.transform.SetAsFirstSibling();
            foreach (var duplicate in view.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DemoDisclosure>(true))
                .Where(section => section.name == "DemoPolishResearchSettings" && section != settings).ToArray())
                Undo.DestroyObjectImmediate(duplicate.gameObject);

            Move(view.journeyStatus.transform, advanced.content.transform);
            view.journeyStatus.transform.SetAsFirstSibling();
            view.newJourney.transform.SetSiblingIndex(1);
            extra.transform.SetAsLastSibling(); settings.transform.SetAsLastSibling();
            var order = new[] { heading.transform, view.ticketStatus.transform, airlineField,
                view.ticketAirlineResult.transform, view.ticketSource.transform, view.confirmAirline.transform,
                view.discardTicket.transform, photos.transform };
            for (int index = 0; index < order.Length; index++) order[index].SetSiblingIndex(index);
            if (trip) trip.SetAsLastSibling();
        }

        private static void Report(AppView view)
        {
            Transform claim = view.claimPanel.transform;
            var heading = Text("TicketReportingTitle", claim, "Your reporting steps", 38, 56); heading.fontStyle = FontStyles.Bold;
            StyleButton(view.research, true, 112, "Find reporting steps with Exa", 34);
            StyleText(view.guidanceStatus, 24, theme.muted, 56);
            StyleText(view.reportingSummary, 27, theme.ink, 96);
            var evidence = Find(claim, "DemoPolishDamageEvidence");
            var requirements = Find(claim, "DemoPolishAirlineRequirements");
            var draft = Find(claim, "DemoPolishDraftReview");
            var draftSection = draft.GetComponent<DemoDisclosure>();
            Move(view.damageDescription.transform.parent, draftSection.content.transform);
            view.damageDescription.transform.parent.SetAsFirstSibling();
            Move(view.claimStatus.transform, draftSection.content.transform);
            var order = new[] { heading.transform, view.research.transform, view.guidanceStatus.transform,
                view.reportingSummary.transform, evidence, view.openForm.transform, requirements, draft };
            for (int index = 0; index < order.Length; index++) if (order[index]) order[index].SetSiblingIndex(index);
        }

        private static void Spotlight(AppView view)
        {
            Transform safe = view.detailPanel.transform.parent;
            Transform overlay = Panel("TicketExaSpotlightOverlay", safe, theme.background); Stretch((RectTransform)overlay, 0); overlay.SetAsLastSibling();
            var spotlight = Get<ExaSpotlight>(overlay.gameObject);
            Transform card = Panel("ExaSpotlightCard", overlay, theme.card);
            var cardRect = (RectTransform)card; cardRect.anchorMin = new Vector2(.06f, .06f); cardRect.anchorMax = new Vector2(.94f, .94f); cardRect.offsetMin = cardRect.offsetMax = Vector2.zero;
            var viewport = Rect("ExaSpotlightViewport", card); Stretch(viewport, 18); Get<RectMask2D>(viewport.gameObject);
            var content = Rect("ExaSpotlightContent", viewport); Vertical(content, 22, 18);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            var fit = Get<ContentSizeFitter>(content.gameObject); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize; fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            var scroll = Get<ScrollRect>(card.gameObject); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            spotlight.contentScroll = scroll;

            var brand = Text("ExaSpotlightBrand", content, "EXA", 72, 104); brand.color = theme.accent; brand.fontStyle = FontStyles.Bold; brand.characterSpacing = 8;
            spotlight.titleLabel = Text("ExaSpotlightTitle", content, "", 48, 124); spotlight.titleLabel.fontStyle = FontStyles.Bold;
            spotlight.detailLabel = Text("ExaSpotlightDetail", content, "", 30, 160); spotlight.detailLabel.color = theme.muted;
            var track = Rect("ExaActivityTrack", content); Height(track.gameObject, 28); Get<RectMask2D>(track.gameObject);
            var trackImage = Get<Image>(track.gameObject); trackImage.color = theme.input; trackImage.raycastTarget = false;
            var marker = Rect("ExaActivityMarker", track); marker.anchorMin = marker.anchorMax = new Vector2(.5f, .5f); marker.sizeDelta = new Vector2(120, 6); marker.anchoredPosition = Vector2.zero;
            var markImage = Get<Image>(marker.gameObject); markImage.color = theme.accent; markImage.raycastTarget = false;
            spotlight.activityTrack = track; spotlight.activityMarker = marker; spotlight.activityImage = markImage;
            spotlight.sourceLabel = Text("ExaSpotlightSource", content, "", 25, 120); spotlight.sourceLabel.color = theme.muted;
            spotlight.continueButton = Button("ExaSpotlightContinue", content, "Continue", true, 96, 32);
            var privacy = Find(content, "ExaSpotlightPrivacy"); if (privacy) privacy.gameObject.SetActive(false);
            view.exaSpotlight = spotlight; spotlight.Hide();
        }

        private static DemoDisclosure Disclosure(string name, Transform parent, string title)
        {
            var root = Rect(name, parent); Vertical(root, 10, 0);
            var section = Get<DemoDisclosure>(root.gameObject); section.title = title;
            section.toggle = Button("DisclosureToggle", root, title + "  +", false, 76, 27); section.label = section.toggle.GetComponentInChildren<TMP_Text>();
            var content = Rect("DisclosureContent", root); Vertical(content, 12, 0); section.content = content.gameObject; section.SetExpanded(false);
            return section;
        }

        private static Button Button(string name, Transform parent, string label, bool primary, float height, float font)
        {
            var rect = Rect(name, parent); var image = Get<Image>(rect.gameObject); image.sprite = rounded; image.type = Image.Type.Sliced;
            var button = Get<Button>(rect.gameObject); button.targetGraphic = image;
            var text = Text("Label", rect, label, font, height); Stretch(text.rectTransform, 12); text.alignment = TextAlignmentOptions.Center; text.fontStyle = FontStyles.Bold;
            StyleButton(button, primary, height, label, font); return button;
        }

        private static void StyleButton(Button button, bool primary, float height, string label, float font)
        {
            Height(button.gameObject, height);
            if (button.targetGraphic) button.targetGraphic.color = primary ? theme.accent : theme.secondary;
            var text = button.GetComponentInChildren<TMP_Text>(true); text.text = label; text.font = theme.font; text.fontSize = font; text.enableAutoSizing = true; text.fontSizeMin = 23; text.fontSizeMax = font; text.richText = false; text.color = primary ? theme.onAccent : theme.ink;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(.92f, .92f, .95f); colors.pressedColor = new Color(.7f, .7f, .75f); colors.disabledColor = new Color(.34f, .34f, .38f); button.colors = colors;
            var state = Get<DemoButtonState>(button.gameObject); state.button = button; state.label = text; state.normalText = text.color; state.disabledText = theme.muted;
        }

        private static TMP_Text Text(string name, Transform parent, string value, float font, float height)
        {
            var rect = Rect(name, parent); var text = Get<TextMeshProUGUI>(rect.gameObject); text.text = value; text.raycastTarget = false; text.richText = false;
            StyleText(text, font, theme.ink, height); return text;
        }

        private static void StyleText(TMP_Text text, float font, Color color, float height)
        {
            text.font = theme.font; text.fontSize = font; text.enableAutoSizing = false; text.richText = false; text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Overflow;
            var element = Height(text.gameObject, height); element.preferredHeight = -1;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var existing = parent.Find(name); if (existing) return (RectTransform)existing;
            var go = new GameObject(name, typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go, "Author ticket UI"); go.transform.SetParent(parent, false); return (RectTransform)go.transform;
        }
        private static Transform Panel(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent); var image = Get<Image>(rect.gameObject); image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color; return rect;
        }
        private static T Get<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? Undo.AddComponent<T>(go);
        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);
        private static void Move(Transform child, Transform parent) { if (child.parent != parent) { Undo.SetTransformParent(child, parent, "Move saved ticket control"); child.localScale = Vector3.one; } }
        private static LayoutElement Height(GameObject go, float height)
        {
            var element = Get<LayoutElement>(go); element.minWidth = 0; element.preferredWidth = -1; element.flexibleWidth = 1; element.minHeight = height; element.preferredHeight = height; element.flexibleHeight = 0; return element;
        }
        private static void Vertical(Transform root, float spacing, int padding)
        {
            var layout = Get<VerticalLayoutGroup>(root.gameObject); layout.spacing = spacing; layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        }
        private static void Horizontal(Transform root, float spacing)
        {
            var layout = Get<HorizontalLayoutGroup>(root.gameObject); layout.spacing = spacing; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        }
        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset;
            var element = rect.GetComponent<LayoutElement>(); if (element) element.ignoreLayout = true;
        }
    }
}

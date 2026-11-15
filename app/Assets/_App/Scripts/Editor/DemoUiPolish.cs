using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TravelAgent.Editor
{
    /// <summary>Explicit additive authoring pass for the compact demo. Existing product controls/references are retained.</summary>
    public static class DemoUiPolish
    {
        private static AppTheme theme;
        private static Sprite rounded;

        [MenuItem("Tools/Travel Agent/Apply compact dark demo UI")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before applying saved UI polish.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != AppSceneBuilder.ScenePath) throw new InvalidOperationException("Open Assets/_App/Scenes/App.unity first.");
            var view = UnityEngine.Object.FindAnyObjectByType<AppView>();
            if (!view) throw new InvalidOperationException("The saved AppView is required.");
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Compact dark demo UI");
            foreach (var root in scene.GetRootGameObjects()) Undo.RegisterFullObjectHierarchyUndo(root, "Compact dark demo UI");
            theme = AssetDatabase.LoadAssetAtPath<AppTheme>("Assets/_App/Configuration/Theme.asset");
            rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_App/Art/Images/RoundedPanel.png");
            Undo.RecordObject(theme, "Set demo palette");
            theme.background = Hex("101114"); theme.card = Hex("1B1D22"); theme.ink = Hex("F4F2F1");
            theme.muted = Hex("A8A8B1"); theme.accent = Hex("FF3347"); theme.warning = theme.accent;
            theme.secondary = Hex("282B32"); theme.input = Hex("14161B"); theme.disabled = Hex("34373F");
            theme.border = Hex("3A3D45"); theme.onAccent = theme.ink; theme.layoutRevision = 4;
            if (!theme.font) theme.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            var app = view.GetComponent<AppComposition>(); app.theme = theme;
            var adaptive = view.GetComponent<CompactLandscapeLayout>(); if (adaptive) adaptive.enabled = false;

            StyleTree(scene.GetRootGameObjects());
            CompactShell(view);
            CompactGuard(view);
            CompactJourney(view);
            CompactDevice(view);
            CompactEvents(view);
            CompactPresenter(view);
            CompactInspectionOverlays(view);
            StyleButtons(view);
            TicketDemoPolish.Apply(view,theme);
            PolishPrefabs();
            theme.appliedAccent = theme.accent; theme.appliedInk = theme.ink; theme.appliedMuted = theme.muted;
            theme.appliedBackground = theme.background; theme.appliedCard = theme.card;
            theme.appliedSecondary = theme.secondary; theme.appliedInput = theme.input; theme.appliedDisabled = theme.disabled; theme.appliedBorder = theme.border; theme.appliedOnAccent = theme.onAccent;
            EditorUtility.SetDirty(theme); EditorUtility.SetDirty(view); EditorUtility.SetDirty(app);
            if (app.configuration) { Undo.RecordObject(app.configuration, "Start demo on Guard"); app.configuration.startupScreen = 0; EditorUtility.SetDirty(app.configuration); }
            view.selector.Show(0); view.journeyFlow.ShowStage(0);
            foreach (var root in scene.GetRootGameObjects()) EditorUtility.SetDirty(root);
            Canvas.ForceUpdateCanvases();
            view.demoPanel.GetComponent<PresenterGridFit>().RefreshLayout();
            Canvas.ForceUpdateCanvases();
            EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(undo);
            Debug.Log("Saved compact charcoal/red demo UI, staged Journey, sequence pad and disturbance graph. Original controls and bindings preserved.");
        }

        private static void CompactShell(AppView view)
        {
            Transform header = view.pageTitle.transform.parent;
            Size(header.gameObject, 124); V(header, 4, 0);
            var brandLine = Find(header, "BrandAndConnection"); if (brandLine) { Size(brandLine.gameObject, 34); H(brandLine, 10); }
            var brand = Find(header, "Brand"); if (brand) Label(brand.GetComponent<TMP_Text>(), "TRAVEL AGENT", 23, theme.ink, 32);
            Label(view.pageTitle, "Guard", 48, theme.ink, 66); view.pageTitle.fontStyle = FontStyles.Bold;
            Label(view.providerLabel, "DEMO", 22, theme.accent, 32);
            Label(view.connectionLabel, "Disconnected", 22, theme.muted, 32);
            Hide(header, "Tagline");
            Transform shell = header.parent; V(shell, 12, 22);
            Size(view.toast.transform.parent.gameObject, 76); V(view.toast.transform.parent, 0, 12);
            Label(view.toast, "Connect your bag to begin.", 26, theme.ink, 48);
            view.toast.overflowMode = TextOverflowModes.Ellipsis; view.toast.maxVisibleLines = 2;
            Transform nav = view.navigation[0].transform.parent; Size(nav.gameObject, 100); H(nav, 8);
            string[] tabs = { "Guard", "Journey", "Events", "Device" };
            for (int i = 0; i < view.navigation.Length; i++) { Size(view.navigation[i].gameObject, 92); ButtonLabel(view.navigation[i], tabs[i], 30); }
            foreach (var screen in view.selector.screens)
            {
                var content = screen.transform.Find("Viewport/Content"); V(content, 16, 0);
                foreach (var scroll in screen.GetComponentsInChildren<ScrollRect>(true)) { scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40; }
            }
            view.detailTitle.fontSize = 38; view.detailBody.fontSize = 29;
            Size(view.closeDetail.gameObject, 88);
        }

        private static void CompactGuard(AppView view)
        {
            Transform content = view.selector.screens[0].transform.Find("Viewport/Content");
            Transform hero = view.guardState.transform.parent;
            Hide(hero, "SuitcaseIllustration"); HideAll(hero, "SectionLabel"); Hide(content, "GuardFootnote");
            V(hero, 10, 22);
            Label(view.guardState, "Unknown", 58, theme.ink, 78); view.guardState.fontStyle = FontStyles.Bold;
            Label(view.guardDetail, "Connect to read the board state.", 30, theme.muted, 74);
            Size(view.guardAction.gameObject, 100); ButtonLabel(view.guardAction, "Arm bag", 34);
            Transform graphCard = Panel("DemoPolishDisturbanceCard", content); V(graphCard, 8, 20); graphCard.SetSiblingIndex(hero.GetSiblingIndex() + 1);
            var title = Text("TimelineTitle", graphCard, "Disturbance timeline", 32, 44); title.fontStyle = FontStyles.Bold;
            var scale = Text("TimelineScale", graphCard, "Event category · tap a marker", 22, 32); scale.color = theme.muted;
            var plotRow = Rect("TimelinePlotRow", graphCard); H(plotRow, 10); Size(plotRow.gameObject, 270);
            plotRow.SetSiblingIndex(scale.transform.GetSiblingIndex() + 1);
            plotRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var axis = Rect("CategoryAxis", plotRow); Size(axis.gameObject, 270); axis.SetAsFirstSibling();
            var axisElement = axis.GetComponent<LayoutElement>(); axisElement.minWidth = 125; axisElement.preferredWidth = 125; axisElement.flexibleWidth = 0;
            CategoryLabel(axis, "Bump", .24f); CategoryLabel(axis, "Movement", .46f);
            CategoryLabel(axis, "Opening", .66f); CategoryLabel(axis, "Impact", .88f);
            var graph = Find(graphCard, "DisturbanceGraph") as RectTransform;
            if (!graph) graph = Rect("DisturbanceGraph", plotRow);
            Move(graph, plotRow); graph.SetAsLastSibling(); Size(graph.gameObject, 270);
            graph.GetComponent<LayoutElement>().flexibleWidth = 1;
            var timeline = Get<DisturbanceTimeline>(graph.gameObject); timeline.raycastTarget = true;
            timeline.yAxisLabels = new[] { "Bump", "Movement", "Opening", "Impact" }
                .Select(category => axis.Find("Category" + category).GetComponent<TMP_Text>()).ToArray();
            var empty = Text("TimelineEmpty", graph, "No disturbance events received", 30, 80); Stretch(empty.rectTransform, 16); empty.alignment = TextAlignmentOptions.Center;
            var caption = Text("TimelineCaption", graphCard, "Recorded events, with their original source and time quality.", 22, 52); caption.color = theme.muted;
            var selection = Text("TimelineSelection", graphCard, "Tap a marker to inspect its record.", 24, 50); selection.color = theme.muted;
            timeline.titleLabel = title; timeline.scaleLabel = scale; timeline.emptyLabel = empty; timeline.captionLabel = caption; timeline.selectionLabel = selection;
            timeline.SetPalette(theme.ink, theme.muted, theme.accent, theme.border); view.timeline = timeline;
            Transform inspection = Panel("DemoPolishInspectionPrompt", content); V(inspection, 10, 18);
            inspection.SetSiblingIndex(graphCard.GetSiblingIndex() + 1);
            Text("InspectionPromptTitle", inspection, "Check bag and contents", 31, 44).fontStyle = FontStyles.Bold;
            view.inspectionStatus = Text("InspectionEventStatus", inspection, "Events prompt inspection; they do not establish damage.", 25, 68);
            view.inspectionStatus.color = theme.muted;
            view.inspectBagContents = Button("InspectBagAndContents", inspection, "Inspect bag and contents", true, 88);
            view.inspectionPromptPanel = inspection.gameObject;
            inspection.gameObject.SetActive(false); // Runtime enables this only for pending inspection prompts.
            Transform recent = view.recentEvents.parent; V(recent, 8, 18);
            HideAll(recent, "SectionLabel"); Text("DemoPolishRecentHeading", recent, "Recent events", 30, 40).transform.SetAsFirstSibling();
            var details = Disclosure("DemoPolishGuardDetails", content, "Device details");
            Move(view.guardCodes.transform, details.content.transform); Move(view.recordingLabel.transform, details.content.transform);
            Label(view.guardCodes, view.guardCodes.text, 26, theme.muted, 46);
            Label(view.recordingLabel, view.recordingLabel.text, 26, theme.muted, 68);
        }

        private static void CompactJourney(AppView view)
        {
            Transform content = view.selector.screens[1].transform.Find("Viewport/Content");
            Transform trip = Find(content, "JourneyDetailsCard");
            Transform before = Find(content, "BeforeHandoverCard");
            Transform inspect = Find(content, "CollectionCard");
            Transform claim = view.claimPanel.transform;
            var flowHeader = Panel("DemoPolishJourneyHeader", content); flowHeader.SetAsFirstSibling(); V(flowHeader, 10, 18);
            Move(view.journeyStatus.transform, flowHeader); Label(view.journeyStatus, view.journeyStatus.text, 27, theme.muted, 62);
            var buttons = Rect("StageButtons", flowHeader); H(buttons, 8); Size(buttons.gameObject, 86);
            var flow = Get<DemoJourneyFlow>(content.gameObject);
            flow.stages = new[] { Rect("DemoPolishBeforeStage", content).gameObject, Rect("DemoPolishInspectStage", content).gameObject, Rect("DemoPolishReportStage", content).gameObject };
            for (int i = 0; i < flow.stages.Length; i++) { V(flow.stages[i].transform, 16, 0); flow.stages[i].transform.SetSiblingIndex(i + 1); }
            flow.stageButtons = new[] { Button("BeforeStep", buttons, "01 Before", false, 82), Button("InspectStep", buttons, "02 Inspect", false, 82), Button("ReportStep", buttons, "03 Report", false, 82) };
            flow.selectedColor = theme.accent; flow.otherColor = theme.secondary;
            flow.scroll = view.selector.screens[1].GetComponent<ScrollRect>();
            Move(trip, flow.stages[0].transform); Move(before, flow.stages[0].transform);
            Move(inspect, flow.stages[1].transform); Move(claim, flow.stages[2].transform);
            Hide(trip, "JourneyHeading"); HideAll(trip, "SectionLabel");
            var optional = Disclosure("DemoPolishOptionalTrip", trip, "Trip details (optional)");
            // Airline stays easy to change in Report; all other original metadata fields remain optional.
            var airlineField = view.airline.transform.parent;
            Move(airlineField, claim); airlineField.SetAsFirstSibling();
            foreach (Transform child in trip.Cast<Transform>().ToArray()) if (child != optional.transform) Move(child, optional.content.transform);
            V(trip, 0, 0);
            foreach (var source in new[] { before, inspect, claim }) { V(source, 12, 20); HideAll(source, "SectionLabel"); }
            var quiet = Find(before, "QuietModeExplanation"); if (quiet) Move(quiet, optional.content.transform);
            Label(Find(inspect, "InspectionQuestion")?.GetComponent<TMP_Text>(), "Did you find damage?", 34, theme.ink, 48);
            Label(Find(inspect, "InspectionHelp")?.GetComponent<TMP_Text>(), "Check your suitcase and its contents. Events do not prove damage; photos are not analysed.", 25, theme.muted, 84);
            CompactPhoto(view.beforePhoto, view.beforePhotoStatus, view.beforeSample, view.beforeImport);
            CompactPhoto(view.afterPhoto, view.afterPhotoStatus, view.afterSample, view.afterImport);
            ButtonLabel(view.beforeSample, "Sample photo", 27); ButtonLabel(view.afterSample, "Sample photo", 27);
            ButtonLabel(view.beforeImport, "Choose photo", 27); ButtonLabel(view.afterImport, "Choose photo", 27);
            view.beforeCapture = Button("BeforeCaptureButton", view.beforeImport.transform.parent, "Take photo", true, 84);
            view.afterCapture = Button("AfterCaptureButton", view.afterImport.transform.parent, "Take photo", true, 84);
            view.beforeCapture.transform.SetAsFirstSibling(); view.afterCapture.transform.SetAsFirstSibling();
            var inspectOrder = new[] { Find(inspect, "InspectionQuestion"), Find(inspect, "InspectionHelp"), view.damageYes.transform.parent,
                view.afterPhoto.transform.parent, view.afterPhotoStatus.transform, view.afterImport.transform.parent, view.collection.transform };
            for (int i = 0; i < inspectOrder.Length; i++) inspectOrder[i].SetSiblingIndex(i);
            ButtonLabel(view.startTransit, "Start transit recording", 31); ButtonLabel(view.handover, "Record handover", 31);
            ButtonLabel(view.collection, "Record collection", 31); ButtonLabel(view.damageYes, "Yes, I found damage", 29);
            ButtonLabel(view.damageNo, "No damage found", 29);
            foreach (var button in new[] { view.startTransit, view.handover, view.collection, view.damageYes, view.damageNo }) Size(button.gameObject, 92);

            var researchSettings = Disclosure("DemoPolishResearchSettings", claim, "Research settings");
            Move(view.toggleGuidance.transform, researchSettings.content.transform);
            Move(view.backendUrl.transform.parent, researchSettings.content.transform); Move(view.backendToken.transform.parent, researchSettings.content.transform);
            Label(view.guidanceProvider, view.guidanceProvider.text, 24, theme.accent, 38);
            Label(view.guidanceStatus, view.guidanceStatus.text, 27, theme.muted, 76);
            Label(view.claimStatus, view.claimStatus.text, 26, theme.muted, 54);
            CompactInput(view.damageDescription, 120); CompactInput(view.claimDraft, 250);
            ButtonLabel(view.research, "Get airline reporting steps", 30); ButtonLabel(view.export, "Share evidence packet", 29);
            ButtonLabel(view.openForm, "Open airline reporting page", 30);
            foreach (var button in new[] { view.research, view.export, view.openForm }) Size(button.gameObject, 92);
            var requirements = Disclosure("DemoPolishAirlineRequirements", claim, "Airline requirements");
            Move(view.guidanceItems, requirements.content.transform);
            var draft = Disclosure("DemoPolishDraftReview", claim, "Draft and evidence packet");
            Move(view.claimDraft.transform.parent, draft.content.transform);
            Move(view.regenerateDraft.transform, draft.content.transform); Move(view.copyDraft.transform, draft.content.transform);
            Size(view.regenerateDraft.gameObject, 88); Size(view.copyDraft.gameObject, 88);
            Move(view.export.transform, draft.content.transform);
            Hide(claim, "DraftActions");
            var existingReportDetails = Find(claim, "DemoPolishReportDetails");
            if (existingReportDetails) Move(existingReportDetails, draft.content.transform);
            var reportAdvanced = Disclosure("DemoPolishReportDetails", draft.content.transform, "Submission confirmation");
            reportAdvanced.transform.SetAsLastSibling();
            Move(view.submissionReference.transform.parent, reportAdvanced.content.transform);
            Move(view.confirmSubmission.transform, reportAdvanced.content.transform);
            var review = Find(claim, "ReviewNotice"); if (review) Move(review, reportAdvanced.content.transform);
            Transform evidence = Panel("DemoPolishDamageEvidence", claim); V(evidence, 10, 16);
            Text("DamageEvidenceTitle", evidence, "Add damage photos", 32, 46).fontStyle = FontStyles.Bold;
            view.reportPhotoStatus = Text("ReportPhotoStatus", evidence, "Add photos of the damage you observed.", 25, 62); view.reportPhotoStatus.color = theme.muted;
            Transform photoActions = Rect("DamagePhotoActions", evidence); H(photoActions, 10); Size(photoActions.gameObject, 92);
            view.takeDamagePhoto = Button("TakeDamagePhoto", photoActions, "Take photo", true, 88);
            view.chooseDamagePhoto = Button("ChooseDamagePhoto", photoActions, "Choose existing", false, 88);
            view.reportingSummary = Text("ReportingSummary", claim, "Research the airline's steps, gather the requested evidence, then review its official reporting page.", 26, 110);
            view.reportingSummary.color = theme.muted;
            // The official reporting path is primary. Draft/export and technical settings remain explicit disclosures.
            var reportOrder = new[] { airlineField, view.damageDescription.transform.parent, view.guidanceProvider.transform,
                view.research.transform, view.guidanceStatus.transform, view.reportingSummary.transform, evidence, view.openForm.transform,
                view.claimStatus.transform, requirements.transform, draft.transform, researchSettings.transform };
            for (int i = 0; i < reportOrder.Length; i++) reportOrder[i].SetSiblingIndex(i);
            var reportHint = Text("DemoPolishReportHint", flow.stages[2].transform, "Inspect your bag and contents first. Only damage you confirm starts a damage report.", 29, 110);
            reportHint.transform.SetAsFirstSibling(); flow.reportHint = reportHint.gameObject; flow.reportContent = view.claimPanel;
            var footer = Rect("DemoPolishJourneyNavigation", content); footer.SetAsLastSibling(); H(footer, 12); Size(footer.gameObject, 94);
            flow.backButton = Button("BackStage", footer, "Back", false, 90); flow.nextButton = Button("NextStage", footer, "Inspect bag and contents", true, 90);
            flow.nextLabel = flow.nextButton.GetComponentInChildren<TMP_Text>(); view.journeyFlow = flow;
        }

        private static void CompactDevice(AppView view)
        {
            Transform content = view.selector.screens[3].transform.Find("Viewport/Content");
            Transform connection = Find(content, "ConnectionCard"); Transform codes = Find(content, "SequenceCard"); Transform feedback = Find(content, "FeedbackCard");
            V(connection, 10, 20); Hide(connection, "ConnectionHeading"); HideAll(connection, "SectionLabel");
            Label(view.deviceStatus, view.deviceStatus.text, 28, theme.muted, 82);
            Size(view.scan.gameObject, 92); Size(view.disconnect.gameObject, 92); Size(view.authorizePhone.gameObject, 88);
            ButtonLabel(view.scan, "Find nearby bag", 30); ButtonLabel(view.authorizePhone, "Authorize phone", 30);
            var settings = Disclosure("DemoPolishDeviceSettings", connection, "Connection settings");
            var providers = Find(connection, "ProviderChoices"); if (providers) Move(providers, settings.content.transform);
            var ownerHelp = Find(connection, "OwnerHelp"); if (ownerHelp) Move(ownerHelp, settings.content.transform);
            V(codes, 10, 20); Hide(codes, "SequenceHeading"); Hide(codes, "SequenceHelp"); HideAll(codes, "SectionLabel");
            Text("DemoPolishSequenceHeading", codes, "Button sequences", 36, 48).transform.SetAsFirstSibling();
            Label(view.configStatus, view.configStatus.text, 26, theme.muted, 66);
            var hidden = Rect("DemoPolishLegacySequenceFields", codes);
            foreach (var field in new[] { view.armCode, view.disarmCode, view.confirmArmCode, view.confirmDisarmCode }) Move(field.transform.parent, hidden);
            hidden.gameObject.SetActive(false);
            var oldArm = Find(codes, "ArmSequenceRow"); if (oldArm) oldArm.gameObject.SetActive(false);
            var oldDisarm = Find(codes, "DisarmSequenceRow"); if (oldDisarm) oldDisarm.gameObject.SetActive(false);
            var padRoot = Rect("DemoPolishSequencePad", codes); padRoot.SetSiblingIndex(view.configStatus.transform.GetSiblingIndex() + 1); V(padRoot, 8, 0);
            var pad = Get<SequenceEntryPad>(padRoot.gameObject);
            pad.instruction = Text("SequenceInstruction", padRoot, "1 / 4  ·  Enter arm sequence", 31, 54);
            pad.progress = Text("SequenceProgress", padRoot, "○ ○ ○ ○", 42, 66); pad.progress.alignment = TextAlignmentOptions.Center;
            var ab = Rect("SequenceButtons", padRoot); H(ab, 16); Size(ab.gameObject, 120);
            pad.aButton = Button("EnterA", ab, "A", true, 116); pad.bButton = Button("EnterB", ab, "B", true, 116);
            ButtonLabel(pad.aButton, "A", 52); ButtonLabel(pad.bButton, "B", 52);
            var corrections = Rect("SequenceCorrections", padRoot); H(corrections, 10); Size(corrections.gameObject, 80);
            pad.undoButton = Button("UndoSequenceTap", corrections, "Undo", false, 76); pad.restartButton = Button("RestartSequenceCapture", corrections, "Start over", false, 76);
            pad.summary = Text("SequenceSummary", padRoot, "Confirmed settings stay unchanged until the bag acknowledges Save.", 25, 104); pad.summary.color = theme.muted;
            pad.armTarget = view.armCode; pad.disarmTarget = view.disarmCode; pad.confirmArmTarget = view.confirmArmCode; pad.confirmDisarmTarget = view.confirmDisarmCode;
            view.sequencePad = pad;
            Size(view.saveSequences.gameObject, 96); ButtonLabel(view.saveSequences, "Save sequences on bag", 31); view.clearSequences.gameObject.SetActive(false);
            var advanced = Disclosure("DemoPolishDeviceAdvanced", content, "Device diagnostics");
            Move(view.codeProgress.transform, advanced.content.transform); if (feedback) Move(feedback, advanced.content.transform);
        }

        private static void CompactEvents(AppView view)
        {
            Transform content = view.selector.screens[2].transform.Find("Viewport/Content");
            Hide(content, "HistoryHeading"); Hide(content, "HistoryHelp"); HideAll(content, "SectionLabel");
            Label(view.historyStatus, view.historyStatus.text, 28, theme.muted, 76);
        }
        private static void CompactPresenter(AppView view)
        {
            Size(view.demoToggle.gameObject, 54); ButtonLabel(view.demoToggle, "Presenter controls", 23);
            view.loadFinnairSample = Button("LoadFinnairSampleCase", view.demoPanel.transform, "Load Finnair case", false, 80);
            view.loadFinnairSample.transform.SetAsFirstSibling();
            ConfigurePresenterGrid(view.demoPanel);
            foreach (var text in view.demoPanel.GetComponentsInChildren<TMP_Text>(true)) text.fontSize = 23;
            view.demoPanel.SetActive(false);
        }

        private static void CompactInspectionOverlays(AppView view)
        {
            Transform detailCard = view.closeDetail.transform.parent;
            V(detailCard, 12, 22);
            Label(view.detailTitle, view.detailTitle.text, 34, theme.ink, 50);
            Label(view.detailBody, view.detailBody.text, 26, theme.muted, 200);
            var bodyElement = view.detailBody.GetComponent<LayoutElement>(); bodyElement.preferredHeight = -1; bodyElement.flexibleHeight = 1;
            view.detailInspect = Button("InspectFromEvent", detailCard, "Inspect bag and contents", true, 88);
            view.detailInspect.transform.SetSiblingIndex(view.closeDetail.transform.GetSiblingIndex());
            view.closeDetail.transform.SetAsLastSibling(); ButtonLabel(view.closeDetail, "Close", 29);

            Transform safe = view.detailPanel.transform.parent;
            Transform overlay = Panel("DemoPolishCameraRecoveryOverlay", safe); Stretch((RectTransform)overlay, 0); overlay.SetAsLastSibling();
            overlay.GetComponent<Image>().color = new Color(0, 0, 0, .9f);
            Transform card = Panel("CameraRecoveryCard", overlay);
            var rect = (RectTransform)card; rect.anchorMin = new Vector2(.08f, .1f); rect.anchorMax = new Vector2(.92f, .9f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            // All recovery choices remain reachable on a short landscape screen as well as portrait.
            var oldLayout = card.GetComponent<VerticalLayoutGroup>(); if (oldLayout) Undo.DestroyObjectImmediate(oldLayout);
            RectTransform viewport = Rect("RecoveryViewport", card); Stretch(viewport, 12); Get<RectMask2D>(viewport.gameObject);
            RectTransform recoveryContent = Rect("RecoveryContent", viewport); V(recoveryContent, 14, 12);
            recoveryContent.anchorMin = new Vector2(0, 1); recoveryContent.anchorMax = Vector2.one; recoveryContent.pivot = new Vector2(.5f, 1);
            recoveryContent.anchoredPosition = Vector2.zero; recoveryContent.sizeDelta = Vector2.zero;
            var fit = Get<ContentSizeFitter>(recoveryContent.gameObject); fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = Get<ScrollRect>(card.gameObject); scroll.viewport = viewport; scroll.content = recoveryContent;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
            foreach (var child in card.Cast<Transform>().ToArray()) if (child != viewport) Move(child, recoveryContent);
            var recoveryTitle = Text("RecoveryTitle", recoveryContent, "Recovered photo", 36, 56); recoveryTitle.fontStyle = FontStyles.Bold;
            RectTransform photo = Rect("RecoveredPhotoPreview", recoveryContent); Size(photo.gameObject, 260);
            var photoElement = photo.GetComponent<LayoutElement>(); photoElement.minHeight = 120; photoElement.flexibleHeight = 1;
            view.recoveryPhoto = Get<Image>(photo.gameObject); view.recoveryPhoto.color = Color.white; view.recoveryPhoto.preserveAspect = true; view.recoveryPhoto.raycastTarget = false;
            view.recoveryStatus = Text("RecoveryStatus", recoveryContent, "A photo is waiting for review. It is not attached automatically.", 27, 105); view.recoveryStatus.color = theme.muted;
            view.recoveryUse = Button("UseRecoveredPhoto", recoveryContent, "Use for this journey", true, 88); view.recoveryUse.interactable = false;
            view.recoveryRetry = Button("CheckRecoveredPhotoAgain", recoveryContent, "Check again", false, 80);
            view.recoveryLater = Button("ReviewRecoveredPhotoLater", recoveryContent, "Review later", false, 80);
            view.recoveryDiscard = Button("DiscardRecoveredPhoto", recoveryContent, "Discard captured image", false, 88);
            var recoveryNotice = Text("RecoveryChoiceNotice", recoveryContent, "Check the journey and photo stage before attaching.", 23, 48); recoveryNotice.color = theme.muted;
            var recoveryOrder = new[] { recoveryTitle.transform, photo, view.recoveryStatus.transform, view.recoveryUse.transform,
                view.recoveryRetry.transform, view.recoveryLater.transform, view.recoveryDiscard.transform, recoveryNotice.transform };
            for (int i = 0; i < recoveryOrder.Length; i++) recoveryOrder[i].SetSiblingIndex(i);
            view.recoveryPanel = overlay.gameObject; overlay.gameObject.SetActive(false);
        }

        private static void StyleTree(GameObject[] roots)
        {
            foreach (var root in roots)
            {
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.font = theme.font; text.fontSize = text.name == "FieldLabel" ? 25 : 29;
                    text.color = text.name == "Placeholder" || text.name == "FieldLabel" ? theme.muted : theme.ink;
                    text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Overflow;
                    var element = text.GetComponent<LayoutElement>(); if (element) { element.minWidth = 0; element.preferredWidth = -1; }
                }
                foreach (var layout in root.GetComponentsInChildren<VerticalLayoutGroup>(true)) { layout.spacing = 12; layout.childForceExpandHeight = false; layout.childControlHeight = true; layout.childControlWidth = true; }
                foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true)) CompactInput(input, input.lineType == TMP_InputField.LineType.SingleLine ? 88 : 200);
                foreach (var image in root.GetComponentsInChildren<Image>(true))
                {
                    if (image.name.EndsWith("ConditionImage", StringComparison.Ordinal)) { image.color = Color.white; continue; }
                    if (image.GetComponent<TMP_InputField>()) image.color = theme.input;
                    else if (image.GetComponent<Button>()) image.color = theme.secondary;
                    else if (image.name == "Background") image.color = theme.background;
                    else if (image.name.Contains("Overlay")) image.color = new Color(0, 0, 0, .88f);
                    else if (image.name.Contains("Frame")) image.color = theme.input;
                    else image.color = theme.card;
                }
            }
        }
        private static void StyleButtons(AppView view)
        {
            var primary = new[] { view.guardAction, view.scan, view.authorizePhone, view.saveSequences, view.beforeCapture, view.afterCapture, view.startTransit, view.openForm, view.inspectBagContents, view.detailInspect, view.takeDamagePhoto, view.recoveryUse };
            foreach (var root in view.gameObject.scene.GetRootGameObjects())
                foreach (var button in root.GetComponentsInChildren<Button>(true)) PaintButton(button, primary.Contains(button) || button == view.sequencePad.aButton || button == view.sequencePad.bButton || button == view.journeyFlow.nextButton);
            foreach (var stage in view.journeyFlow.stageButtons) PaintButton(stage, false);
        }
        private static void PolishPrefabs()
        {
            foreach (string path in new[] { "Assets/_App/Prefabs/Components/TimelineRow.prefab", "Assets/_App/Prefabs/DemoControls.prefab" })
            {
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) continue;
                var prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    StyleTree(new[] { prefab });
                    var row = prefab.GetComponent<ListRowView>();
                    if (row)
                    {
                        V(prefab.transform, 6, 16); Size(prefab, 160, false);
                        Label(row.title, row.title.text, 28, theme.ink, 38); row.title.fontStyle = FontStyles.Bold;
                        Label(row.detail, row.detail.text, 24, theme.muted, 72); PaintButton(row.action, false);
                    }
                    else
                    {
                        foreach (var button in prefab.GetComponentsInChildren<Button>(true)) PaintButton(button, false);
                        if (prefab.GetComponent<GridLayoutGroup>()) ConfigurePresenterGrid(prefab);
                    }
                    PrefabUtility.SaveAsPrefabAsset(prefab, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
        }

        private static void CompactPhoto(Image photo, TMP_Text status, Button sample, Button import)
        { Size(photo.transform.parent.gameObject, 220); Label(status, status.text, 24, theme.muted, 42); Size(sample.gameObject, 84); Size(import.gameObject, 84); }
        private static void ConfigurePresenterGrid(GameObject root)
        {
            var grid = root.GetComponent<GridLayoutGroup>(); if (!grid) return;
            grid.spacing = new Vector2(8, 8);
            var fit = Get<PresenterGridFit>(root); fit.grid = grid; fit.panel = Get<LayoutElement>(root);
            fit.portraitColumns = 3; fit.landscapeColumns = 6; fit.minimumCellWidth = 160; fit.cellHeight = 80;
            fit.RefreshLayout();
        }
        private static void CategoryLabel(Transform axis, string category, float level)
        {
            var label = Text("Category" + category, axis, category, 22, 32);
            label.color = theme.muted; label.alignment = TextAlignmentOptions.Right; label.textWrappingMode = TextWrappingModes.NoWrap;
            // Match DisturbanceTimeline's fixed 270px height and 22px plot inset, with categorical heights from the domain projection.
            float y = (22 + level * (270 - 44)) / 270;
            var rect = label.rectTransform; rect.anchorMin = new Vector2(0, y); rect.anchorMax = new Vector2(1, y);
            rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(0, 32); rect.anchoredPosition = Vector2.zero;
            label.GetComponent<LayoutElement>().ignoreLayout = true;
        }
        private static void CompactInput(TMP_InputField input, float boxHeight)
        {
            Size(input.gameObject, boxHeight); Size(input.transform.parent.gameObject, boxHeight + 42); V(input.transform.parent, 6, 0);
            input.textComponent.fontSize = 30; input.textComponent.color = theme.ink;
            if (input.placeholder is TMP_Text placeholder) { placeholder.fontSize = 27; placeholder.color = theme.muted; }
            if (input.targetGraphic) input.targetGraphic.color = theme.input;
            var fieldLabel = input.transform.parent.Find("FieldLabel")?.GetComponent<TMP_Text>(); if (fieldLabel) Label(fieldLabel, fieldLabel.text, 25, theme.muted, 32);
        }
        private static DemoDisclosure Disclosure(string name, Transform parent, string title)
        {
            var root = Rect(name, parent); V(root, 10, 0);
            var disclosure = Get<DemoDisclosure>(root.gameObject); disclosure.title = title;
            disclosure.toggle = Button("DisclosureToggle", root, title + "  +", false, 76);
            disclosure.label = disclosure.toggle.GetComponentInChildren<TMP_Text>();
            var content = Rect("DisclosureContent", root); V(content, 12, 0); disclosure.content = content.gameObject; disclosure.SetExpanded(false);
            return disclosure;
        }
        private static void PaintButton(Button button, bool primary)
        {
            if (!button) return;
            if (button.targetGraphic) button.targetGraphic.color = primary ? theme.accent : theme.secondary;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(.92f, .92f, .95f, 1); colors.pressedColor = new Color(.7f, .7f, .75f, 1); colors.selectedColor = Color.white;
            bool row = button.GetComponent<ListRowView>(); colors.disabledColor = row ? Color.white : new Color(.34f, .34f, .38f, 1); colors.fadeDuration = .08f; button.colors = colors;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label) { label.color = primary ? theme.onAccent : theme.ink; label.fontSizeMin = 23; label.fontSizeMax = Mathf.Max(29, label.fontSize); }
            if (!row && label)
            {
                var state = Get<DemoButtonState>(button.gameObject); state.button = button; state.label = label; state.normalText = primary ? theme.onAccent : theme.ink; state.disabledText = theme.muted;
            }
            var element = button.GetComponent<LayoutElement>(); if (element) { element.minWidth = 0; element.flexibleWidth = 1; if (!row && element.minHeight > 130) Size(button.gameObject, 90); }
        }
        private static Button Button(string name, Transform parent, string text, bool primary, float height)
        {
            var rect = Rect(name, parent); var image = Get<Image>(rect.gameObject); image.sprite = rounded; image.type = Image.Type.Sliced;
            var button = Get<Button>(rect.gameObject); button.targetGraphic = image; Size(rect.gameObject, height);
            var label = Text("Label", rect, text, 30, height); Stretch(label.rectTransform, 12); label.alignment = TextAlignmentOptions.Center; label.fontStyle = FontStyles.Bold; label.enableAutoSizing = true; label.fontSizeMin = 23; label.fontSizeMax = 32;
            PaintButton(button, primary); return button;
        }
        private static Transform Panel(string name, Transform parent) { var rect = Rect(name, parent); var image = Get<Image>(rect.gameObject); image.sprite = rounded; image.type = Image.Type.Sliced; image.color = theme.card; return rect; }
        private static TMP_Text Text(string name, Transform parent, string value, float font, float height)
        { var rect = Rect(name, parent); var text = Get<TextMeshProUGUI>(rect.gameObject); text.font = theme.font; text.raycastTarget = false; Label(text, value, font, theme.ink, height); return text; }
        private static void Label(TMP_Text text, string value, float font, Color color, float minimumHeight)
        { if (!text) return; text.text = value; text.font = theme.font; text.fontSize = font; text.enableAutoSizing = false; text.color = color; text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Overflow; Size(text.gameObject, minimumHeight, false); }
        private static void ButtonLabel(Button button, string value, float font) { var label = button.GetComponentInChildren<TMP_Text>(true); if (label) { label.text = value; label.fontSize = font; label.fontSizeMax = font; label.fontSizeMin = Mathf.Min(24, font); label.enableAutoSizing = true; } }
        private static RectTransform Rect(string name, Transform parent)
        { var existing = parent.Find(name); if (existing) return (RectTransform)existing; var go = new GameObject(name, typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go, "Create compact UI object"); go.transform.SetParent(parent, false); return (RectTransform)go.transform; }
        private static T Get<T>(GameObject go) where T : Component { return go.GetComponent<T>() ?? Undo.AddComponent<T>(go); }
        private static void Move(Transform child, Transform parent) { if (child && parent && child.parent != parent) { Undo.SetTransformParent(child, parent, "Move saved UI control"); child.localScale = Vector3.one; } }
        private static Transform Find(Transform root, string name) { return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name); }
        private static void Hide(Transform root, string name) { var target = Find(root, name); if (target) target.gameObject.SetActive(false); }
        private static void HideAll(Transform root, string name) { foreach (var target in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name)) target.gameObject.SetActive(false); }
        private static void Size(GameObject go, float height, bool fixedHeight = true)
        { var element = Get<LayoutElement>(go); element.minWidth = 0; element.preferredWidth = -1; element.minHeight = height; element.preferredHeight = fixedHeight ? height : -1; element.flexibleHeight = 0; }
        private static void V(Transform transform, float spacing, int padding)
        { var layout = Get<VerticalLayoutGroup>(transform.gameObject); layout.spacing = spacing; layout.padding = new RectOffset(padding, padding, padding, padding); layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false; }
        private static void H(Transform transform, float spacing)
        { var layout = Get<HorizontalLayoutGroup>(transform.gameObject); layout.spacing = spacing; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false; }
        private static void Stretch(RectTransform rect, float inset)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset; var element = rect.GetComponent<LayoutElement>(); if (element) element.ignoreLayout = true; }
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var result); return result; }
    }
}

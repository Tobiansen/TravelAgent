using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TravelAgent.Editor
{
    /// <summary>Explicit first-time authoring tool. Never runs on import/reload or overwrites existing scenes.</summary>
    public static class AppSceneBuilder
    {
        public const string ScenePath = "Assets/_App/Scenes/App.unity";
        private static AppTheme theme;
        private static Sprite rounded;

        [MenuItem("Tools/Travel Agent/Create application assets (first time)")]
        public static void Generate()
        {
            if (File.Exists(ScenePath)) throw new InvalidOperationException("App.unity already exists. Edit the saved scene; this tool preserves it.");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save your open scene before creating application assets.");
            EnsureFolders();
            if (!AssetDatabase.FindAssets("t:TMP_FontAsset").Any())
            {
                string package = Directory.GetDirectories("Library/PackageCache", "com.unity.ugui@*").First();
                UnityEditor.AssetPackage.Package.Import(Path.Combine(package, "Package Resources/TMP Essential Resources.unitypackage"), false);
            }
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (!font) throw new InvalidOperationException("TextMeshPro essential resources are importing. Run generation again once import completes.");
            theme = LoadOrCreate<AppTheme>("Assets/_App/Configuration/Theme.asset"); theme.font = font;
            rounded = CreateRoundedSprite();
            var config = LoadOrCreate<AppConfiguration>("Assets/_App/Configuration/AppConfiguration.asset");
            config.beforeSample = CreateSample("before", false); config.afterSample = CreateSample("after", true);
            EditorUtility.SetDirty(theme); EditorUtility.SetDirty(config);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera"; camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor; camera.GetComponent<Camera>().backgroundColor = theme.background;
            var root = new GameObject("AppRoot");
            var app = root.AddComponent<AppComposition>(); app.configuration = config; app.theme = theme;
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
            var background = Panel("Background", canvas.transform, theme.background); Stretch(background);
            var safe = Object("SafeArea", canvas.transform); Stretch(safe); safe.AddComponent<SafeArea>();
            var shell = Object("AppShell", safe.transform); shell.AddComponent<ResponsiveShell>(); Stretch(shell);
            var layout = Vertical(shell, 18, 30); layout.childForceExpandHeight = false;
            var view = root.AddComponent<AppView>(); app.view = view;
            Header(shell.transform, view);
            var toastPanel = Panel("StatusMessage", shell.transform, new Color32(225, 235, 227, 255)); Height(toastPanel, 90); Vertical(toastPanel, 0, 20);
            view.toast = Text("StatusMessageLabel", toastPanel.transform, "Connect your bag to begin.", 26, theme.ink, 54);
            var container = Object("ScreenContainer", shell.transform); container.AddComponent<LayoutElement>().flexibleHeight = 1;
            var selector = container.AddComponent<ScreenSelector>(); view.selector = selector;
            var screens = new GameObject[4];
            for (int i = 0; i < 4; i++) screens[i] = Screen(new[] { "GuardScreen", "JourneyScreen", "HistoryScreen", "DeviceScreen" }[i], container.transform);
            selector.screens = screens;
            var row = CreateRowPrefab(); view.rowPrefab = row;
            Guard(Content(screens[0]), view); Journey(Content(screens[1]), view); History(Content(screens[2]), view); Device(Content(screens[3]), view);
            view.demoToggle = Button("DemoToggle", shell.transform, "PRESENTER CONTROLS  ·  simulated scenarios", false, 58);
            Demo(shell.transform, view);
            var nav = Object("BottomNavigation", shell.transform); Height(nav, 110); Horizontal(nav, 10);
            view.navigation = new[] { "Guard", "Journey", "History", "Device" }.Select(label => Button(label + "Tab", nav.transform, label, false, 96)).ToArray();
            Details(safe.transform, view);
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            selector.Show(config.startupScreen);
            PlayerSettings.companyName = "Travel Agent"; PlayerSettings.productName = "Travel Agent";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.travelagent.companion");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath)).ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            UpgradeCurrentScene();
            Selection.activeGameObject = root;
            Debug.Log("Travel Agent: saved editable App.unity, theme, configuration, row and presenter prefabs.");
        }

        private static void Header(Transform parent, AppView v)
        {
            var header = Object("Header", parent); Vertical(header, 12); Height(header, 190);
            var line = Object("BrandAndConnection", header.transform); Height(line, 42); Horizontal(line, 12);
            Text("Brand", line.transform, "TRAVEL / AGENT", 27, theme.accent, 40).fontStyle = FontStyles.Bold;
            v.providerLabel = Text("ProviderLabel", line.transform, "DEMO DEVICE", 22, theme.muted, 40);
            v.connectionLabel = Text("ConnectionLabel", line.transform, "Disconnected", 22, theme.muted, 40);
            v.pageTitle = Text("PageTitle", header.transform, "Your device", 64, theme.ink, 86); v.pageTitle.fontStyle = FontStyles.Bold;
            Text("Tagline", header.transform, "A little more peace of mind, wherever your bag goes.", 27, theme.muted, 40);
        }

        private static void Device(Transform content, AppView v)
        {
            var connect = Card("ConnectionCard", content);
            Eyebrow(connect, "01 / WIRELESS CONNECTION");
            Text("ConnectionHeading", connect, "Your bag. Your controls.", 46, theme.ink, 65).fontStyle = FontStyles.Bold;
            v.deviceStatus = Text("DeviceStatus", connect, "Choose a provider, then find your bag.", 29, theme.muted, 100);
            var providers = Row("ProviderChoices", connect);
            v.useBluetooth = Button("UseBluetooth", providers, "Use Bluetooth", true);
            v.useDemo = Button("UseDemo", providers, "Use demo board", false);
            var actions = Row("ConnectionActions", connect);
            v.scan = Button("ScanButton", actions, "Find nearby bag", true);
            v.disconnect = Button("DisconnectButton", actions, "Disconnect", false);
            v.discoveredDevices = List("DiscoveredDevices", connect);
            v.authorizePhone = Button("AuthorizePhone", connect, "Authorize this phone", false);
            Text("OwnerHelp", connect, "For a real bag, open its physical pairing window first. Settings become editable after the board authorizes this phone.", 26, theme.muted, 88);
            var codes = Card("SequenceCard", content);
            Eyebrow(codes, "02 / FOUR-PRESS CONTROLS");
            Text("SequenceHeading", codes, "Make the sequences yours.", 43, theme.ink, 64).fontStyle = FontStyles.Bold;
            Text("SequenceHelp", codes, "Use exactly four A/B presses for each sequence. They must differ. Connect and disarm the bag before saving.", 28, theme.muted, 95);
            v.configStatus = Text("ConfirmedConfiguration", codes, "Connect to read confirmed sequences.", 28, theme.accent, 85);
            var arm = Row("ArmSequenceRow", codes);
            v.armCode = Input("ArmSequence", arm, "Arm sequence", "AABB", false, 4);
            v.confirmArmCode = Input("ConfirmArmSequence", arm, "Repeat arm", "Enter again", false, 4);
            var disarm = Row("DisarmSequenceRow", codes);
            v.disarmCode = Input("DisarmSequence", disarm, "Disarm sequence", "BABA", false, 4);
            v.confirmDisarmCode = Input("ConfirmDisarmSequence", disarm, "Repeat disarm", "Enter again", false, 4);
            v.saveSequences = Button("SaveSequencesButton", codes, "Save sequences on bag", true);
            v.clearSequences = Button("ClearSequencesButton", codes, "Clear entries", false);
            v.codeProgress = Text("PhysicalInputProgress", codes, "Physical input   ○○○○", 32, theme.muted, 60);
            var feedback = Card("FeedbackCard", content);
            Eyebrow(feedback, "03 / DEVICE FEEDBACK");
            v.testFeedback = Button("TestFeedbackButton", feedback, "Test lights and sound", false);
            Text("MotionProfile", feedback, "Current guard profile\n5 s settling + 1 s quiet · 2 s warning\nMotion: 0.12 g baseline departure or 20°/s rotation for 150 ms. Gentle rolling may be missed.", 27, theme.muted, 160);
            Text("MotorLimit", feedback, "Motor command state is shown separately from guard state. It does not measure rotation or confirm dispensing.", 26, theme.muted, 90);
        }

        private static void Guard(Transform content, AppView v)
        {
            var hero = Card("GuardStatusCard", content);
            Eyebrow(hero, "STATIONARY GUARD / MY SUITCASE");
            var icon = Object("SuitcaseIllustration", hero); Height(icon, 210);
            var body = Panel("BagBody", icon.transform, theme.accent); Rect(body, new Vector2(.5f,.5f), new Vector2(210,160), Vector2.zero);
            var handle = Panel("BagHandle", icon.transform, theme.accent); Rect(handle, new Vector2(.5f,.5f), new Vector2(90,22), new Vector2(0,92));
            for (int i=-1;i<=1;i++) { var rib = Panel("Rib"+i, body.transform, new Color32(85,154,135,255)); Rect(rib, new Vector2(.5f,.5f), new Vector2(8,100), new Vector2(i*48,0)); }
            v.guardState = Text("GuardStateLabel", hero, "Disarmed", 65, theme.ink, 95); v.guardState.fontStyle = FontStyles.Bold;
            v.guardDetail = Text("GuardDetail", hero, "Connect your bag to read its confirmed state.", 30, theme.muted, 135);
            v.guardAction = Button("GuardActionButton", hero, "Arm stationary guard", true, 100); v.guardActionLabel = v.guardAction.GetComponentInChildren<TMP_Text>();
            v.guardCodes = Text("ConfirmedCodes", hero, "Connect to read the physical sequences.", 29, theme.muted, 70);
            v.recordingLabel = Text("RecordingAndMotorState", hero, "Recording: Idle\nMotor command: Idle", 28, theme.muted, 85);
            var recent = Card("RecentActivityCard", content); Eyebrow(recent, "RECENT ACTIVITY");
            v.recentEvents = List("RecentEvents", recent);
            Text("GuardFootnote", content, "Connection lost? The bag continues its own confirmed guard behavior. The app shows the last contact rather than assuming protection.", 26, theme.muted, 105);
        }

        private static void Journey(Transform content, AppView v)
        {
            var trip = Card("JourneyDetailsCard", content); Eyebrow(trip, "01 / PREPARE");
            Text("JourneyHeading", trip, "Keep the facts together.", 44, theme.ink, 65).fontStyle = FontStyles.Bold;
            v.journeyStatus = Text("JourneyStatus", trip, "Prepare your bag before handover.", 28, theme.accent, 80);
            var airlineRow = Row("AirlineAndFlight", trip);
            v.airline = Input("AirlineInput", airlineRow, "Operating airline", "Finnair");
            v.flight = Input("FlightInput", airlineRow, "Flight", "AY 123");
            v.route = Input("RouteInput", trip, "Route", "Helsinki → destination");
            var context = Row("CountryAndCollection", trip);
            v.country = Input("CountryInput", context, "Relevant country", "Finland");
            v.collectionDate = Input("CollectionDateInput", context, "Collection date", "YYYY-MM-DD");
            v.bagDescription = Input("BagDescriptionInput", trip, "Bag description", "Color, model, identifying features");
            v.baggageReference = Input("BaggageReferenceInput", trip, "Baggage reference (optional)", "From your baggage receipt");
            var before = Card("BeforeHandoverCard", content); Eyebrow(before, "02 / BEFORE HANDOVER");
            Photo(before, "Before", out v.beforePhoto, out v.beforePhotoStatus, out v.beforeSample, out v.beforeImport);
            v.startTransit = Button("StartTransitButton", before, "Start quiet transit recording", true);
            Text("QuietModeExplanation", before, "Starting transit disarms stationary guarding and keeps siren and dispensing quiet. Recording continues independently of disarming.", 27, theme.muted, 100);
            v.handover = Button("RecordHandoverButton", before, "Record handover", false);
            var after = Card("CollectionCard", content); Eyebrow(after, "03 / COLLECT & INSPECT");
            v.collection = Button("RecordCollectionButton", after, "Record collection", true);
            Photo(after, "After", out v.afterPhoto, out v.afterPhotoStatus, out v.afterSample, out v.afterImport);
            Text("InspectionQuestion", after, "Did you find damage?", 38, theme.ink, 65).fontStyle = FontStyles.Bold;
            Text("InspectionHelp", after, "Inspect the bag yourself. A handling event alone does not establish damage.", 27, theme.muted, 80);
            var inspect = Row("InspectionChoices", after);
            v.damageNo = Button("NoDamageButton", inspect, "No damage", false);
            v.damageYes = Button("DamageFoundButton", inspect, "I found damage", true);
            var claim = Card("ClaimPreparation", content); v.claimPanel = claim.gameObject;
            Eyebrow(claim, "04 / PREPARE YOUR REPORT");
            v.damageDescription = Input("DamageDescriptionInput", claim, "What did you observe?", "Describe only damage you personally found", true);
            v.guidanceProvider = Text("GuidanceProvider", claim, "FIXTURE GUIDANCE", 25, theme.accent, 52);
            v.toggleGuidance = Button("ToggleGuidanceProvider", claim, "Switch fixture / live Exa", false);
            v.backendUrl = Input("BackendUrlInput", claim, "Laptop service address", "http://127.0.0.1:8787");
            v.backendToken = Input("BackendTokenInput", claim, "Service access token (if configured)", "Kept only for this session"); v.backendToken.contentType = TMP_InputField.ContentType.Password;
            v.research = Button("ResearchGuidanceButton", claim, "Research airline requirements", true);
            v.guidanceStatus = Text("GuidanceStatus", claim, "Research current official requirements.", 28, theme.muted, 125);
            v.guidanceItems = List("GuidanceItems", claim);
            v.claimDraft = Input("ClaimDraftInput", claim, "Your editable draft", "A factual draft appears after research", true, 0, 400);
            v.regenerateDraft = Button("RegenerateDraftButton", claim, "Rebuild draft from current facts", false);
            var exports = Row("DraftActions", claim);
            v.copyDraft = Button("CopyDraftButton", exports, "Copy draft", false);
            v.export = Button("ExportReportButton", exports, "Export evidence", false);
            v.openForm = Button("OpenReportingPageButton", claim, "Open official reporting page", true);
            v.claimStatus = Text("ClaimStatus", claim, "Nothing has been submitted.", 27, theme.muted, 100);
            v.submissionReference = Input("SubmissionReferenceInput", claim, "Reference received from the airline", "Only after you have submitted");
            v.confirmSubmission = Button("RecordSubmissionButton", claim, "Record my submission confirmation", false);
            Text("ReviewNotice", claim, "Review and submit through the airline’s official channel yourself. The app never sends a claim automatically.", 27, theme.muted, 90);
        }

        private static void History(Transform content, AppView v)
        {
            var card = Card("HistoryCard", content); Eyebrow(card, "YOUR BAG / EVENT RECORD");
            Text("HistoryHeading", card, "A clearer handling story.", 45, theme.ink, 70).fontStyle = FontStyles.Bold;
            v.historyStatus = Text("HistoryStatus", card, "No events yet.", 29, theme.muted, 100);
            Text("HistoryHelp", card, "Each event keeps its original source, time and quality. Gaps stay visible. Motion does not prove damage or responsibility.", 27, theme.muted, 115);
            v.historyEvents = List("EventTimeline", card);
        }
        private static void Photo(Transform parent, string name, out Image image, out TMP_Text label, out Button sample, out Button import)
        {
            var frame = Panel(name + "ImageFrame", parent, new Color32(234,239,234,255)); Height(frame, 280);
            var go = Object(name + "ConditionImage", frame.transform); Stretch(go, 10); image = go.AddComponent<Image>(); image.preserveAspect = true; image.raycastTarget = false;
            label = Text(name + "EvidenceOrigin", parent, "No image yet", 24, theme.muted, 55);
            var actions = Row(name + "PhotoActions", parent);
            sample = Button(name + "SampleButton", actions, "Use sample illustration", false);
            import = Button(name + "ImportButton", actions, "Import image", false);
        }
        private static void Demo(Transform parent, AppView v)
        {
            var panel = Panel("DemoControls", parent, new Color32(222,233,224,255)); Height(panel, 290);
            var grid = panel.AddComponent<GridLayoutGroup>(); grid.padding = new RectOffset(12,12,12,12); grid.spacing = new Vector2(10,10); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 4; grid.cellSize = new Vector2(235,78);
            v.pressA = Button("PressA", panel.transform, "Press A", false); v.pressB = Button("PressB", panel.transform, "Press B", false);
            v.bump = Button("Bump", panel.transform, "Brief bump", false); v.movement = Button("Movement", panel.transform, "Movement", false);
            v.rolling = Button("SlowRolling", panel.transform, "Slow rolling", false); v.impact = Button("Impact", panel.transform, "Impact", false);
            v.lid = Button("Lid", panel.transform, "Opening", false); v.sensorFault = Button("SensorFault", panel.transform, "Sensor fault", false);
            v.failure = Button("CommandFailure", panel.transform, "Fail command", false); v.failureLabel = v.failure.GetComponentInChildren<TMP_Text>();
            v.reconnect = Button("Reconnect", panel.transform, "Reconnect", false); v.reset = Button("ResetDemo", panel.transform, "Reset demo", false);
            v.backendFailure = Button("BackendFailure", panel.transform, "Backend failure", false);
            string path = "Assets/_App/Prefabs/DemoControls.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(panel, path, InteractionMode.AutomatedAction);
            v.demoPanel = panel; panel.SetActive(false);
        }
        private static void Details(Transform parent, AppView v)
        {
            var overlay = Panel("EventDetailOverlay", parent, new Color(0.04f,.10f,.09f,.92f)); Stretch(overlay);
            var card = Panel("EventDetailCard", overlay.transform, theme.card);
            var rect = (RectTransform)card.transform; rect.anchorMin = new Vector2(.06f,.12f); rect.anchorMax = new Vector2(.94f,.88f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            Vertical(card, 22, 35);
            v.detailTitle = Text("DetailTitle", card.transform, "Event details", 44, theme.ink, 80);
            v.detailBody = Text("DetailBody", card.transform, "Source, time and original measurements", 29, theme.muted, 700); v.detailBody.GetComponent<LayoutElement>().flexibleHeight = 1;
            v.closeDetail = Button("CloseDetail", card.transform, "Close details", true);
            v.detailPanel = overlay; overlay.SetActive(false);
        }
        private static ListRowView CreateRowPrefab()
        {
            var go = Panel("TimelineRow", null, new Color32(239,244,239,255));
            Height(go, 190); Vertical(go, 8, 20);
            var row = go.AddComponent<ListRowView>();
            row.title = Text("Title", go.transform, "Event title", 30, theme.ink, 44); row.title.fontStyle = FontStyles.Bold;
            row.detail = Text("Detail", go.transform, "Time · origin\nEvent details", 25, theme.muted, 100);
            row.action = go.AddComponent<Button>(); row.action.targetGraphic = go.GetComponent<Image>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/_App/Prefabs/Components/TimelineRow.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab.GetComponent<ListRowView>();
        }

        private static GameObject Screen(string name, Transform parent)
        {
            var screen = Object(name, parent); Stretch(screen);
            var scroll = screen.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Object("Viewport", screen.transform); Stretch(viewport); viewport.AddComponent<RectMask2D>();
            var content = Object("Content", viewport.transform); var rect = (RectTransform)content.transform;
            rect.anchorMin = new Vector2(0,1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f,1); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero;
            Vertical(content, 24, 2); var fitter = content.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = (RectTransform)viewport.transform; scroll.content = rect;
            return screen;
        }
        private static Transform Content(GameObject screen) => screen.transform.Find("Viewport/Content");
        private static RectTransform List(string name, Transform parent) { var go = Object(name, parent); Vertical(go, 14); var fit = go.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize; return (RectTransform)go.transform; }
        private static Transform Card(string name, Transform parent) { var card = Panel(name,parent,theme.card); Vertical(card,20,30); return card.transform; }
        private static Transform Row(string name, Transform parent) { var row = Object(name,parent); Horizontal(row,16); return row.transform; }
        private static void Eyebrow(Transform parent,string text) => Text("SectionLabel",parent,text,23,theme.accent,40).fontStyle=FontStyles.Bold;
        private static GameObject Object(string name,Transform parent) { var go=new GameObject(name,typeof(RectTransform)); if(parent)go.transform.SetParent(parent,false); return go; }
        private static GameObject Panel(string name,Transform parent,Color color) { var go=Object(name,parent); var img=go.AddComponent<Image>();img.color=color;img.sprite=rounded;img.type=Image.Type.Sliced;return go; }
        private static void Stretch(GameObject go,float inset=0) { var r=(RectTransform)go.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=Vector2.one*inset;r.offsetMax=Vector2.one*-inset; }
        private static void Rect(GameObject go,Vector2 anchor,Vector2 size,Vector2 position) { var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=anchor;r.sizeDelta=size;r.anchoredPosition=position; }
        private static void Height(GameObject go,float height) { var le=go.GetComponent<LayoutElement>()??go.AddComponent<LayoutElement>();le.minHeight=height;le.preferredHeight=height; }
        private static VerticalLayoutGroup Vertical(GameObject go,float spacing,float padding=0) { var l=go.AddComponent<VerticalLayoutGroup>();l.spacing=spacing;l.padding=new RectOffset((int)padding,(int)padding,(int)padding,(int)padding);l.childControlHeight=true;l.childControlWidth=true;l.childForceExpandHeight=false;l.childForceExpandWidth=true;return l; }
        private static void Horizontal(GameObject go,float spacing) {var l=go.AddComponent<HorizontalLayoutGroup>();l.spacing=spacing;l.childControlWidth=true;l.childControlHeight=true;l.childForceExpandWidth=true;l.childForceExpandHeight=false;}
        private static TMP_Text Text(string name,Transform parent,string text,float size,Color color,float height)
        {
            var go=Object(name,parent);Height(go,height);var t=go.AddComponent<TextMeshProUGUI>();t.font=theme.font;t.text=text;t.fontSize=size;t.color=color;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Truncate;t.raycastTarget=false;t.richText=false;return t;
        }
        private static Button Button(string name,Transform parent,string text,bool primary,float height=86)
        {
            var go=Panel(name,parent,primary?theme.accent:new Color32(233,240,233,255));Height(go,height);var b=go.AddComponent<Button>();b.targetGraphic=go.GetComponent<Image>();
            var label=Text("Label",go.transform,text,29,primary?Color.white:theme.ink,height);Stretch(label.gameObject,12);label.alignment=TextAlignmentOptions.Center;label.fontStyle=FontStyles.Bold;label.fontSizeMax=29;label.fontSizeMin=22;label.enableAutoSizing=true;
            var colors=b.colors;colors.disabledColor=new Color(.73f,.77f,.73f,.65f);colors.highlightedColor=new Color(.91f,.96f,.93f);b.colors=colors;return b;
        }
        private static TMP_InputField Input(string name,Transform parent,string title,string placeholder,bool multiline=false,int limit=0,float height=150)
        {
            var container=Object(name+"Field",parent);Vertical(container,8);Height(container,multiline?height+58:145);
            Text("FieldLabel",container.transform,title,25,theme.muted,36);
            var box=Panel(name,container.transform,new Color32(237,242,237,255));Height(box,multiline?height:94);var field=box.AddComponent<TMP_InputField>();field.characterLimit=limit;
            var viewport=Object("TextArea",box.transform);Stretch(viewport,18);viewport.AddComponent<RectMask2D>();
            var text=Text("Text",viewport.transform,"",31,theme.ink,60);Stretch(text.gameObject);
            var hint=Text("Placeholder",viewport.transform,placeholder,28,theme.muted,60);Stretch(hint.gameObject);
            field.textViewport=(RectTransform)viewport.transform;field.textComponent=(TextMeshProUGUI)text;field.placeholder=hint;field.lineType=multiline?TMP_InputField.LineType.MultiLineNewline:TMP_InputField.LineType.SingleLine;field.selectionColor=new Color(.2f,.6f,.45f,.25f);
            return field;
        }
        private static T LoadOrCreate<T>(string path) where T:ScriptableObject {var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(!asset){asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,path);}return asset;}
        private static void EnsureFolders() {foreach(var folder in new[]{"Assets/_App/Scenes","Assets/_App/Configuration","Assets/_App/Art/Images","Assets/_App/Prefabs/Components"})Directory.CreateDirectory(folder);AssetDatabase.Refresh();}
        private static Sprite CreateRoundedSprite()
        {
            const string path="Assets/_App/Art/Images/RoundedPanel.png";var texture=new Texture2D(64,64,TextureFormat.RGBA32,false);
            for(int y=0;y<64;y++)for(int x=0;x<64;x++){float dx=Mathf.Max(12-x,x-51),dy=Mathf.Max(12-y,y-51);float distance=new Vector2(Mathf.Max(0,dx),Mathf.Max(0,dy)).magnitude;texture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(12.5f-distance)));}
            texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spriteBorder=new Vector4(14,14,14,14);importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        private static Sprite CreateSample(string name,bool damaged)
        {
            string path="Assets/_App/Art/Images/fixture-"+name+".png";var t=new Texture2D(720,480,TextureFormat.RGBA32,false);
            for(int y=0;y<480;y++)for(int x=0;x<720;x++){Color c=new Color32(224,233,220,255);if(y<90)c=new Color32(198,211,195,255);if(x>255&&x<465&&y>95&&y<365)c=new Color32(39,101,87,255);if(x>310&&x<410&&y>=365&&y<390)c=new Color32(22,55,48,255);if((x>270&&x<300||x>420&&x<450)&&y>70&&y<105)c=new Color32(22,55,48,255);if(x>265&&x<455&&y>110&&y<350&&(x-265)%45<5)c=new Color32(85,142,122,255);if(damaged&&x>390&&x<460&&y>290&&y<340&&Mathf.Abs((x-390)-(y-290))<7)c=new Color32(216,140,73,255);t.SetPixel(x,y,c);}
            t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.isReadable=true;importer.mipmapEnabled=false;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        [MenuItem("Tools/Travel Agent/Apply additive authoring updates")]
        public static void UpgradeCurrentScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before editing saved application assets.");
            var view = UnityEngine.Object.FindAnyObjectByType<AppView>();
            if (!view) throw new InvalidOperationException("Open App.unity first.");
            theme=AssetDatabase.LoadAssetAtPath<AppTheme>("Assets/_App/Configuration/Theme.asset");
            if(!theme.font || theme.font.name=="LiberationSans SDF - Fallback")theme.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            const string panelPath="Assets/_App/Art/Images/RoundedPanel.png";
            var panelImporter=(TextureImporter)AssetImporter.GetAtPath(panelPath);
            if(panelImporter.spriteImportMode!=SpriteImportMode.Single || panelImporter.textureCompression!=TextureImporterCompression.Uncompressed)
            {panelImporter.spriteImportMode=SpriteImportMode.Single;panelImporter.spriteBorder=new Vector4(14,14,14,14);panelImporter.textureCompression=TextureImporterCompression.Uncompressed;panelImporter.SaveAndReimport();}
            rounded=AssetDatabase.LoadAssetAtPath<Sprite>(panelPath);
            foreach(var samplePath in new[]{"Assets/_App/Art/Images/fixture-before.png","Assets/_App/Art/Images/fixture-after.png"})
            {
                var sampleImporter=(TextureImporter)AssetImporter.GetAtPath(samplePath);
                if(sampleImporter.textureCompression!=TextureImporterCompression.Uncompressed || !sampleImporter.isReadable){sampleImporter.textureCompression=TextureImporterCompression.Uncompressed;sampleImporter.isReadable=true;sampleImporter.SaveAndReimport();}
            }
            EditorUtility.SetDirty(theme);
            foreach(var text in view.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TMP_Text>(true)))
                if(!text.font || text.font.name.StartsWith("LiberationSans")){text.font=theme.font;text.overflowMode=TextOverflowModes.Truncate;EditorUtility.SetDirty(text);}
            foreach(var image in view.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Image>(true)))
                if(image.type==Image.Type.Sliced && (!image.sprite || AssetDatabase.GetAssetPath(image.sprite)==panelPath)){image.sprite=rounded;EditorUtility.SetDirty(image);}
            foreach(var path in new[]{"Assets/_App/Prefabs/Components/TimelineRow.prefab","Assets/_App/Prefabs/DemoControls.prefab"})
            {
                var prefab=PrefabUtility.LoadPrefabContents(path);
                try {
                    foreach(var text in prefab.GetComponentsInChildren<TMP_Text>(true))if(!text.font || text.font.name.StartsWith("LiberationSans")){text.font=theme.font;text.overflowMode=TextOverflowModes.Truncate;}
                    foreach(var image in prefab.GetComponentsInChildren<Image>(true))if(image.type==Image.Type.Sliced && (!image.sprite || AssetDatabase.GetAssetPath(image.sprite)==panelPath))image.sprite=rounded;
                    PrefabUtility.SaveAsPrefabAsset(prefab,path);
                }
                finally {PrefabUtility.UnloadPrefabContents(prefab);}
            }
            if(!view.regenerateDraft)
            {
                var field=view.claimDraft.transform.parent;
                view.regenerateDraft=Button("RegenerateDraftButton",field.parent,"Rebuild draft from current facts",false);
                view.regenerateDraft.transform.SetSiblingIndex(field.GetSiblingIndex()+1);
            }
            if(!view.backendFailure)
            {
                var old=view.demoPanel.transform.Find("ScenarioOrigin");if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);
                view.backendFailure=Button("BackendFailure",view.demoPanel.transform,"Backend failure",false);
            }
            if(!view.clearSequences)
            {
                view.clearSequences=Button("ClearSequencesButton",view.saveSequences.transform.parent,"Clear entries",false);
                view.clearSequences.transform.SetSiblingIndex(view.saveSequences.transform.GetSiblingIndex()+1);
            }
            var adaptive=view.GetComponent<CompactLandscapeLayout>()??view.gameObject.AddComponent<CompactLandscapeLayout>();adaptive.view=view;EditorUtility.SetDirty(adaptive);
            if(theme.layoutRevision<2)
            {
                foreach(var root in view.gameObject.scene.GetRootGameObjects())IncreaseReadability(root);
                foreach(var path in new[]{"Assets/_App/Prefabs/Components/TimelineRow.prefab","Assets/_App/Prefabs/DemoControls.prefab"})
                {
                    var prefab=PrefabUtility.LoadPrefabContents(path);
                    try {IncreaseReadability(prefab);PrefabUtility.SaveAsPrefabAsset(prefab,path);}
                    finally {PrefabUtility.UnloadPrefabContents(prefab);}
                }
                theme.layoutRevision=2;EditorUtility.SetDirty(theme);
            }
            ConfigureDetailScroll(view);
            foreach(var root in view.gameObject.scene.GetRootGameObjects())AllowParagraphGrowth(root);
            foreach(var path in new[]{"Assets/_App/Prefabs/Components/TimelineRow.prefab","Assets/_App/Prefabs/DemoControls.prefab"})
            {
                var prefab=PrefabUtility.LoadPrefabContents(path);
                try {AllowParagraphGrowth(prefab);PrefabUtility.SaveAsPrefabAsset(prefab,path);}
                finally {PrefabUtility.UnloadPrefabContents(prefab);}
            }
            EditorUtility.SetDirty(view);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(view.gameObject.scene);
            var sceneGuid=new GUID(AssetDatabase.AssetPathToGUID(ScenePath));
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(sceneGuid,true)}.Concat(EditorBuildSettings.scenes.Where(s=>s.path!=ScenePath)).ToArray();
            var tutorialSettings=AssetDatabase.LoadMainAssetAtPath("Assets/Welcome/TutorialProjectSettings.asset");
            if(tutorialSettings) {var serialized=new SerializedObject(tutorialSettings);var welcome=serialized.FindProperty("m_WelcomePage");if(welcome!=null){welcome.objectReferenceValue=null;serialized.ApplyModifiedProperties();AssetDatabase.SaveAssets();}}
        }
        private static void AllowParagraphGrowth(GameObject root)
        {
            foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var element=text.GetComponent<LayoutElement>();
                if(element && text.transform.parent.GetComponent<VerticalLayoutGroup>()){element.preferredHeight=-1;EditorUtility.SetDirty(element);}
            }
            foreach(var layout in root.GetComponentsInChildren<VerticalLayoutGroup>(true))
            {
                var element=layout.GetComponent<LayoutElement>();
                if(element && element.flexibleHeight<=0){element.preferredHeight=-1;EditorUtility.SetDirty(element);}
            }
        }
        private static void ConfigureDetailScroll(AppView view)
        {
            if(view.detailBody.transform.parent.name=="DetailViewport")return;
            var text=view.detailBody;var card=text.transform.parent;
            int position=text.transform.GetSiblingIndex();
            var area=Object("DetailScroll",card);area.transform.SetSiblingIndex(position);
            var flexible=area.AddComponent<LayoutElement>();flexible.minHeight=100;flexible.flexibleHeight=1;
            var scroll=area.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            var viewport=Object("DetailViewport",area.transform);Stretch(viewport);viewport.AddComponent<RectMask2D>();
            text.transform.SetParent(viewport.transform,false);
            var element=text.GetComponent<LayoutElement>();if(element)UnityEngine.Object.DestroyImmediate(element);
            var rect=(RectTransform)text.transform;rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=Vector2.zero;
            var fit=text.gameObject.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport=(RectTransform)viewport.transform;scroll.content=rect;
        }
        private static void IncreaseReadability(GameObject root)
        {
            foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if(text.fontSize<60)text.fontSize*=1.35f;
                if(text.enableAutoSizing){text.fontSizeMin*=1.35f;text.fontSizeMax*=1.35f;}
                if(text.name=="Tagline" && text.text=="A little more peace of mind, wherever your bag goes.")text.text="Your bag. Your controls. Wherever you go.";
                EditorUtility.SetDirty(text);
            }
            foreach(var element in root.GetComponentsInChildren<LayoutElement>(true))
            {
                if(element.minHeight>0)element.minHeight*=1.35f;
                if(element.preferredHeight>0)element.preferredHeight*=1.35f;
                if(element.GetComponent<Button>()){element.minHeight=Mathf.Max(144,element.minHeight);element.preferredHeight=Mathf.Max(144,element.preferredHeight);}
                EditorUtility.SetDirty(element);
            }
            foreach(var grid in root.GetComponentsInChildren<GridLayoutGroup>(true)){grid.cellSize=new Vector2(grid.cellSize.x,grid.cellSize.y*1.35f);EditorUtility.SetDirty(grid);}
        }
        [MenuItem("Tools/Travel Agent/Apply shared theme to saved scene")]
        public static void ApplyTheme()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before applying shared styling.");
            theme=AssetDatabase.LoadAssetAtPath<AppTheme>("Assets/_App/Configuration/Theme.asset");
            foreach(var graphic in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Graphic>(true)))
            {
                Undo.RecordObject(graphic,"Apply Travel Agent theme");
                if(graphic.color==theme.appliedAccent)graphic.color=theme.accent;
                else if(graphic.color==theme.appliedInk)graphic.color=theme.ink;
                else if(graphic.color==theme.appliedMuted)graphic.color=theme.muted;
                else if(graphic.color==theme.appliedBackground)graphic.color=theme.background;
                else if(graphic.color==theme.appliedCard)graphic.color=theme.card;
                else if(graphic.color==theme.appliedSecondary)graphic.color=theme.secondary;
                else if(graphic.color==theme.appliedInput)graphic.color=theme.input;
                else if(graphic.color==theme.appliedDisabled)graphic.color=theme.disabled;
                else if(graphic.color==theme.appliedBorder)graphic.color=theme.border;
                else if(graphic.color==theme.appliedOnAccent)graphic.color=theme.onAccent;
                if(graphic is TMP_Text text && text.font.name.StartsWith("LiberationSans"))text.font=theme.font;
            }
            theme.appliedAccent=theme.accent;theme.appliedInk=theme.ink;theme.appliedMuted=theme.muted;theme.appliedBackground=theme.background;theme.appliedCard=theme.card;
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach(var state in root.GetComponentsInChildren<DemoButtonState>(true)) {state.normalText=state.normalText==theme.appliedOnAccent?theme.onAccent:theme.ink;state.disabledText=theme.muted;EditorUtility.SetDirty(state);}
                foreach(var flow in root.GetComponentsInChildren<DemoJourneyFlow>(true)){flow.selectedColor=theme.accent;flow.otherColor=theme.secondary;EditorUtility.SetDirty(flow);}
                foreach(var graph in root.GetComponentsInChildren<DisturbanceTimeline>(true)){graph.SetPalette(theme.ink,theme.muted,theme.accent,theme.border);EditorUtility.SetDirty(graph);}
            }
            theme.appliedSecondary=theme.secondary;theme.appliedInput=theme.input;theme.appliedDisabled=theme.disabled;theme.appliedBorder=theme.border;theme.appliedOnAccent=theme.onAccent;
            EditorUtility.SetDirty(theme);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
        public static void PrepareSavedScene()
        {
            EditorSceneManager.OpenScene(ScenePath);
            UpgradeCurrentScene();
            EditorTaskBridge.Verify();
        }
        [MenuItem("Tools/Travel Agent/Preview/Guard")]private static void PreviewGuard()=>Preview(0);
        [MenuItem("Tools/Travel Agent/Preview/Journey")]private static void PreviewJourney()=>Preview(1);
        [MenuItem("Tools/Travel Agent/Preview/History")]private static void PreviewHistory()=>Preview(2);
        [MenuItem("Tools/Travel Agent/Preview/Device")]private static void PreviewDevice()=>Preview(3);
        private static void Preview(int index) {var selector=UnityEngine.Object.FindAnyObjectByType<ScreenSelector>();if(!selector)return;Undo.RecordObject(selector,"Preview Travel Agent screen");foreach(var screen in selector.screens)Undo.RecordObject(screen,"Preview screen");selector.Show(index);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());}
    }
}

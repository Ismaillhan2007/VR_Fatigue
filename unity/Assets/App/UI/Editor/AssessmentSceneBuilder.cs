using System;
using System.IO;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.UI.Panels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace App.UI.Editor
{
    /// <summary>
    /// Generates the placeholder panel prefabs and the Assessment scene from code, so no GUIDs are hand-written.
    /// Menu: App > Build Assessment Scene. Batch: -executeMethod App.UI.Editor.AssessmentSceneBuilder.BuildAll
    /// Overwrites Assets/App/UI/Prefabs/*.prefab and Assets/App/Scenes/Assessment.unity. Never touches ForestDemo.
    /// </summary>
    public static class AssessmentSceneBuilder
    {
        public const string PrefabDir = "Assets/App/UI/Prefabs";
        public const string ScenePath = "Assets/App/Scenes/Assessment.unity";

        static DefaultControls.Resources _res;
        static Font _font;

        // Canvas units: 1 unit is 1 mm on the Quest panel 2 m away (about 0.03 degrees). Body text of 40 units is
        // roughly 1.1 degrees per em, readable in the headset; the previous 20-25 units were not.
        public const int EyebrowSize = 28, TitleSize = 58, BodySize = 38, SmallSize = 30, ButtonSize = 38, NumberSize = 44;
        const float ButtonHeight = 92, ButtonWidth = 360;

        enum ButtonKind { Primary, Secondary, Accent }

        [MenuItem("App/Build Assessment Scene")]
        public static void BuildAll()
        {
            InitializeWidgets();
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

            var prefabs = new GameObject[]
            {
                Save(Consent()), Save(Baseline()), Save(LoadTask()), Save(CheckIn()), Save(Summary()),
                Save(RelaxationSetup()), Save(SceneView()), Save(ReRating()), Save(Done()),
            };
            var bar = Save(Bar());
            var pause = Save(Pause());
            var probe = Save(ProbeOverlay());
            BuildScene(prefabs, bar, pause, probe);
            AssetDatabase.SaveAssets();
            Debug.Log("[AssessmentSceneBuilder] prefabs and Assessment scene written");
        }

        static void InitializeWidgets()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _res = new DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };
            UiTheme.EnsureSprites();
        }

        [MenuItem("App/Update Backend Settings UI")]
        public static void BuildBackendSettings()
        {
            InitializeWidgets();
            var prefab = Save(Consent());
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var controller = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            var old = controller.Panels.OfType<ConsentPanel>().Single();
            var parent = old.transform.parent;
            var sibling = old.transform.GetSiblingIndex();
            var index = controller.Panels.IndexOf(old);
            UnityEngine.Object.DestroyImmediate(old.gameObject);
            var replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            replacement.transform.SetSiblingIndex(sibling);
            controller.Panels[index] = replacement.GetComponent<ConsentPanel>();
            EditorUtility.SetDirty(controller);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[AssessmentSceneBuilder] backend settings UI updated; other panels preserved");
        }

        // ----- panels -----

        static GameObject Consent()
        {
            var root = PanelRoot("ConsentPanel");
            var p = root.AddComponent<ConsentPanel>();
            Eyebrow(root.transform, "WELCOME");
            Title(root.transform, "Eye comfort check");
            p.Body = Body(root.transform, flexible: true);
            p.Body.fontSize = SmallSize + 4;
            var row = ButtonRow(root.transform);
            p.OpenSettingsButton = MakeButton(row, "Backend settings", ButtonKind.Secondary);
            p.StartButton = MakeButton(row, "I agree, start");

            var settings = PanelRoot("BackendSettings", Vector2.zero, Vector2.one);
            settings.transform.SetParent(root.transform, false);
            settings.AddComponent<LayoutElement>().ignoreLayout = true;
            settings.GetComponent<VerticalLayoutGroup>().spacing = 12;
            p.SettingsRoot = settings;
            Title(settings.transform, "Optional backend (session only)").fontSize = 44;
            Small(settings.transform, "Use HTTPS. The team demo token stays in memory and clears on Exit / New session. "
                + "Checking sends only an invented test request. Upload permission below is separate from eye capture and Gemini.", 76);
            p.BackendUrl = MakeInput(settings.transform, "HTTPS backend origin");
            p.BackendUrl.gameObject.name = "BackendUrl";
            p.BackendUrl.lineType = InputField.LineType.SingleLine;
            p.BackendUrl.characterLimit = 2048;
            p.BackendUrl.GetComponent<LayoutElement>().preferredHeight = 64;
            p.BackendToken = MakeInput(settings.transform, "Team demo token");
            p.BackendToken.gameObject.name = "BackendToken";
            p.BackendToken.lineType = InputField.LineType.SingleLine;
            p.BackendToken.contentType = InputField.ContentType.Password;
            p.BackendToken.characterLimit = 4096;
            p.BackendToken.shouldHideMobileInput = true;
            p.BackendToken.GetComponent<LayoutElement>().preferredHeight = 64;
            p.LocalDevelopment = MakeToggle(settings.transform, "Local development: allow loopback / private LAN HTTP");
            p.InventedDemo = MakeToggle(settings.transform, "This entire session uses invented eye data AND invented check-ins");
            p.AllowUpload = MakeToggle(settings.transform, "Allow this session's invented data, ratings and words to reach this server");
            p.ConnectionStatus = Small(settings.transform, "", flexible: true);
            var buttons = ButtonRow(settings.transform);
            p.CheckConnectionButton = MakeButton(buttons, "Check with invented data", ButtonKind.Secondary);
            p.SaveSettingsButton = MakeButton(buttons, "Save and back");
            settings.SetActive(false);
            return root;
        }

        static GameObject Baseline()
        {
            var root = PanelRoot("BaselinePanel");
            var p = root.AddComponent<BaselinePanel>();
            Eyebrow(root.transform, "STEP 1 OF 4  ·  BASELINE");
            p.Title = Title(root.transform, "Before we start");
            p.Status = Body(root.transform, height: 330);
            p.Countdown = Countdown(root.transform);
            p.RatingGroup = Group(root.transform, "Ratings");
            Legend(p.RatingGroup.transform);
            p.EyeStrain = Rating(p.RatingGroup.transform, "Eye strain");
            p.MentalFatigue = Rating(p.RatingGroup.transform, "Mental fatigue");
            Spacer(root.transform);
            var row = ButtonRow(root.transform);
            p.SkipButton = MakeButton(row, "Skip (desktop)", ButtonKind.Secondary);
            p.StartButton = MakeButton(row, "I'm ready, start");
            p.ContinueButton = MakeButton(row, "Continue");
            return root;
        }

        static GameObject LoadTask()
        {
            var root = PanelRoot("LoadTaskPanel");
            var p = root.AddComponent<LoadTaskPanel>();
            Eyebrow(root.transform, "STEP 2 OF 4  ·  TASK");
            p.Title = Title(root.transform, "The task");
            p.Status = Body(root.transform, height: 330);
            p.Countdown = Countdown(root.transform);
            Spacer(root.transform);
            var row = ButtonRow(root.transform);
            p.SkipButton = MakeButton(row, "Skip (desktop)", ButtonKind.Secondary);
            p.StartButton = MakeButton(row, "Start task");
            return root;
        }

        static GameObject CheckIn()
        {
            var root = PanelRoot("CheckInPanel");
            var p = root.AddComponent<CheckInPanel>();
            p.StepLabel = Eyebrow(root.transform, "STEP 3 OF 4  ·  CHECK-IN");

            var ratings = Group(root.transform, "Step 1: ratings");
            Title(ratings.transform, "How do you feel after the task?");
            Legend(ratings.transform);
            p.EyeStrain = Rating(ratings.transform, "Eye strain");
            p.MentalFatigue = Rating(ratings.transform, "Mental fatigue");
            p.Steps.Add(ratings);

            var feelings = Group(root.transform, "Step 2: feelings");
            Title(feelings.transform, "Anything else? Pick any that fit");
            Small(feelings.transform, "Optional. Up to 6.", 40);
            Small(feelings.transform, "I feel", 40).fontStyle = FontStyle.Bold;
            var feel = ChipGrid(feelings.transform, "I feel", 2);
            Small(feelings.transform, "I would like", 40).fontStyle = FontStyle.Bold;
            var like = ChipGrid(feelings.transform, "I would like", 1);
            foreach (Chip c in Enum.GetValues(typeof(Chip)))
            {
                var label = ReportText.ChipLabel(c);
                label = char.ToUpperInvariant(label[0]) + label.Substring(1);
                p.ChipToggles.Add(Pill(c >= Chip.WantQuiet ? like : feel, label, SmallSize + 2, 0, 0, layout: false));
            }
            p.Steps.Add(feelings);

            var words = Group(root.transform, "Step 3: own words");
            Title(words.transform, "In your own words (optional)");
            Body(words.transform, "Point at the box and press the trigger: the headset keyboard opens. "
                + "Press its microphone key to dictate, or type.", 100).fontSize = SmallSize + 2;
            p.FreeText = MakeInput(words.transform, "For example: my eyes feel dry and I want somewhere quiet");
            p.FreeText.GetComponent<LayoutElement>().preferredHeight = 140;
            p.Hint = Small(words.transform, "", 44);
            p.CloudExplanation = Pill(words.transform, "Optional: also send aggregate eye measurements and ratings to the configured "
                + "backend and Google Gemini for an experimental explanation (Google's Gemini terms apply). "
                + "Raw gaze and your words stay here.", 26, 0, 110, alignment: TextAnchor.MiddleLeft);
            p.CloudExplanation.gameObject.name = "Optional Gemini summary consent";
            p.Steps.Add(words);

            Spacer(root.transform);
            var row = ButtonRow(root.transform);
            p.BackButton = MakeButton(row, "Back", ButtonKind.Secondary);
            p.NextButton = MakeButton(row, "Next");
            p.SubmitButton = MakeButton(row, "View report");
            return root;
        }

        static GameObject Summary()
        {
            var root = PanelRoot("SummaryPanel");
            var p = root.AddComponent<SummaryPanel>();
            Eyebrow(root.transform, "STEP 4 OF 4  ·  SUMMARY");
            Title(root.transform, "Your experimental wellness summary").fontSize = 50;
            p.Body = Body(root.transform, flexible: true);
            BestFit(p.Body);
            var row = ButtonRow(root.transform);
            p.EnterSceneButton = MakeButton(row, "Try optional relaxation", ButtonKind.Secondary);
            p.FinishReportButton = MakeButton(row, "Finish with report");
            return root;
        }

        static GameObject RelaxationSetup()
        {
            var root = PanelRoot("RelaxationSetupPanel");
            var p = root.AddComponent<RelaxationSetupPanel>();
            Eyebrow(root.transform, "OPTIONAL  ·  RELAXATION");
            Title(root.transform, "A calm forest, if you like");
            p.Body = Body(root.transform, flexible: true);
            p.Body.fontSize = SmallSize + 2;
            p.ChooseSettings = MakeToggle(root.transform, "Choose settings myself");
            p.PreferenceGroup = new GameObject("Preferences", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            p.PreferenceGroup.transform.SetParent(root.transform, false);
            var g = p.PreferenceGroup.GetComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(360, 76);
            g.spacing = new Vector2(14, 14);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 4;
            p.PreferenceGroup.GetComponent<LayoutElement>().preferredHeight = 76;
            p.Animals = Pill(p.PreferenceGroup.transform, "Animals", SmallSize, 0, 0, layout: false);
            p.Silent = Pill(p.PreferenceGroup.transform, "Silent", SmallSize, 0, 0, layout: false);
            p.Sunset = Pill(p.PreferenceGroup.transform, "Sunset (off = day)", SmallSize, 0, 0, layout: false);
            p.GentleMotion = Pill(p.PreferenceGroup.transform, "Gentle motion", SmallSize, 0, 0, layout: false);
            var row = ButtonRow(root.transform);
            p.BackButton = MakeButton(row, "Back to report", ButtonKind.Secondary);
            p.EnterButton = MakeButton(row, "Enter optional forest");
            return root;
        }

        static GameObject SceneView()
        {
            var root = PanelRoot("ScenePanel", new Vector2(0.08f, 0.02f), new Vector2(0.92f, 0.3f));
            root.GetComponent<Image>().color = new Color(UiTheme.Card.r, UiTheme.Card.g, UiTheme.Card.b, 0.92f);
            var p = root.AddComponent<ScenePanel>();
            p.Background = root.GetComponent<Image>();
            p.Content = Group(root.transform, "Menu");
            Title(p.Content.transform, "Calming forest").fontSize = 44;
            p.Body = Small(p.Content.transform, "", flexible: true);
            p.Content.GetComponent<LayoutElement>().flexibleHeight = 1;
            var row = ButtonRow(p.Content.transform);
            p.HideButton = MakeButton(row, "Hide menu", ButtonKind.Secondary);
            p.MuteButton = MakeButton(row, "Mute", ButtonKind.Secondary);
            p.FinishButton = MakeButton(row, "Finish");

            // Collapsed: one small button at the bottom edge plus the data/AI labels, so the forest is unobstructed.
            p.ShowButton = MakeButton(root.transform, "Show menu", ButtonKind.Secondary);
            var show = (RectTransform)p.ShowButton.transform;
            p.ShowButton.GetComponent<LayoutElement>().ignoreLayout = true;
            show.anchorMin = show.anchorMax = new Vector2(0.5f, 0);
            show.pivot = new Vector2(0.5f, 0);
            show.sizeDelta = new Vector2(ButtonWidth, ButtonHeight - 12);
            show.anchoredPosition = new Vector2(0, 8);
            p.HiddenLabel = Small(root.transform, "");
            p.HiddenLabel.GetComponent<LayoutElement>().ignoreLayout = true;
            p.HiddenLabel.alignment = TextAnchor.LowerCenter;
            p.HiddenLabel.fontSize = 24;
            var hidden = (RectTransform)p.HiddenLabel.transform;
            hidden.anchorMin = new Vector2(0, 0);
            hidden.anchorMax = new Vector2(1, 0);
            hidden.pivot = new Vector2(0.5f, 0);
            hidden.sizeDelta = new Vector2(0, 36);
            hidden.anchoredPosition = new Vector2(0, ButtonHeight + 4);
            p.ShowButton.gameObject.SetActive(false);
            p.HiddenLabel.gameObject.SetActive(false);
            return root;
        }

        static GameObject ReRating()
        {
            var root = PanelRoot("ReRatingPanel");
            var p = root.AddComponent<ReRatingPanel>();
            Eyebrow(root.transform, "OPTIONAL  ·  FEEDBACK");
            Title(root.transform, "How do you feel now?");
            Legend(root.transform);
            p.EyeStrain = Rating(root.transform, "Eye strain");
            p.MentalFatigue = Rating(root.transform, "Mental fatigue");
            Spacer(root.transform);
            Small(root.transform, "Feedback is optional.", 44);
            var row = ButtonRow(root.transform);
            p.SkipButton = MakeButton(row, "Finish without feedback", ButtonKind.Secondary);
            p.SubmitButton = MakeButton(row, "Submit and finish");
            return root;
        }

        static GameObject Done()
        {
            var root = PanelRoot("DonePanel");
            var p = root.AddComponent<DonePanel>();
            Eyebrow(root.transform, "DONE");
            Title(root.transform, "Thank you");
            p.Body = Body(root.transform, flexible: true);
            BestFit(p.Body);
            p.RestartButton = MakeButton(ButtonRow(root.transform), "New session");
            return root;
        }

        static GameObject Bar()
        {
            var root = new GameObject("ProvenanceBar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            var rt = (RectTransform)root.transform;
            // 0.882-0.998 (125 units): three label rows plus padding are not clipped; panels end at 0.88.
            rt.anchorMin = new Vector2(0.08f, 0.882f);
            rt.anchorMax = new Vector2(0.92f, 0.998f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var barImage = root.GetComponent<Image>();
            barImage.sprite = UiTheme.Fill;
            barImage.type = Image.Type.Sliced;
            barImage.color = UiTheme.Bar;
            var h = root.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(28, 14, 10, 10);
            h.spacing = 14;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            var b = root.AddComponent<ProvenanceBar>();
            var labels = Group(root.transform, "Labels");
            labels.GetComponent<LayoutElement>().flexibleWidth = 1;
            labels.GetComponent<VerticalLayoutGroup>().spacing = 0;
            b.DataLabel = Label(labels.transform, "Data: -", 28, 36, FontStyle.Bold, UiTheme.Text);
            b.AiLabel = Label(labels.transform, "AI: -", 26, 34, FontStyle.Bold, UiTheme.Accent);
            b.StateLabel = Label(labels.transform, "", 20, 24, FontStyle.Normal, UiTheme.Muted);
            b.PauseButton = MakeButton(root.transform, "Pause / Resume", ButtonKind.Secondary);
            b.ExitButton = MakeButton(root.transform, "Exit", ButtonKind.Secondary);
            foreach (var button in new[] { b.PauseButton, b.ExitButton })
            {
                var le = button.GetComponent<LayoutElement>();
                le.preferredWidth = button == b.ExitButton ? 160 : 280;
                le.preferredHeight = 72;
                button.GetComponentInChildren<Text>().fontSize = SmallSize;
            }
            return root;
        }

        static GameObject Pause()
        {
            var root = PanelRoot("PauseOverlay");
            root.GetComponent<Image>().color = new Color(UiTheme.Bar.r, UiTheme.Bar.g, UiTheme.Bar.b, 0.97f);
            var p = root.AddComponent<PauseOverlay>();
            Title(root.transform, "Paused").fontSize = 72;
            Body(root.transform, "Take your time. Nothing is recorded while paused. Exit is always available.", flexible: true);
            var row = ButtonRow(root.transform);
            p.ExitButton = MakeButton(row, "Exit", ButtonKind.Secondary);
            p.ResumeButton = MakeButton(row, "Resume", ButtonKind.Accent);
            return root;
        }

        /// <summary>Own overlay canvas above the panels; the dot is placed by ProbeTaskRunner each frame.</summary>
        static GameObject ProbeOverlay()
        {
            var root = new GameObject("ProbeOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var dot = new GameObject("ProbeTarget", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(root.transform, false);
            var rt = (RectTransform)dot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(26, 26);
            var image = dot.GetComponent<Image>();
            image.sprite = _res.knob;
            image.color = new Color(1f, 0.85f, 0.2f); // measurement stimulus: unchanged by UI restyling
            image.raycastTarget = false;
            dot.SetActive(false);
            var runner = root.AddComponent<ProbeTaskRunner>();
            runner.CanvasRect = (RectTransform)root.transform;
            runner.Target = rt;
            return root;
        }

        // ----- scene -----

        static void BuildScene(GameObject[] panelPrefabs, GameObject barPrefab, GameObject pausePrefab, GameObject probePrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var controller = new GameObject("SessionController").AddComponent<SessionController>();
            foreach (var prefab in panelPrefabs)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasGo.transform);
                instance.SetActive(false);
                controller.Panels.Add(instance.GetComponent<FlowPanel>());
            }
            controller.ProvenanceBar = ((GameObject)PrefabUtility.InstantiatePrefab(barPrefab, canvasGo.transform)).GetComponent<ProvenanceBar>();
            var pause = (GameObject)PrefabUtility.InstantiatePrefab(pausePrefab, canvasGo.transform);
            pause.SetActive(false);
            controller.PauseOverlay = pause.GetComponent<PauseOverlay>();
            controller.ProbeRunner = ((GameObject)PrefabUtility.InstantiatePrefab(probePrefab)).GetComponent<ProbeTaskRunner>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var others = EditorBuildSettings.scenes.Where(s => s.path != ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(others).ToArray();
        }

        // ----- widgets -----

        static GameObject Save(GameObject go)
        {
            var path = $"{PrefabDir}/{go.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject PanelRoot(string name, Vector2? anchorMin = null, Vector2? anchorMax = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin ?? new Vector2(0.08f, 0.03f);
            rt.anchorMax = anchorMax ?? new Vector2(0.92f, 0.88f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var card = go.GetComponent<Image>();
            card.sprite = UiTheme.Fill;
            card.type = Image.Type.Sliced;
            card.pixelsPerUnitMultiplier = 0.5f; // 32-unit corners on the large cards
            card.color = new Color(UiTheme.Card.r, UiTheme.Card.g, UiTheme.Card.b, 0.97f);
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(56, 56, 40, 36);
            v.spacing = 16;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return go;
        }

        static GameObject Group(Transform parent, string name, bool horizontal = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            HorizontalOrVerticalLayoutGroup g = horizontal
                ? (HorizontalOrVerticalLayoutGroup)go.AddComponent<HorizontalLayoutGroup>()
                : go.AddComponent<VerticalLayoutGroup>();
            g.spacing = horizontal ? 16 : 14;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = horizontal;
            g.childForceExpandHeight = false;
            return go;
        }

        static Transform ButtonRow(Transform parent)
        {
            var row = Group(parent, "Buttons", horizontal: true);
            ((HorizontalLayoutGroup)row.GetComponent<HorizontalOrVerticalLayoutGroup>()).childAlignment = TextAnchor.MiddleRight;
            row.GetComponent<HorizontalOrVerticalLayoutGroup>().childForceExpandWidth = false;
            row.GetComponent<LayoutElement>().preferredHeight = ButtonHeight + 4;
            return row.transform;
        }

        static void Spacer(Transform parent)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().flexibleHeight = 1;
        }

        static Text Label(Transform parent, string text, int size, float height, FontStyle style, Color color, bool flexible = false)
        {
            var go = DefaultControls.CreateText(_res);
            go.name = "Text";
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.lineSpacing = 1.1f;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.alignment = TextAnchor.UpperLeft;
            t.raycastTarget = false;
            t.text = text;
            var le = go.AddComponent<LayoutElement>();
            if (height > 0) le.preferredHeight = height;
            if (flexible) le.flexibleHeight = 1;
            return t;
        }

        static Text Eyebrow(Transform parent, string text) => Label(parent, text, EyebrowSize, 36, FontStyle.Bold, UiTheme.Accent);
        static Text Title(Transform parent, string text) => Label(parent, text, TitleSize, 72, FontStyle.Bold, UiTheme.Text);
        static Text Body(Transform parent, string text = "", float height = 0, bool flexible = false) =>
            Label(parent, text, BodySize, height, FontStyle.Normal, UiTheme.Text, flexible);
        static Text Small(Transform parent, string text, float height = 0, bool flexible = false) =>
            Label(parent, text, SmallSize, height, FontStyle.Normal, UiTheme.Muted, flexible);

        static Text Countdown(Transform parent)
        {
            var t = Label(parent, "", 96, 120, FontStyle.Bold, UiTheme.Accent);
            t.alignment = TextAnchor.MiddleCenter;
            return t;
        }

        static void Legend(Transform parent) => Small(parent, "Choose a number:  1 = not at all   ·   7 = extremely", 44);

        // Desktop only: the Quest presentation turns flexible bodies into scroll views at full size instead.
        static void BestFit(Text t)
        {
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 18;
            t.resizeTextMaxSize = BodySize;
        }

        static ColorBlock Tint()
        {
            var c = ColorBlock.defaultColorBlock;
            // Hover brightens: the resting state is slightly dimmed so a ray over a control is visible.
            c.normalColor = c.selectedColor = new Color(0.86f, 0.86f, 0.86f);
            c.highlightedColor = Color.white;
            c.pressedColor = new Color(0.66f, 0.66f, 0.66f);
            c.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.35f);
            c.fadeDuration = 0.08f;
            return c;
        }

        static Button MakeButton(Transform parent, string text, ButtonKind kind = ButtonKind.Primary)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = UiTheme.Fill;
            image.type = Image.Type.Sliced;
            image.color = kind == ButtonKind.Secondary ? UiTheme.Surface : UiTheme.Accent;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.colors = Tint();
            var label = Label(go.transform, text, ButtonSize, 0, FontStyle.Bold,
                kind == ButtonKind.Secondary ? UiTheme.Text : UiTheme.AccentText);
            label.alignment = TextAnchor.MiddleCenter;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            Stretch((RectTransform)label.transform, 16);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = ButtonHeight;
            return button;
        }

        /// <summary>Selectable pill: the whole pill turns accent when on (chips, 1-7 numbers, preferences).</summary>
        static Toggle Pill(Transform parent, string text, int fontSize, float width, float height, bool layout = true,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(ToggleVisual));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = UiTheme.Fill;
            image.type = Image.Type.Sliced;
            image.color = UiTheme.Surface;
            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = image;
            toggle.graphic = null;
            toggle.isOn = false;
            toggle.colors = Tint();
            var label = Label(go.transform, text, fontSize, 0, FontStyle.Bold, UiTheme.Text);
            label.alignment = alignment;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            Stretch((RectTransform)label.transform, 20);
            var visual = go.GetComponent<ToggleVisual>();
            visual.Toggle = toggle;
            visual.Background = image;
            visual.Label = label;
            visual.OnFill = UiTheme.Accent;
            visual.OffFill = UiTheme.Surface;
            visual.OnText = UiTheme.AccentText;
            visual.OffText = UiTheme.Text;
            if (layout)
            {
                var le = go.AddComponent<LayoutElement>();
                if (width > 0) le.preferredWidth = width;
                else le.flexibleWidth = 1;
                le.preferredHeight = height;
            }
            return toggle;
        }

        static Toggle MakeToggle(Transform parent, string text) =>
            Pill(parent, text, SmallSize, 0, 68, alignment: TextAnchor.MiddleLeft);

        static Transform ChipGrid(Transform parent, string name, int rows)
        {
            var grid = new GameObject(name, typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            grid.transform.SetParent(parent, false);
            var g = grid.GetComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(352, 80);
            g.spacing = new Vector2(14, 14);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 4;
            grid.GetComponent<LayoutElement>().preferredHeight = rows * 80 + (rows - 1) * 14;
            return grid.transform;
        }

        static InputField MakeInput(Transform parent, string placeholder)
        {
            var go = DefaultControls.CreateInputField(_res);
            go.name = "FreeText";
            go.transform.SetParent(parent, false);
            var field = go.GetComponent<InputField>();
            field.lineType = InputField.LineType.MultiLineNewline;
            field.colors = Tint();
            field.customCaretColor = true;
            field.caretColor = UiTheme.Text;
            field.caretWidth = 3;
            field.selectionColor = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.4f);
            var fieldImage = go.GetComponent<Image>();
            fieldImage.sprite = UiTheme.Fill;
            fieldImage.type = Image.Type.Sliced;
            fieldImage.color = UiTheme.Surface;
            foreach (var t in new[] { field.textComponent, field.placeholder as Text })
            {
                if (t == null) continue;
                t.font = _font;
                t.fontSize = SmallSize + 2;
                t.fontStyle = t == field.textComponent ? FontStyle.Normal : FontStyle.Italic;
                t.color = t == field.textComponent ? UiTheme.Text : UiTheme.Muted;
                Stretch((RectTransform)t.transform, 20);
            }
            ((Text)field.placeholder).text = placeholder;
            go.AddComponent<LayoutElement>().preferredHeight = 110;
            return field;
        }

        static RatingRow Rating(Transform parent, string label)
        {
            var row = Group(parent, label, horizontal: true);
            var h = (HorizontalLayoutGroup)row.GetComponent<HorizontalOrVerticalLayoutGroup>();
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.spacing = 14;
            row.GetComponent<LayoutElement>().preferredHeight = 100;
            var name = Label(row.transform, label, BodySize, 100, FontStyle.Bold, UiTheme.Text);
            name.alignment = TextAnchor.MiddleLeft;
            name.GetComponent<LayoutElement>().preferredWidth = 380;
            var r = row.AddComponent<RatingRow>();
            for (var i = 1; i <= RatingRow.Max; i++) r.Options.Add(Pill(row.transform, i.ToString(), NumberSize, 120, 100));
            return r;
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, 6);
            rect.offsetMax = new Vector2(-inset, -6);
        }
    }
}

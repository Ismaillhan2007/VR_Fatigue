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

        [MenuItem("App/Build Assessment Scene")]
        public static void BuildAll()
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
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

            var prefabs = new GameObject[]
            {
                Save(Consent()), Save(Baseline()), Save(LoadTask()), Save(CheckIn()), Save(Summary()),
                Save(SceneView()), Save(ReRating()), Save(Done()),
            };
            var bar = Save(Bar());
            var pause = Save(Pause());
            var probe = Save(ProbeOverlay());
            BuildScene(prefabs, bar, pause, probe);
            AssetDatabase.SaveAssets();
            Debug.Log("[AssessmentSceneBuilder] prefabs and Assessment scene written");
        }

        // ----- panels -----

        static GameObject Consent()
        {
            var root = PanelRoot("ConsentPanel");
            var p = root.AddComponent<ConsentPanel>();
            Label(root.transform, "Welcome", 30, 44);
            p.Body = Label(root.transform, "", 20, flexible: true);
            p.StartButton = MakeButton(ButtonRow(root.transform), "I agree, start");
            return root;
        }

        static GameObject Baseline()
        {
            var root = PanelRoot("BaselinePanel");
            var p = root.AddComponent<BaselinePanel>();
            Label(root.transform, "Baseline", 30, 44);
            p.Status = Label(root.transform, "", 20, 90);
            p.RatingGroup = Group(root.transform, "Ratings");
            p.EyeStrain = Rating(p.RatingGroup.transform, "Eye strain");
            p.MentalFatigue = Rating(p.RatingGroup.transform, "Mental fatigue");
            Label(root.transform, "1 = not at all, 7 = extremely", 16, 28);
            Spacer(root.transform);
            var row = ButtonRow(root.transform);
            p.SkipButton = MakeButton(row, "Skip (desktop)");
            p.ContinueButton = MakeButton(row, "Continue");
            return root;
        }

        static GameObject LoadTask()
        {
            var root = PanelRoot("LoadTaskPanel");
            var p = root.AddComponent<LoadTaskPanel>();
            Label(root.transform, "Task", 30, 44);
            p.Status = Label(root.transform, "", 20, flexible: true);
            p.SkipButton = MakeButton(ButtonRow(root.transform), "Skip (desktop)");
            return root;
        }

        static GameObject CheckIn()
        {
            var root = PanelRoot("CheckInPanel");
            var p = root.AddComponent<CheckInPanel>();
            Label(root.transform, "How do you feel?", 30, 44);
            p.EyeStrain = Rating(root.transform, "Eye strain");
            p.MentalFatigue = Rating(root.transform, "Mental fatigue");
            Label(root.transform, "Pick up to 6:", 18, 28);
            var grid = new GameObject("Chips", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            grid.transform.SetParent(root.transform, false);
            var g = grid.GetComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(250, 34);
            g.spacing = new Vector2(8, 6);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 5;
            grid.GetComponent<LayoutElement>().preferredHeight = 80;
            foreach (Chip c in Enum.GetValues(typeof(Chip))) p.ChipToggles.Add(MakeToggle(grid.transform, ReportText.ChipLabel(c), layout: false));
            p.FreeText = MakeInput(root.transform, "Optional: say how you feel (max 300 characters)");
            p.Hint = Label(root.transform, "", 14, 24);
            p.ChooseSettings = MakeToggle(root.transform, "I want to choose scene settings myself");
            p.PreferenceGroup = Group(root.transform, "Preferences", horizontal: true);
            p.Animals = MakeToggle(p.PreferenceGroup.transform, "Animals");
            p.Silent = MakeToggle(p.PreferenceGroup.transform, "Silent");
            p.Sunset = MakeToggle(p.PreferenceGroup.transform, "Sunset (off = day)");
            p.GentleMotion = MakeToggle(p.PreferenceGroup.transform, "Gentle motion");
            p.SubmitButton = MakeButton(ButtonRow(root.transform), "Continue");
            return root;
        }

        static GameObject Summary()
        {
            var root = PanelRoot("SummaryPanel");
            var p = root.AddComponent<SummaryPanel>();
            Label(root.transform, "Your experimental wellness summary", 28, 40);
            p.Body = Label(root.transform, "", 16, flexible: true);
            p.Body.resizeTextForBestFit = true;
            p.Body.resizeTextMinSize = 10;
            p.Body.resizeTextMaxSize = 16;
            p.EnterSceneButton = MakeButton(ButtonRow(root.transform), "Enter calming scene");
            return root;
        }

        static GameObject SceneView()
        {
            var root = PanelRoot("ScenePanel", new Vector2(0.08f, 0.02f), new Vector2(0.92f, 0.3f));
            root.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.2f, 0.75f);
            var p = root.AddComponent<ScenePanel>();
            Label(root.transform, "Calming scene", 24, 32);
            p.Body = Label(root.transform, "", 15, flexible: true);
            var row = ButtonRow(root.transform);
            p.MuteButton = MakeButton(row, "Mute");
            p.FinishButton = MakeButton(row, "Finish");
            return root;
        }

        static GameObject ReRating()
        {
            var root = PanelRoot("ReRatingPanel");
            var p = root.AddComponent<ReRatingPanel>();
            Label(root.transform, "How do you feel now?", 30, 44);
            p.EyeStrain = Rating(root.transform, "Eye strain");
            p.MentalFatigue = Rating(root.transform, "Mental fatigue");
            Spacer(root.transform);
            p.SubmitButton = MakeButton(ButtonRow(root.transform), "Done");
            return root;
        }

        static GameObject Done()
        {
            var root = PanelRoot("DonePanel");
            var p = root.AddComponent<DonePanel>();
            Label(root.transform, "Finished", 30, 44);
            p.Body = Label(root.transform, "", 20, flexible: true);
            p.RestartButton = MakeButton(ButtonRow(root.transform), "New session");
            return root;
        }

        static GameObject Bar()
        {
            var root = new GameObject("ProvenanceBar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            var rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(0, 0.9f);
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            var h = root.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 8, 8);
            h.spacing = 16;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            var b = root.AddComponent<ProvenanceBar>();
            var labels = Group(root.transform, "Labels");
            labels.GetComponent<LayoutElement>().flexibleWidth = 1;
            b.DataLabel = Label(labels.transform, "Data: -", 18, 26);
            b.AiLabel = Label(labels.transform, "AI: -", 18, 26);
            b.StateLabel = Label(labels.transform, "", 14, 20);
            b.PauseButton = MakeButton(root.transform, "Pause / Resume");
            b.ExitButton = MakeButton(root.transform, "Exit");
            return root;
        }

        static GameObject Pause()
        {
            var root = PanelRoot("PauseOverlay");
            root.GetComponent<Image>().color = new Color(0, 0, 0, 0.92f);
            var p = root.AddComponent<PauseOverlay>();
            Label(root.transform, "Paused", 36, 60);
            Label(root.transform, "Take your time. Exit is always available.", 20, flexible: true);
            var row = ButtonRow(root.transform);
            p.ResumeButton = MakeButton(row, "Resume");
            p.ExitButton = MakeButton(row, "Exit");
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
            image.color = new Color(1f, 0.85f, 0.2f);
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
            go.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.2f, 0.95f);
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(32, 32, 24, 24);
            v.spacing = 10;
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
            g.spacing = 8;
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
            row.GetComponent<LayoutElement>().preferredHeight = 52;
            return row.transform;
        }

        static void Spacer(Transform parent)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().flexibleHeight = 1;
        }

        static Text Label(Transform parent, string text, int size, float height = 0, bool flexible = false)
        {
            var go = DefaultControls.CreateText(_res);
            go.name = "Text";
            go.transform.SetParent(parent, false);
            var t = Style(go.GetComponent<Text>(), size);
            t.text = text;
            t.alignment = TextAnchor.UpperLeft;
            var le = go.AddComponent<LayoutElement>();
            if (height > 0) le.preferredHeight = height;
            if (flexible) le.flexibleHeight = 1;
            return t;
        }

        static Button MakeButton(Transform parent, string text)
        {
            var go = DefaultControls.CreateButton(_res);
            go.name = text;
            go.transform.SetParent(parent, false);
            var label = go.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = text;
                label.font = _font;
                label.fontSize = 20;
            }
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 240;
            le.preferredHeight = 48;
            return go.GetComponent<Button>();
        }

        static Toggle MakeToggle(Transform parent, string text, bool layout = true)
        {
            var go = DefaultControls.CreateToggle(_res);
            go.name = text;
            go.transform.SetParent(parent, false);
            var label = go.GetComponentInChildren<Text>();
            if (label != null)
            {
                Style(label, 18);
                label.text = text;
            }
            if (layout) go.AddComponent<LayoutElement>().preferredHeight = 32;
            return go.GetComponent<Toggle>();
        }

        static InputField MakeInput(Transform parent, string placeholder)
        {
            var go = DefaultControls.CreateInputField(_res);
            go.name = "FreeText";
            go.transform.SetParent(parent, false);
            var field = go.GetComponent<InputField>();
            field.lineType = InputField.LineType.MultiLineNewline;
            field.textComponent.font = _font;
            field.textComponent.fontSize = 18;
            if (field.placeholder is Text ph)
            {
                ph.font = _font;
                ph.text = placeholder;
            }
            go.AddComponent<LayoutElement>().preferredHeight = 70;
            return field;
        }

        static RatingRow Rating(Transform parent, string label)
        {
            var row = Group(parent, label, horizontal: true);
            row.GetComponent<HorizontalOrVerticalLayoutGroup>().childForceExpandWidth = false;
            row.GetComponent<LayoutElement>().preferredHeight = 36;
            Label(row.transform, label, 20, 32).GetComponent<LayoutElement>().preferredWidth = 220;
            var sliderGo = DefaultControls.CreateSlider(_res);
            sliderGo.transform.SetParent(row.transform, false);
            var sle = sliderGo.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1;
            sle.preferredHeight = 24;
            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = 1;
            slider.maxValue = 7;
            slider.wholeNumbers = true;
            slider.value = 1;
            var value = Label(row.transform, "1 / 7", 20, 32);
            value.GetComponent<LayoutElement>().preferredWidth = 90;
            var r = row.AddComponent<RatingRow>();
            r.Slider = slider;
            r.ValueLabel = value;
            return r;
        }

        static Text Style(Text t, int size)
        {
            t.font = _font;
            t.fontSize = size;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }
    }
}

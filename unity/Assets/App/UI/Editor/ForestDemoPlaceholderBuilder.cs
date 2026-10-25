using System.IO;
using System.Linq;
using App.Relaxation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrFatigue.Relaxation;
using VrFatigue.Squirrel;

namespace App.UI.Editor
{
    /// <summary>
    /// INTEGRATION PLACEHOLDER for the forest owner: creates Assets/App/Scenes/ForestDemo.unity only if it does not
    /// exist yet (never overwrites the owner's scene), with primitive trees, the generated squirrel, particles, an
    /// orb, empty audio layers and SceneConfigApplier + ForestSceneApplier wired. No camera: it is loaded additively
    /// under the Assessment camera. Menu: App > Create ForestDemo Placeholder.
    /// </summary>
    public static class ForestDemoPlaceholderBuilder
    {
        public const string ScenePath = "Assets/App/Scenes/ForestDemo.unity";
        const string MaterialDir = "Assets/App/Relaxation/Placeholder";

        [MenuItem("App/Create ForestDemo Placeholder")]
        public static void CreateIfMissing()
        {
            if (!File.Exists(ScenePath))
            {
                var assessment = EditorSceneManager.GetActiveScene().path;
                Build();
                if (!string.IsNullOrEmpty(assessment) && assessment != ScenePath) EditorSceneManager.OpenScene(assessment);
            }
            else Debug.Log("[ForestDemoPlaceholderBuilder] ForestDemo exists; left untouched");
            AddToBuildSettings();
        }

        static void Build()
        {
            Directory.CreateDirectory(MaterialDir);
            var leaves = Material("Leaves", new Color(0.25f, 0.45f, 0.22f));
            var trunk = Material("Trunk", new Color(0.36f, 0.25f, 0.16f));
            var ground = Material("Ground", new Color(0.30f, 0.38f, 0.22f));
            var fur = Material("SquirrelFur", new Color(0.55f, 0.31f, 0.15f));
            var glow = Material("Particles", new Color(1f, 0.92f, 0.6f), "Universal Render Pipeline/Particles/Unlit");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("ForestPlaceholder");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.SetParent(root.transform);
            floor.transform.localScale = new Vector3(10, 1, 10);
            floor.GetComponent<Renderer>().sharedMaterial = ground;

            // Deterministic scatter in front of the Assessment camera (0, 1, -10); nothing within 2.5 m of it.
            var rng = new System.Random(7);
            for (var i = 0; i < 40; i++)
            {
                var pos = new Vector3((float)(rng.NextDouble() * 36 - 18), 0, (float)(rng.NextDouble() * 34 - 6));
                if (Vector3.Distance(pos, new Vector3(0, 0, -10)) < 6 || Mathf.Abs(pos.x) < 3 && pos.z < 4) continue;
                Tree(root.transform, pos, 0.85f + (float)rng.NextDouble() * 0.35f, (float)rng.NextDouble() * 360, leaves, trunk);
            }

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.SetParent(root.transform);
            sun.transform.rotation = Quaternion.Euler(12, 30, 0);

            var anchor = new GameObject("SquirrelAnchor");
            anchor.transform.SetParent(root.transform);
            anchor.transform.position = new Vector3(2.2f, 0, -6.5f);
            anchor.transform.rotation = Quaternion.Euler(0, 200, 0);
            SquirrelIdle idle = null;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/App/Squirrel/Squirrel.obj");
            if (model != null)
            {
                var squirrel = (GameObject)PrefabUtility.InstantiatePrefab(model, anchor.transform);
                squirrel.transform.localScale = Vector3.one * 2f;
                foreach (var r in squirrel.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = Enumerable.Repeat(fur, r.sharedMaterials.Length).ToArray();
                idle = squirrel.AddComponent<SquirrelIdle>();
            }

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "BreathingOrb";
            Object.DestroyImmediate(orb.GetComponent<Collider>());
            orb.transform.SetParent(root.transform);
            orb.transform.position = new Vector3(0, 1.4f, -4f);
            orb.transform.localScale = Vector3.one * 0.35f;
            orb.GetComponent<Renderer>().sharedMaterial = glow;

            var controller = new GameObject("ForestController");
            controller.transform.SetParent(root.transform);
            var applier = controller.AddComponent<SceneConfigApplier>();
            controller.AddComponent<ForestSceneApplier>();

            var so = new SerializedObject(applier);
            so.FindProperty("sun").objectReferenceValue = sun;
            foreach (var layer in new[] { "wind", "birds", "water", "pad" })
            {
                var source = new GameObject("Audio_" + layer).AddComponent<AudioSource>();
                source.transform.SetParent(controller.transform);
                source.loop = true;
                source.playOnAwake = false;
                so.FindProperty(layer).objectReferenceValue = source; // no clips yet: forest owner adds CC0/own audio
            }
            so.FindProperty("squirrelAnchor").objectReferenceValue = anchor;
            so.FindProperty("fireflies").objectReferenceValue = Particles(root.transform, "Fireflies", glow, 0.05f);
            so.FindProperty("pollen").objectReferenceValue = Particles(root.transform, "Pollen", glow, 0.025f);
            so.FindProperty("orb").objectReferenceValue = orb.transform;
            var motion = so.FindProperty("ambientMotionTargets");
            motion.arraySize = idle != null ? 1 : 0;
            if (idle != null) motion.GetArrayElementAtIndex(0).objectReferenceValue = idle;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[ForestDemoPlaceholderBuilder] placeholder ForestDemo written");
        }

        static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(s => s.path != ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            // Assessment stays first.
            var assessment = scenes.FirstOrDefault(s => s.path == AssessmentSceneBuilder.ScenePath);
            if (assessment != null) { scenes.Remove(assessment); scenes.Insert(0, assessment); }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void Tree(Transform parent, Vector3 pos, float scale, float yaw, Material leaves, Material trunk)
        {
            var tree = new GameObject("Tree");
            tree.transform.SetParent(parent);
            tree.transform.position = pos;
            tree.transform.rotation = Quaternion.Euler(0, yaw, 0);
            tree.transform.localScale = Vector3.one * scale;
            tree.isStatic = true;
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.transform.SetParent(tree.transform, false);
            stem.transform.localPosition = new Vector3(0, 1.2f, 0);
            stem.transform.localScale = new Vector3(0.3f, 1.2f, 0.3f);
            stem.GetComponent<Renderer>().sharedMaterial = trunk;
            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.transform.SetParent(tree.transform, false);
            crown.transform.localPosition = new Vector3(0, 3.1f, 0);
            crown.transform.localScale = new Vector3(2.2f, 2.6f, 2.2f);
            crown.GetComponent<Renderer>().sharedMaterial = leaves;
        }

        static ParticleSystem Particles(Transform parent, string name, Material material, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(0, 1.2f, 0);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = size;
            main.startSpeed = 0.05f;
            main.startLifetime = 8f;
            main.maxParticles = 150;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(14, 2, 14);
            var emission = ps.emission;
            emission.rateOverTime = 12;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return ps;
        }

        static Material Material(string name, Color color, string shader = "Universal Render Pipeline/Lit")
        {
            var path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = new Material(Shader.Find(shader) ?? Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VrFatigue.Relaxation;
using VrFatigue.Squirrel;
using PlaceholderBuilder = App.UI.Editor.ForestDemoPlaceholderBuilder;

namespace App.Relaxation.Editor
{
    /// <summary>
    /// Builds Assets/App/Scenes/ForestDemo.unity from the committed Quaternius "Stylized Nature MegaKit" (68 textured
    /// models, CC0), the 4K sky, generated ground and synthesised audio. Deterministic (fixed seed) and re-runnable; it
    /// REPLACES the scene, prefabs and generated assets under Assets/App/Forest. Run it once on one machine and commit the
    /// result (including .meta files) so GUIDs stay stable for everybody. Menu: App > Forest > Build ForestDemo. Batch:
    /// -executeMethod App.Relaxation.Editor.ForestSceneBuilder.BuildBatch.
    ///
    /// It wires the existing seams only: SceneConfigApplier (forest DTO, VrFatigue.Relaxation) + ForestSceneApplier
    /// (ISceneApplier seam, App.Relaxation). It adds no config fields and no runtime code besides the skybox hook.
    /// The scene has no camera: it is loaded additively under the Assessment camera at (0, 1, -10) looking along +Z.
    /// </summary>
    public static class ForestSceneBuilder
    {
        public const string Root = "Assets/App/Forest";
        const string ModelDir = Root + "/Art/MegaKit";
        const string TextureDir = ModelDir + "/Textures";
        const string SkyTexturePath = Root + "/Art/Sky/DayEnvironmentHDRI113_4K_TONEMAPPED.jpg";
        const string GeneratedDir = Root + "/Generated";
        const string MaterialDir = Root + "/Materials";
        const string PrefabDir = Root + "/Prefabs";
        const string AudioDir = Root + "/Audio";
        const string SquirrelModel = "Assets/App/Squirrel/Squirrel.obj";
        const int Seed = 20260101;
        const int QuestTextureSize = 1024; // Android override for every MegaKit texture (ASTC)

        static readonly Vector3 Viewer = new Vector3(ForestTerrain.Viewer.x, 0f, ForestTerrain.Viewer.y);
        static readonly Vector2 RockSeat = new Vector2(2.0f, -5.6f); // boulder the squirrel sits on

        struct Spec
        {
            public string Name;
            public float Target;      // metres: height, or longest side when Extent
            public bool Extent;       // measure the longest side instead of the height
            public float CullHeight;  // >0 adds a one-level LODGroup that culls below this screen height
            public Spec(string n, float t, bool extent = false, float cull = 0f) { Name = n; Target = t; Extent = extent; CullHeight = cull; }
        }

        static readonly Spec[] Specs = BuildSpecs();

        static Spec[] BuildSpecs()
        {
            var l = new List<Spec>();
            for (int i = 1; i <= 5; i++) l.Add(new Spec("CommonTree_" + i, 9.0f));
            for (int i = 1; i <= 5; i++) l.Add(new Spec("TwistedTree_" + i, 7.0f));
            for (int i = 1; i <= 5; i++) l.Add(new Spec("Pine_" + i, 11.0f));
            for (int i = 1; i <= 5; i++) l.Add(new Spec("DeadTree_" + i, 8.0f));
            l.Add(new Spec("Bush_Common", 1.3f));
            l.Add(new Spec("Bush_Common_Flowers", 1.3f));
            l.Add(new Spec("Plant_1", 0.8f, false, 0.012f));
            l.Add(new Spec("Plant_1_Big", 1.2f, false, 0.012f));
            l.Add(new Spec("Plant_7", 0.8f, false, 0.012f));
            l.Add(new Spec("Plant_7_Big", 1.2f, false, 0.012f));
            l.Add(new Spec("Fern_1", 0.9f, false, 0.012f));
            l.Add(new Spec("Clover_1", 0.35f, true, 0.01f));
            l.Add(new Spec("Clover_2", 0.35f, true, 0.01f));
            l.Add(new Spec("Grass_Common_Short", 0.35f, false, 0.012f));
            l.Add(new Spec("Grass_Common_Tall", 0.7f, false, 0.012f));
            l.Add(new Spec("Grass_Wispy_Short", 0.4f, false, 0.012f));
            l.Add(new Spec("Grass_Wispy_Tall", 0.8f, false, 0.012f));
            foreach (var f in new[] { "Flower_3_Single", "Flower_4_Single" }) l.Add(new Spec(f, 0.4f, false, 0.012f));
            foreach (var f in new[] { "Flower_3_Group", "Flower_4_Group" }) l.Add(new Spec(f, 0.5f, false, 0.012f));
            for (int i = 1; i <= 5; i++) l.Add(new Spec("Petal_" + i, 0.12f, true, 0.008f));
            l.Add(new Spec("Mushroom_Common", 0.18f, false, 0.01f));
            l.Add(new Spec("Mushroom_Laetiporus", 0.35f, true, 0.01f));
            for (int i = 1; i <= 5; i++) l.Add(new Spec("Pebble_Round_" + i, 0.18f, true, 0.008f));
            for (int i = 1; i <= 6; i++) l.Add(new Spec("Pebble_Square_" + i, 0.2f, true, 0.008f));
            for (int i = 1; i <= 3; i++) l.Add(new Spec("RockPath_Round_Small_" + i, 0.6f, true, 0.01f));
            for (int i = 1; i <= 3; i++) l.Add(new Spec("RockPath_Square_Small_" + i, 0.6f, true, 0.01f));
            l.Add(new Spec("RockPath_Round_Thin", 0.9f, true, 0.01f));
            l.Add(new Spec("RockPath_Square_Thin", 0.9f, true, 0.01f));
            l.Add(new Spec("RockPath_Round_Wide", 1.1f, true, 0.01f));
            l.Add(new Spec("RockPath_Square_Wide", 1.1f, true, 0.01f));
            float[] rock = { 1.4f, 1.1f, 1.6f };
            for (int i = 1; i <= 3; i++) l.Add(new Spec("Rock_Medium_" + i, rock[i - 1], true, 0.01f));
            return l.ToArray();
        }

        // ---------------------------------------------------------------- menu

        [MenuItem("App/Forest/Build ForestDemo")]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog("Build ForestDemo",
                    "This replaces Assets/App/Scenes/ForestDemo.unity and regenerates Assets/App/Forest/{Prefabs,Materials,Generated,Audio}. " +
                    "Commit the result once; do not rebuild on several machines.", "Build", "Cancel")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
        }

        public static void BuildBatch() => BuildScene();

        // ---------------------------------------------------------------- build

        static void BuildScene()
        {
            var previous = EditorSceneManager.GetActiveScene().path;
            var missing = Specs.Select(s => $"{ModelDir}/{s.Name}.fbx").Where(p => !File.Exists(p)).ToList();
            if (missing.Count > 0 || !File.Exists(SkyTexturePath) || !File.Exists(SquirrelModel))
            {
                Debug.LogError("[ForestSceneBuilder] missing input assets: " + string.Join(", ", missing) +
                               (File.Exists(SkyTexturePath) ? "" : " | sky texture") +
                               (File.Exists(SquirrelModel) ? "" : " | squirrel model"));
                return;
            }

            MaterialCache.Clear();
            TextureIndex.Clear();
            UnresolvedTextures.Clear();
            PathStones.Clear();
            foreach (var d in new[] { GeneratedDir, MaterialDir, PrefabDir, AudioDir }) Directory.CreateDirectory(d);
            try
            {
                ConfigureTextures();
                ImportModels();
                IndexTextures();
                var sky = ConfigureSky();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var prefabs = MakePrefabs();
                var env = new GameObject("Environment").transform;
                var ground = BuildGround(env);
                var water = BuildWater(env);
                BuildFarGround(env);

                var rng = new System.Random(Seed);
                var occupied = new List<Vector3>(); // x = world x, y = world z, z = clearance radius
                occupied.Add(new Vector3(RockSeat.x, RockSeat.y, 1.6f));
                PlacePath(prefabs, env, rng, occupied);
                PlaceTrees(prefabs, env, rng, occupied);
                PlaceUnderstory(prefabs, env, rng, occupied);
                PlacePondDetails(prefabs, env, rng);
                var anchor = BuildSquirrel(prefabs, env);

                var sun = BuildLighting();
                var fx = new GameObject("Effects").transform;
                var fireflies = BuildParticles(fx, "Fireflies", new Vector3(0f, 1.4f, 4f), new Vector3(18f, 2.2f, 14f), 5f, 0.06f, 0.10f,
                    new Color(1f, 0.92f, 0.55f, 0.9f), 90);
                var pollen = BuildParticles(fx, "Pollen", new Vector3(0f, 1.6f, 0f), new Vector3(16f, 2.6f, 14f), 4f, 0.025f, 0.045f,
                    new Color(1f, 0.97f, 0.85f, 0.45f), 80);
                var orb = BuildOrb(fx);

                var audio = new GameObject("Audio").transform;
                BuildController(audio, sun, sky, anchor, fireflies, pollen, orb);

                ApplyRenderSettings(sky, sun);
                EditorSceneManager.SaveScene(scene, PlaceholderBuilder.ScenePath, false);
                AssetDatabase.SaveAssets();
                Report(ground, water);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            PlaceholderBuilder.CreateIfMissing(); // scene now exists: only registers it in Build Settings, Assessment first
            if (!string.IsNullOrEmpty(previous) && previous != PlaceholderBuilder.ScenePath) EditorSceneManager.OpenScene(previous);
        }

        // ---------------------------------------------------------------- import settings

        static bool IsAlphaTexture(string stem) =>
            stem.StartsWith("Leaves") || stem.StartsWith("Leaf") || stem.StartsWith("Grass") || stem.StartsWith("Flowers");

        // Every MegaKit texture is capped for the headset (ASTC) while the editor keeps the full-size source.
        static void ConfigureTextures()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in Directory.GetFiles(TextureDir, "*.png").Select(p => p.Replace('\\', '/')))
                {
                    var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti == null) continue;
                    string stem = Path.GetFileNameWithoutExtension(path);
                    bool normal = stem.EndsWith("_Normal");
                    ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.mipmapEnabled = true;
                    ti.anisoLevel = 2;
                    ti.wrapMode = TextureWrapMode.Repeat;
                    ti.filterMode = FilterMode.Bilinear;
                    ti.textureCompression = TextureImporterCompression.Compressed;
                    if (!normal && IsAlphaTexture(stem))
                    {
                        ti.alphaSource = TextureImporterAlphaSource.FromInput;
                        ti.alphaIsTransparency = true;
                        ti.mipMapsPreserveCoverage = true;
                        ti.alphaTestReferenceValue = 0.5f;
                    }
                    ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                    {
                        name = "Android", overridden = true, maxTextureSize = QuestTextureSize,
                        format = normal ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6, compressionQuality = 50,
                    });
                    ti.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
        }

        static void ImportModels()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var s in Specs)
                {
                    var mi = AssetImporter.GetAtPath($"{ModelDir}/{s.Name}.fbx") as ModelImporter;
                    if (mi == null) continue;
                    mi.importCameras = false;
                    mi.importLights = false;
                    mi.importBlendShapes = false;
                    mi.animationType = ModelImporterAnimationType.None;
                    mi.isReadable = false;
                    mi.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
        }

        // 4096x2048 LDR panorama (tone-mapped export of the Day Environment HDRI): ASTC 6x6 on Quest keeps it near 5 MB.
        // The 37 MB EXR and RGBAHalf are deliberately not used on the headset.
        static Material ConfigureSky()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(SkyTexturePath);
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 0;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 4096,
                format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50,
            });
            ti.SaveAndReimport();

            var mat = Recreate(MaterialDir + "/ForestSky.mat", "Skybox/Panoramic");
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(SkyTexturePath));
            mat.SetFloat("_Mapping", 1f); // latitude/longitude layout
            mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            mat.SetFloat("_ImageType", 0f);
            mat.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- materials

        static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> TextureIndex = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> UnresolvedTextures = new HashSet<string>();

        static void IndexTextures()
        {
            foreach (var path in Directory.GetFiles(TextureDir, "*.png").Select(p => p.Replace('\\', '/')))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) TextureIndex[Path.GetFileNameWithoutExtension(path).ToLowerInvariant()] = tex;
            }
        }

        static Material Recreate(string path, string shaderName)
        {
            AssetDatabase.DeleteAsset(path);
            var m = new Material(Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material Lit(string name, Color color, float smoothness = 0.05f, bool twoSided = false, Texture baseMap = null,
            Texture normalMap = null, bool alphaClip = false)
        {
            string key = name + (twoSided ? "_2s" : "") + (alphaClip ? "_ac" : "");
            if (MaterialCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var m = Recreate($"{MaterialDir}/{key}.mat", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            if (baseMap != null) m.SetTexture("_BaseMap", baseMap);
            if (normalMap != null)
            {
                m.SetTexture("_BumpMap", normalMap);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (twoSided) m.SetFloat("_Cull", 0f);
            if (alphaClip)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                if (m.HasProperty("_AlphaToMask")) m.SetFloat("_AlphaToMask", 1f); // smooth leaf edges when MSAA is on
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            MaterialCache[key] = m;
            return m;
        }

        // Pull saturated source colours slightly towards a calm natural forest (used only when a material has no texture).
        static Color Grade(Color c)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            return Color.HSVToRGB(h, s * 0.88f, Mathf.Pow(v, 1.05f) * 0.95f);
        }

        // MegaKit materials are named after their texture family ("Leaves_NormalTree", "Bark_NormalTree", "Leaves_Pine",
        // "Grass", "Rocks", ...). Unity usually binds the PNGs by file name on import; when it does not, resolve them here.
        static Texture2D FindTexture(string materialName, bool wantNormal)
        {
            string n = materialName.ToLowerInvariant().Replace("leaves_pine", "leaf_pine");
            string[] candidates = wantNormal
                ? new[] { n + "_normal" }
                : new[] { n + "_c", n, n + "_diffuse" };
            foreach (var c in candidates)
                if (TextureIndex.TryGetValue(c, out var t)) return t;
            if (wantNormal) return null;
            foreach (var kv in TextureIndex) // fuzzy: "PathRocks" -> PathRocks_Diffuse, "Rocks" -> Rocks_Diffuse
                if (!kv.Key.EndsWith("_normal") && (kv.Key.StartsWith(n) || n.StartsWith(kv.Key.Replace("_diffuse", "").Replace("_c", ""))))
                    return kv.Value;
            return null;
        }

        static Material ToUrp(Material src)
        {
            if (src == null) return Lit("Fallback", new Color(0.4f, 0.4f, 0.4f), 0.05f, true);
            string name = new string(src.name.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
            bool foliage = IsAlphaTexture(name);
            Texture baseMap = src.mainTexture != null ? src.mainTexture : FindTexture(name, false);
            Texture normalMap = src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null ? src.GetTexture("_BumpMap") : FindTexture(name, true);
            if (baseMap == null) UnresolvedTextures.Add(name);
            Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                    : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.gray;
            c = baseMap != null ? new Color(0.97f, 0.97f, 0.97f) : Grade(c);
            return Lit(name, c, foliage ? 0.02f : 0.06f, foliage, baseMap, normalMap, foliage && baseMap != null);
        }

        // ---------------------------------------------------------------- prefabs

        static Dictionary<string, GameObject> MakePrefabs()
        {
            var result = new Dictionary<string, GameObject>();
            for (int i = 0; i < Specs.Length; i++)
            {
                var s = Specs[i];
                EditorUtility.DisplayProgressBar("ForestDemo", "Prefab " + s.Name, i / (float)Specs.Length);
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{s.Name}.fbx");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                var root = new GameObject(s.Name);
                model.transform.SetParent(root.transform, false);
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterials = r.sharedMaterials.Select(ToUrp).ToArray();
                    r.shadowCastingMode = s.Target < 0.8f ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                // Normalise size (independent of the FBX unit scale) and put the pivot x/z on the trunk, y on the ground.
                var b = RenderBounds(root);
                float measured = s.Extent ? Mathf.Max(b.size.x, b.size.y, b.size.z) : b.size.y;
                float f = measured > 1e-4f ? s.Target / measured : 1f;
                model.transform.localScale = Vector3.one * f;
                b = RenderBounds(root);
                model.transform.localPosition = new Vector3(0f, -b.min.y, 0f);

                if (s.CullHeight > 0f)
                {
                    var lg = root.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(s.CullHeight, root.GetComponentsInChildren<Renderer>()) });
                    lg.RecalculateBounds();
                }
                var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{s.Name}.prefab");
                Object.DestroyImmediate(root);
                result[s.Name] = saved;
            }
            EditorUtility.ClearProgressBar();
            return result;
        }

        static Bounds RenderBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // ---------------------------------------------------------------- ground, water

        static MeshFilter BuildGround(Transform parent)
        {
            var mesh = SaveMesh(ForestTerrain.BuildGroundMesh(112), "ForestGround");
            var albedo = ForestTerrain.PaintAlbedo(GeneratedDir + "/GroundAlbedo.png", 2048);
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Lit("ForestGround", Color.white, 0f, false, albedo);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
            return mf;
        }

        static void BuildFarGround(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "FarGround";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, -0.02f, 0f);
            go.transform.localScale = new Vector3(80f, 1f, 80f); // 800 m
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Lit("ForestFarGround", ForestTerrain.FarColor, 0f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
        }

        static MeshFilter BuildWater(Transform parent)
        {
            var mesh = SaveMesh(ForestTerrain.BuildDisc(ForestTerrain.PondRadius * 1.6f, 48, "ForestWater"), "ForestWater");
            var go = new GameObject("Water");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(ForestTerrain.Pond.x, ForestTerrain.WaterLevel, ForestTerrain.Pond.y);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Lit("ForestWater", new Color(0.09f, 0.26f, 0.28f), 0.85f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
            return mf;
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            var path = $"{GeneratedDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---------------------------------------------------------------- placement helpers

        static float Smooth(float edge0, float edge1, float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - edge0) / (edge1 - edge0)));

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        static bool Blocked(Vector2 p, float pad, bool keepCorridor)
        {
            if (Vector2.Distance(p, ForestTerrain.Viewer) < 5f) return true;                       // clearing
            if (Vector2.Distance(p, ForestTerrain.Pond) < ForestTerrain.PondRadius * 1.1f + pad) return true; // water + bank
            if (keepCorridor && p.y > ForestTerrain.Viewer.y &&
                DistToSegment(p, ForestTerrain.Viewer, ForestTerrain.Pond) < 2.4f) return true;    // view to the focal point
            return false;
        }

        static bool Clear(Vector2 p, float radius, List<Vector3> occupied)
        {
            foreach (var o in occupied)
                if (Vector2.Distance(p, new Vector2(o.x, o.y)) < radius + o.z) return false;
            return true;
        }

        static GameObject Spawn(GameObject prefab, Transform parent, Vector2 xz, float yaw, float scale, float tilt, float sink)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            float y = ForestTerrain.Height(xz.x, xz.y) - sink * scale;
            go.transform.position = new Vector3(xz.x, y, xz.y);
            go.transform.rotation = Quaternion.Euler(tilt * Mathf.Sin(yaw), yaw, tilt * Mathf.Cos(yaw));
            go.transform.localScale = Vector3.one * scale;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            return go;
        }

        static T Pick<T>(System.Random rng, IList<T> list) => list[rng.Next(list.Count)];
        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static string[] Names(string prefix, int from, int to) => Enumerable.Range(from, to - from + 1).Select(i => prefix + i).ToArray();

        static string PickWeighted(System.Random rng, string[] names, float[] weights)
        {
            float total = weights.Sum(), r = (float)rng.NextDouble() * total;
            for (int i = 0; i < names.Length; i++)
            {
                r -= weights[i];
                if (r <= 0f) return names[i];
            }
            return names[names.Length - 1];
        }

        static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        static readonly List<Vector2> PathStones = new List<Vector2>();
        static bool OnPath(Vector2 p) => PathStones.Any(s => Vector2.Distance(s, p) < 0.8f);

        static readonly string[] AllRocks = Names("Rock_Medium_", 1, 3);
        static readonly string[] AllPebbles = Names("Pebble_Round_", 1, 5).Concat(Names("Pebble_Square_", 1, 6)).ToArray();
        static readonly string[] Ferns = { "Fern_1", "Plant_1", "Plant_1_Big", "Plant_7", "Plant_7_Big" };
        static readonly string[] Blooms = { "Flower_3_Single", "Flower_3_Group", "Flower_4_Single", "Flower_4_Group" };

        // ---------------------------------------------------------------- placement

        // Stepping stones from the clearing towards the pond (a visible, calm line of sight; no collisions, never walked).
        static void PlacePath(Dictionary<string, GameObject> p, Transform env, System.Random rng, List<Vector3> occupied)
        {
            var path = new GameObject("StonePath").transform;
            path.SetParent(env, false);
            var a = new Vector2(-0.3f, -5.2f);
            var pond = ForestTerrain.Pond;
            var end = pond + (a - pond).normalized * ForestTerrain.PondRadius * 1.25f;
            var c1 = new Vector2(-1.1f, -2.4f);
            var c2 = new Vector2(end.x - 1.4f, end.y - 1.6f);
            var kinds = new[] { "RockPath_Round_Wide", "RockPath_Square_Wide", "RockPath_Round_Thin", "RockPath_Square_Thin",
                "RockPath_Round_Small_1", "RockPath_Square_Small_1", "RockPath_Round_Small_2", "RockPath_Square_Small_2",
                "RockPath_Round_Small_3", "RockPath_Square_Small_3" };
            const int Count = 13;
            for (int i = 0; i < Count; i++)
            {
                float t = i / (float)(Count - 1);
                var pos = Bezier(a, c1, c2, end, t);
                var ahead = Bezier(a, c1, c2, end, Mathf.Min(1f, t + 0.02f));
                float yaw = Mathf.Atan2(ahead.x - pos.x, ahead.y - pos.y) * Mathf.Rad2Deg + Range(rng, -25f, 25f);
                pos += new Vector2(Range(rng, -0.12f, 0.12f), Range(rng, -0.1f, 0.1f));
                Spawn(p[kinds[(i * 3 + rng.Next(3)) % kinds.Length]], path, pos, yaw, Range(rng, 0.9f, 1.15f), 0f, 0.03f);
                occupied.Add(new Vector3(pos.x, pos.y, 0.55f));
                PathStones.Add(pos);
            }
        }

        static void PlaceTrees(Dictionary<string, GameObject> p, Transform env, System.Random rng, List<Vector3> occupied)
        {
            var trees = new GameObject("Trees").transform;
            trees.SetParent(env, false);
            var common = Names("CommonTree_", 1, 5);
            var twisted = Names("TwistedTree_", 1, 5);
            var pine = Names("Pine_", 1, 5);
            var dead = Names("DeadTree_", 1, 5);

            // Two gnarled trees frame the pond, which is the focal point of the view.
            var pond = ForestTerrain.Pond;
            foreach (var (name, off) in new[] { ("TwistedTree_1", new Vector2(-6.2f, 3.4f)), ("TwistedTree_3", new Vector2(6.6f, 2.2f)) })
            {
                var pos = pond + off;
                Spawn(p[name], trees, pos, Range(rng, 0, 360), 1.1f, 1.5f, 0f);
                occupied.Add(new Vector3(pos.x, pos.y, 2.2f));
            }

            int placed = 0, attempts = 0, deadCount = 0;
            const int Main = 70;
            while (placed < Main && attempts++ < 6000)
            {
                var pos = new Vector2(Range(rng, -29f, 29f), Range(rng, -29f, 29f));
                float far = Smooth(8f, 26f, Vector2.Distance(pos, new Vector2(0f, -2f)));
                if (rng.NextDouble() > 0.25 + 0.75 * far) continue; // denser towards the edge, open near the viewer
                if (Blocked(pos, 1.0f, true) || Vector2.Distance(pos, ForestTerrain.Viewer) < 7f) continue;
                float r = Range(rng, 2.4f, 3.8f);
                if (!Clear(pos, r, occupied)) continue;
                double kind = rng.NextDouble();
                string name;
                if (kind < 0.42) name = Pick(rng, common);
                else if (kind < 0.60) name = Pick(rng, twisted);
                else if (kind < 0.94 || deadCount >= 4 || Vector2.Distance(pos, ForestTerrain.Viewer) < 14f) name = Pick(rng, pine);
                else { name = Pick(rng, dead); deadCount++; }
                Spawn(p[name], trees, pos, Range(rng, 0, 360), Range(rng, 0.88f, 1.3f), 2.5f, 0.05f);
                occupied.Add(new Vector3(pos.x, pos.y, r * 0.5f));
                placed++;
            }

            // Tree line on the far plane so the 64 m ground never looks like an island.
            int ring = 0; attempts = 0;
            var farOcc = new List<Vector3>();
            while (ring < 48 && attempts++ < 3000)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f), d = Range(rng, 32f, 58f);
                var pos = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
                if (!Clear(pos, 3.5f, farOcc)) continue;
                string name = rng.NextDouble() < 0.55 ? Pick(rng, pine) : Pick(rng, common);
                Spawn(p[name], trees, pos, Range(rng, 0, 360), Range(rng, 1.3f, 1.9f), 0f, 0f);
                farOcc.Add(new Vector3(pos.x, pos.y, 1.75f));
                ring++;
            }
        }

        static void PlaceUnderstory(Dictionary<string, GameObject> p, Transform env, System.Random rng, List<Vector3> occupied)
        {
            var under = new GameObject("Understory").transform;
            under.SetParent(env, false);
            var trunks = occupied.Where(o => o.z > 1.0f).Select(o => new Vector2(o.x, o.y)).ToList();
            var bushes = new[] { "Bush_Common", "Bush_Common_Flowers" };
            var grasses = new[] { "Grass_Common_Short", "Grass_Common_Tall", "Grass_Wispy_Short", "Grass_Wispy_Tall" };
            var grassWeights = new[] { 3f, 2f, 3f, 2f };
            var fernWeights = new[] { 2f, 2f, 1f, 1.5f, 1f };

            int n = 0, tries = 0;
            while (n < 48 && tries++ < 3000)
            {
                var pos = new Vector2(Range(rng, -28f, 28f), Range(rng, -28f, 28f));
                if (Mathf.PerlinNoise(pos.x * 0.11f + 5f, pos.y * 0.11f + 9f) < 0.42f) continue;
                if (Blocked(pos, 1.2f, true) || !Clear(pos, 0.9f, occupied)) continue;
                Spawn(p[rng.NextDouble() < 0.7 ? bushes[0] : bushes[1]], under, pos, Range(rng, 0, 360), Range(rng, 0.8f, 1.3f), 3f, 0.02f);
                occupied.Add(new Vector3(pos.x, pos.y, 0.5f));
                n++;
            }

            // Ferns and plants gather around trunks, the way undergrowth does.
            n = 0; tries = 0;
            while (n < 110 && tries++ < 3000)
            {
                var around = Pick(rng, trunks);
                float a = Range(rng, 0f, Mathf.PI * 2f), d = Range(rng, 0.9f, 2.6f);
                var pos = around + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (Mathf.Abs(pos.x) > 29f || Mathf.Abs(pos.y) > 29f || Blocked(pos, 0.6f, false) || OnPath(pos)) continue;
                Spawn(p[PickWeighted(rng, Ferns, fernWeights)], under, pos, Range(rng, 0, 360), Range(rng, 0.8f, 1.4f), 4f, 0.01f);
                n++;
            }

            // Grass in soft clumps, including inside the clearing (but not under the viewer).
            n = 0; tries = 0;
            while (n < 340 && tries++ < 8000)
            {
                var pos = new Vector2(Range(rng, -29f, 29f), Range(rng, -29f, 29f));
                if (Mathf.PerlinNoise(pos.x * 0.16f + 31f, pos.y * 0.16f + 2f) < 0.5f) continue;
                if (Vector2.Distance(pos, ForestTerrain.Viewer) < 1.6f || ForestTerrain.IsWater(pos.x, pos.y) || OnPath(pos)) continue;
                Spawn(p[PickWeighted(rng, grasses, grassWeights)], under, pos, Range(rng, 0, 360), Range(rng, 0.8f, 1.5f), 6f, 0f);
                n++;
            }

            // Clover carpets near the clearing edge.
            for (int i = 0; i < 70; i++)
            {
                var c = new Vector2(Range(rng, -9f, 9f), Range(rng, -9f, 0f));
                if (Blocked(c, 0.2f, false) || OnPath(c) || Vector2.Distance(c, ForestTerrain.Viewer) < 1.6f) continue;
                Spawn(p[rng.NextDouble() < 0.5 ? "Clover_1" : "Clover_2"], under, c, Range(rng, 0, 360), Range(rng, 0.9f, 1.6f), 3f, 0f);
            }

            // Three flower meadows at the edge of the clearing, with fallen petals around them.
            foreach (var c in new[] { new Vector2(-4.6f, -6.2f), new Vector2(5.2f, -3.4f), new Vector2(-6.4f, 0.8f) })
            {
                for (int i = 0; i < 14; i++)
                {
                    var pos = c + new Vector2(Range(rng, -2.2f, 2.2f), Range(rng, -2.2f, 2.2f));
                    if (Blocked(pos, 0.5f, false) || !Clear(pos, 0.3f, occupied)) continue;
                    Spawn(p[Pick(rng, Blooms)], under, pos, Range(rng, 0, 360), Range(rng, 0.9f, 1.5f), 5f, 0f);
                }
                for (int i = 0; i < 14; i++)
                {
                    var pos = c + new Vector2(Range(rng, -2.8f, 2.8f), Range(rng, -2.8f, 2.8f));
                    if (Blocked(pos, 0.4f, false)) continue;
                    Spawn(p["Petal_" + rng.Next(1, 6)], under, pos, Range(rng, 0, 360), Range(rng, 0.8f, 1.4f), 0f, 0f);
                }
            }

            // Mushrooms at tree bases; a bracket fungus on a few trunks.
            n = 0; tries = 0;
            while (n < 18 && tries++ < 600)
            {
                var around = Pick(rng, trunks);
                float a = Range(rng, 0f, Mathf.PI * 2f), d = Range(rng, 0.6f, 1.3f);
                var pos = around + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (Blocked(pos, 0.5f, false)) continue;
                Spawn(p["Mushroom_Common"], under, pos, Range(rng, 0, 360), Range(rng, 0.8f, 1.5f), 4f, 0f);
                if (n % 6 == 0) Spawn(p["Mushroom_Laetiporus"], under, pos + new Vector2(0.35f, 0.2f), Range(rng, 0, 360), 1f, 0f, 0.02f);
                n++;
            }

            // Boulders, half sunk, away from trunks; pebbles gather around them.
            n = 0; tries = 0;
            while (n < 16 && tries++ < 3000)
            {
                var pos = new Vector2(Range(rng, -27f, 27f), Range(rng, -27f, 27f));
                if (Blocked(pos, 1.0f, true) || !Clear(pos, 0.8f, occupied)) continue;
                Spawn(p[Pick(rng, AllRocks)], under, pos, Range(rng, 0, 360), Range(rng, 0.7f, 1.7f), 8f, 0.18f);
                occupied.Add(new Vector3(pos.x, pos.y, 0.6f));
                for (int k = 0; k < 4; k++)
                {
                    float a = Range(rng, 0f, Mathf.PI * 2f), d = Range(rng, 0.9f, 1.8f);
                    Spawn(p[Pick(rng, AllPebbles)], under, pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d, Range(rng, 0, 360), Range(rng, 0.8f, 1.5f), 0f, 0.3f);
                }
                n++;
            }
        }

        static void PlacePondDetails(Dictionary<string, GameObject> p, Transform env, System.Random rng)
        {
            var pond = new GameObject("PondDetails").transform;
            pond.SetParent(env, false);
            var center = ForestTerrain.Pond;
            float rim = ForestTerrain.PondRadius;
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.PI * (0.05f + 0.9f * i / 8f) + Range(rng, -0.1f, 0.1f) + Mathf.PI; // the near (viewer-facing) side
                var pos = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range(rng, rim * 1.05f, rim * 1.35f);
                Spawn(p[Pick(rng, AllRocks)], pond, pos, Range(rng, 0, 360), Range(rng, 0.6f, 1.2f), 8f, 0.2f);
            }
            for (int i = 0; i < 34; i++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                var pos = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range(rng, rim * 0.95f, rim * 1.5f);
                if (ForestTerrain.IsWater(pos.x, pos.y)) continue;
                Spawn(p[Pick(rng, AllPebbles)], pond, pos, Range(rng, 0, 360), Range(rng, 0.8f, 1.6f), 0f, 0.3f);
            }
            var fernWeights = new[] { 3f, 1f, 1f, 1f, 1f };
            for (int i = 0; i < 28; i++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                var pos = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range(rng, rim * 1.0f, rim * 1.55f);
                if (ForestTerrain.IsWater(pos.x, pos.y)) continue;
                Spawn(p[PickWeighted(rng, Ferns, fernWeights)], pond, pos, Range(rng, 0, 360), Range(rng, 0.9f, 1.5f), 4f, 0f);
            }
            for (int i = 0; i < 10; i++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                var pos = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range(rng, rim * 1.1f, rim * 1.6f);
                if (ForestTerrain.IsWater(pos.x, pos.y)) continue;
                Spawn(p[Pick(rng, Blooms)], pond, pos, Range(rng, 0, 360), Range(rng, 0.9f, 1.4f), 5f, 0f);
            }
        }

        static Transform BuildSquirrel(Dictionary<string, GameObject> p, Transform env)
        {
            var seat = Spawn(p["Rock_Medium_2"], env, RockSeat, 20f, 0.8f, 0f, 0.04f);
            var bounds = RenderBounds(seat);

            var anchor = new GameObject("SquirrelAnchor").transform;
            anchor.SetParent(env, false);
            anchor.position = new Vector3(bounds.center.x, bounds.max.y - 0.02f, bounds.center.z);
            var toViewer = new Vector2(Viewer.x - anchor.position.x, Viewer.z - anchor.position.z);
            anchor.rotation = Quaternion.Euler(0f, Mathf.Atan2(toViewer.x, toViewer.y) * Mathf.Rad2Deg, 0f);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SquirrelModel);
            var squirrel = (GameObject)PrefabUtility.InstantiatePrefab(model, anchor);
            squirrel.transform.localScale = Vector3.one * 1.6f; // storybook scale so it reads at ~5 m
            foreach (var r in squirrel.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = r.sharedMaterials.Select(m =>
                {
                    string n = m != null ? m.name : "";
                    return n.Contains("Dark") ? Lit("SquirrelDark", new Color(0.05f, 0.04f, 0.04f), 0.35f)
                         : n.Contains("Belly") ? Lit("SquirrelBelly", new Color(0.85f, 0.75f, 0.62f), 0.08f)
                         : Lit("SquirrelFur", new Color(0.52f, 0.30f, 0.15f), 0.08f);
                }).ToArray();
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            squirrel.AddComponent<SquirrelIdle>();
            return anchor;
        }

        // ---------------------------------------------------------------- lighting, effects

        static Light BuildLighting()
        {
            var go = new GameObject("Sun");
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.intensity = 0.9f;
            sun.transform.rotation = Quaternion.Euler(12f, 30f, 0f); // SceneConfigApplier takes over (day / sunset)
            return sun;
        }

        static void ApplyRenderSettings(Material sky, Light sun)
        {
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.5f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.78f, 0.74f, 0.66f);
            RenderSettings.fogDensity = 0.0125f; // fog_density 0.25 * SceneConfigApplier.fogDensityScale; the applier overrides it
            DynamicGI.UpdateEnvironment();
        }

        static ParticleSystem BuildParticles(Transform parent, string name, Vector3 center, Vector3 box, float rate, float sizeMin, float sizeMax, Color color, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 10f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = color;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.2f;
            noise.scrollSpeed = 0.1f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GlowMaterial();
            return ps;
        }

        static Material GlowMaterial()
        {
            const string key = "ForestGlow";
            if (MaterialCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var tex = ForestTerrain.SoftDot(GeneratedDir + "/SoftDot.png");
            var m = Recreate($"{MaterialDir}/{key}.mat", "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            // Additive transparent: slow, soft points. If the editor shows it opaque, set Surface = Transparent, Blend = Additive.
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            MaterialCache[key] = m;
            return m;
        }

        static Transform BuildOrb(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "BreathingOrb";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, 1.45f, -6.5f); // 3.5 m ahead of the viewer, eye height
            go.transform.localScale = Vector3.one * 0.3f;
            var m = Recreate(MaterialDir + "/ForestOrb.mat", "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", new Color(0.72f, 0.9f, 1f));
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        // ---------------------------------------------------------------- controller wiring

        static GameObject BuildController(Transform audioRoot, Light sun, Material sky, Transform anchor, ParticleSystem fireflies,
            ParticleSystem pollen, Transform orb)
        {
            var controller = new GameObject("ForestController");
            var applier = controller.AddComponent<SceneConfigApplier>();
            controller.AddComponent<ForestSceneApplier>(); // ISceneApplier seam used by RelaxationSceneLoader

            var so = new SerializedObject(applier);
            so.FindProperty("sun").objectReferenceValue = sun;
            so.FindProperty("skyboxMaterial").objectReferenceValue = sky;
            so.FindProperty("squirrelAnchor").objectReferenceValue = anchor.gameObject;
            so.FindProperty("fireflies").objectReferenceValue = fireflies;
            so.FindProperty("pollen").objectReferenceValue = pollen;
            so.FindProperty("orb").objectReferenceValue = orb;

            var layers = new[]
            {
                ("wind", ForestAudioSynth.Wind(AudioDir), false, Vector3.zero),
                ("birds", ForestAudioSynth.Birds(AudioDir), false, Vector3.zero),
                ("pad", ForestAudioSynth.Pad(AudioDir), false, Vector3.zero),
                ("water", ForestAudioSynth.Water(AudioDir), true, new Vector3(ForestTerrain.Pond.x, 0f, ForestTerrain.Pond.y)),
            };
            foreach (var (field, clip, spatial, pos) in layers)
            {
                var go = new GameObject("Audio_" + field);
                go.transform.SetParent(audioRoot, false);
                go.transform.position = pos;
                var src = go.AddComponent<AudioSource>();
                src.clip = clip;
                src.loop = true;
                src.playOnAwake = false;
                src.volume = 0f; // SceneConfigApplier fades in
                src.spatialBlend = spatial ? 1f : 0f;
                if (spatial) { src.minDistance = 4f; src.maxDistance = 45f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.dopplerLevel = 0f; }
                so.FindProperty(field).objectReferenceValue = src;
            }

            var idle = anchor.GetComponentInChildren<SquirrelIdle>();
            var motion = so.FindProperty("ambientMotionTargets");
            motion.arraySize = idle != null ? 1 : 0;
            if (idle != null) motion.GetArrayElementAtIndex(0).objectReferenceValue = idle;
            so.ApplyModifiedPropertiesWithoutUndo();
            return controller;
        }

        // ---------------------------------------------------------------- report

        static void Report(MeshFilter ground, MeshFilter water)
        {
            long tris = 0;
            int renderers = 0;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                renderers++;
                for (int i = 0; i < mesh.subMeshCount; i++) tris += mesh.GetIndexCount(i) / 3;
            }
            Debug.Log($"[ForestSceneBuilder] ForestDemo written: {renderers} mesh renderers, {tris} triangles before culling " +
                      "(measure frame time on the Quest Pro; no performance figure has been recorded).");
            if (UnresolvedTextures.Count > 0)
                Debug.LogWarning("[ForestSceneBuilder] no texture found for materials (plain colour used): " + string.Join(", ", UnresolvedTextures));
        }
    }
}

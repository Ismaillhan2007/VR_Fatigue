using System.IO;
using UnityEditor;
using UnityEngine;

namespace App.Relaxation.Editor
{
    /// <summary>
    /// Ground shape and texture for ForestDemo. One height function drives the mesh, the object placement and the
    /// painted albedo, so nothing floats or sinks. The ground is a 64 m mesh centred on the origin; a flat far plane
    /// continues it to the horizon (see ForestSceneBuilder). Viewer stands at (0, -10) looking along +Z.
    /// </summary>
    public static class ForestTerrain
    {
        public const float Size = 64f;
        public const float EdgeBlend = 9f;
        public static readonly Vector2 Viewer = new Vector2(0f, -10f);
        public static readonly Vector2 Pond = new Vector2(3f, 4f);
        public const float PondRadius = 4.2f;
        public const float WaterLevel = -0.2f;

        static readonly Color GrassDeep = new Color(0.17f, 0.31f, 0.12f);
        static readonly Color GrassLight = new Color(0.30f, 0.44f, 0.16f);
        static readonly Color Moss = new Color(0.14f, 0.27f, 0.14f);
        static readonly Color Soil = new Color(0.28f, 0.20f, 0.13f);
        public static readonly Color FarColor = new Color(0.20f, 0.33f, 0.14f);

        public static float Fbm(float x, float z)
        {
            float v = 0f, a = 0.5f, f = 1f;
            for (int i = 0; i < 4; i++)
            {
                v += a * Mathf.PerlinNoise(x * f, z * f);
                f *= 2.03f; a *= 0.5f;
            }
            return Mathf.Clamp01(v / 0.9375f);
        }

        static float EdgeFactor(float x, float z)
        {
            float edge = Mathf.Min(Size * 0.5f - Mathf.Abs(x), Size * 0.5f - Mathf.Abs(z));
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / EdgeBlend));
        }

        public static float Height(float x, float z)
        {
            float h = (Fbm(x * 0.045f + 13.1f, z * 0.045f + 7.7f) - 0.5f) * 1.7f;
            h += (Fbm(x * 0.15f + 3.3f, z * 0.15f + 91.4f) - 0.5f) * 0.3f;
            var p = new Vector2(x, z);
            h *= Mathf.SmoothStep(0f, 1f, (Vector2.Distance(p, Viewer) - 3f) / 6f); // flat around the viewer
            float basin = 1f - Mathf.SmoothStep(0f, 1f, (Vector2.Distance(p, Pond) - PondRadius * 0.55f) / (PondRadius * 0.95f));
            h = Mathf.Lerp(h, -0.6f, basin);
            return h * EdgeFactor(x, z); // meets the flat far plane at y = 0
        }

        public static bool IsWater(float x, float z) => Height(x, z) < WaterLevel - 0.05f;

        public static Mesh BuildGroundMesh(int n)
        {
            var verts = new Vector3[(n + 1) * (n + 1)];
            var uv = new Vector2[verts.Length];
            for (int iz = 0; iz <= n; iz++)
                for (int ix = 0; ix <= n; ix++)
                {
                    float x = (ix / (float)n - 0.5f) * Size, z = (iz / (float)n - 0.5f) * Size;
                    int i = iz * (n + 1) + ix;
                    verts[i] = new Vector3(x, Height(x, z), z);
                    uv[i] = new Vector2(ix / (float)n, iz / (float)n);
                }
            var tris = new int[n * n * 6];
            int t = 0;
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    int i = iz * (n + 1) + ix;
                    tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
                }
            var mesh = new Mesh { name = "ForestGround", vertices = verts, uv = uv, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh BuildDisc(float radius, int segments, string name)
        {
            var verts = new Vector3[segments + 1];
            var uv = new Vector2[segments + 1];
            var tris = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                uv[i + 1] = new Vector2(verts[i + 1].x / radius * 0.5f + 0.5f, verts[i + 1].z / radius * 0.5f + 0.5f);
                tris[i * 3] = 0; tris[i * 3 + 1] = (i + 1) % segments + 1; tris[i * 3 + 2] = i + 1; // clockwise from above
            }
            uv[0] = new Vector2(0.5f, 0.5f);
            var mesh = new Mesh { name = name, vertices = verts, uv = uv, triangles = tris };
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>Paints soil, moss and two grass tones over the ground, matching the height field and pond.</summary>
        public static Texture2D PaintAlbedo(string assetPath, int size)
        {
            var px = new Color32[size * size];
            var rng = new System.Random(5);
            for (int py = 0; py < size; py++)
            {
                if (py % 128 == 0) EditorUtility.DisplayProgressBar("ForestDemo", "Painting ground texture", py / (float)size);
                for (int pxI = 0; pxI < size; pxI++)
                {
                    float x = (pxI / (float)(size - 1) - 0.5f) * Size, z = (py / (float)(size - 1) - 0.5f) * Size;
                    float big = Fbm(x * 0.09f + 40f, z * 0.09f + 12f);
                    float mid = Fbm(x * 0.35f + 7f, z * 0.35f + 55f);
                    var c = Color.Lerp(GrassDeep, GrassLight, Mathf.SmoothStep(0.25f, 0.75f, big));
                    c = Color.Lerp(c, Moss, Mathf.SmoothStep(0.55f, 0.85f, Fbm(x * 0.2f + 90f, z * 0.2f + 3f)) * 0.7f);
                    float soil = Mathf.SmoothStep(0.62f, 0.8f, mid) * 0.55f;
                    float dp = Vector2.Distance(new Vector2(x, z), Pond);
                    soil = Mathf.Max(soil, 1f - Mathf.SmoothStep(PondRadius * 0.7f, PondRadius * 1.5f, dp)); // damp mud ring
                    float dv = Vector2.Distance(new Vector2(x, z), Viewer);
                    soil = Mathf.Max(soil, (1f - Mathf.SmoothStep(1.5f, 4.5f, dv)) * 0.5f); // worn spot where the viewer stands
                    c = Color.Lerp(c, Soil, soil);
                    float grain = 1f + ((float)rng.NextDouble() - 0.5f) * 0.08f;
                    c = new Color(c.r * grain, c.g * grain, c.b * grain);
                    c = Color.Lerp(FarColor, c, EdgeFactor(x, z));
                    px[py * size + pxI] = c;
                }
            }
            EditorUtility.ClearProgressBar();
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = size;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// <summary>Soft round sprite used by the firefly / pollen particles.</summary>
        public static Texture2D SoftDot(string assetPath)
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(s * 0.5f - 0.5f, s * 0.5f - 0.5f)) / (s * 0.5f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}

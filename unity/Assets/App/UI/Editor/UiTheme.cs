using System.IO;
using UnityEditor;
using UnityEngine;

namespace App.UI.Editor
{
    /// <summary>
    /// Visual tokens for the generated assessment UI: the velt.dev vocabulary (rounded cards, solid primary buttons,
    /// bold type) in a calm dark palette. Large white panels glared in the headset, and this app asks about eye strain,
    /// so cards are deep green-grey with soft off-white text and a sage accent. Editor-only: AssessmentSceneBuilder bakes
    /// these into the prefabs. The rounded sprite is generated here (no binary art is committed by hand).
    /// </summary>
    public static class UiTheme
    {
        public const string SpriteDir = "Assets/App/UI/Generated";

        public static readonly Color Card = Hex("17221E");        // panel background
        public static readonly Color Surface = Hex("25332D");     // secondary buttons, unselected chips and numbers, inputs
        public static readonly Color Bar = Hex("0E1512");         // provenance bar and pause overlay
        public static readonly Color Text = Hex("EEF2EC");
        public static readonly Color Muted = Hex("A9B9AF");
        public static readonly Color Accent = Hex("8ED1B0");      // primary action and selection
        public static readonly Color AccentText = Hex("0E1A14");  // text on the accent
        public static readonly Color Probe = Hex("FFD84D");       // the yellow target the instructions name
        public static readonly Color Background = Hex("0B110E");  // camera clear colour behind the panels

        public const int CornerRadius = 16;                      // canvas units (about 16 mm at 0.001 m per unit)
        const int SpriteSize = 64;

        /// <summary>White rounded rectangle; tint it through Image.color.</summary>
        public static Sprite Fill { get; private set; }

        public static Color Hex(string rgb)
        {
            ColorUtility.TryParseHtmlString("#" + rgb, out var c);
            return c;
        }

        public static void EnsureSprites()
        {
            Directory.CreateDirectory(SpriteDir);
            Fill = Make("RoundedFill");
        }

        static Sprite Make(string name)
        {
            var path = $"{SpriteDir}/{name}.png";
            var tex = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
            float half = SpriteSize * 0.5f;
            for (int y = 0; y < SpriteSize; y++)
                for (int x = 0; x < SpriteSize; x++)
                {
                    // signed distance to a rounded rectangle (negative inside)
                    float px = Mathf.Abs(x + 0.5f - half) - (half - CornerRadius);
                    float py = Mathf.Abs(y + 0.5f - half) - (half - CornerRadius);
                    float d = Mathf.Sqrt(Mathf.Max(px, 0f) * Mathf.Max(px, 0f) + Mathf.Max(py, 0f) * Mathf.Max(py, 0f))
                              + Mathf.Min(Mathf.Max(px, py), 0f) - CornerRadius;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));
                }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.spriteBorder = new Vector4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed; // crisp corners; 64x64 costs nothing
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}

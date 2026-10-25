using System.IO;
using UnityEditor;
using UnityEngine;

namespace App.UI.Editor
{
    /// <summary>
    /// Visual tokens for the generated assessment UI, taken from the velt.dev marketing site (measured from its computed
    /// styles): warm near-black ink, off-white paper, hairline warm-grey borders, 12-unit rounded cards, solid ink
    /// buttons with an outlined secondary, bold type. Editor-only: AssessmentSceneBuilder bakes these into the prefabs.
    /// The two rounded sprites are generated here (no binary art is committed by hand).
    /// </summary>
    public static class UiTheme
    {
        public const string SpriteDir = "Assets/App/UI/Generated";

        public static readonly Color Ink = Hex("26251E");        // velt button / text colour
        public static readonly Color InkHover = Hex("3D3B30");
        public static readonly Color Paper = Hex("F7F7F4");      // velt light text on ink
        public static readonly Color PaperHover = Hex("ECEBE5");
        public static readonly Color Card = Color.white;
        public static readonly Color Border = Hex("D9D5CF");     // velt card border
        public static readonly Color Muted = Hex("5A5953");      // darker than velt's grey: contrast for headset reading
        public static readonly Color Accent = Hex("D95D2B");     // velt orange badges
        public static readonly Color AccentHover = Hex("E8743F");
        public static readonly Color Disabled = Hex("B5B2AB");

        public const int CornerRadius = 16;                      // canvas units (about 16 mm at 0.001 m per unit)
        const int SpriteSize = 64;
        const int RingWidth = 3;

        /// <summary>White rounded rectangle; tint it through Image.color or a Button ColorBlock.</summary>
        public static Sprite Fill { get; private set; }

        /// <summary>White rounded rectangle with a baked Ink ring (secondary buttons, toggles, inputs).</summary>
        public static Sprite Outline { get; private set; }

        /// <summary>White rounded rectangle with a baked Border-grey ring (panels).</summary>
        public static Sprite CardOutline { get; private set; }

        public static Color Hex(string rgb)
        {
            ColorUtility.TryParseHtmlString("#" + rgb, out var c);
            return c;
        }

        public static void EnsureSprites()
        {
            Directory.CreateDirectory(SpriteDir);
            Fill = Make("RoundedFill", null);
            Outline = Make("RoundedOutline", Ink);
            CardOutline = Make("RoundedCard", Border);
        }

        static Sprite Make(string name, Color? ring)
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
                    float alpha = Mathf.Clamp01(0.5f - d);
                    var c = Color.white;
                    if (ring.HasValue && d > -RingWidth) c = Color.Lerp(Color.white, ring.Value, Mathf.Clamp01(d + RingWidth));
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, alpha));
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

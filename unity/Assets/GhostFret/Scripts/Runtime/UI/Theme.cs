using UnityEngine;

namespace GhostFret.Runtime.UI
{
    /// <summary>
    /// Colours, sizes and motion from docs/UX_GUIDE.md. Every visual uses these so the app feels like one piece.
    /// Colour is never the only signal: pair Good with "✓" and Hint with "!".
    /// </summary>
    public static class Theme
    {
        public static readonly Color Ghost = new Color(0.35f, 0.88f, 1f, 0.75f);     // translucent cyan finger dots
        public static readonly Color Good = new Color(0.45f, 0.90f, 0.55f, 1f);      // soft green + ✓
        public static readonly Color Hint = new Color(1f, 0.72f, 0.28f, 1f);         // warm amber + ! (never red)
        public static readonly Color FretLine = new Color(1f, 1f, 1f, 0.5f);         // frets slightly brighter
        public static readonly Color StringLine = new Color(1f, 1f, 1f, 0.4f);       // strings at 40%
        public static readonly Color PanelBg = new Color(0.07f, 0.09f, 0.12f, 0.95f); // dark glass, nearly opaque so cards behind never show through in passthrough
        public static readonly Color ButtonBg = new Color(0.16f, 0.20f, 0.26f, 0.92f);
        public static readonly Color ButtonHover = new Color(0.22f, 0.32f, 0.40f, 0.95f);
        public static readonly Color Text = new Color(0.95f, 0.96f, 0.98f, 1f);
        public static readonly Color TextDim = new Color(0.70f, 0.75f, 0.80f, 1f);

        // Plain characters only: the built-in TMP font (LiberationSans SDF) has no ✓ or ○ glyphs,
        // which showed as empty boxes on the headset. Good messages carry their own words ("Clean Em!").
        public const string GoodMark = "";
        public const string HintMark = "!";
        public const string SkipMark = "×";
        public const string OpenMark = "o";

        // Text heights in metres (readable at ~0.5 m).
        public const float TitleHeight = 0.028f;
        public const float BodyHeight = 0.018f;
        public const float SmallHeight = 0.013f;

        /// <summary>All motion eases over 200–300 ms; nothing snaps or flashes.</summary>
        public const float EaseSeconds = 0.25f;

        /// <summary>User-adjustable overlay brightness (accessibility). 1 = default.</summary>
        public static float OverlayBrightness = 1f;

        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public static Color WithAlpha(Color c, float a) { return new Color(c.r, c.g, c.b, a); }

        private static Shader _shader;

        /// <summary>
        /// Unlit, transparent, double-sided material. "Sprites/Default" ships in every build
        /// (Graphics settings → Always Included Shaders) and works in URP.
        /// </summary>
        public static Material UnlitMaterial(Color colour)
        {
            if (_shader == null) _shader = Shader.Find("Sprites/Default");
            var m = new Material(_shader);
            m.color = colour;
            return m;
        }
    }
}

#if UNITY_EDITOR
using System.Collections.Generic;
using GhostFret.Runtime.UI;
using UnityEngine;

namespace GhostFret.Runtime.Preview
{
    /// <summary>
    /// Editor preview only: warm, muted colours for the stand-in guitar and room, and one lit
    /// material per colour (URP Lit, falling back to Standard, then the app's unlit material).
    /// </summary>
    public static class Palette
    {
        public static readonly Color Spruce = new Color(0.78f, 0.58f, 0.34f);
        public static readonly Color Mahogany = new Color(0.33f, 0.16f, 0.08f);
        public static readonly Color NeckWood = new Color(0.42f, 0.23f, 0.11f);
        public static readonly Color Rosewood = new Color(0.14f, 0.08f, 0.05f);
        public static readonly Color Tortoise = new Color(0.20f, 0.09f, 0.04f);
        public static readonly Color Hole = new Color(0.03f, 0.025f, 0.02f);
        public static readonly Color Bone = new Color(0.90f, 0.86f, 0.76f);
        public static readonly Color Pearl = new Color(0.90f, 0.89f, 0.84f);
        public static readonly Color Nickel = new Color(0.76f, 0.76f, 0.74f);
        public static readonly Color Bronze = new Color(0.74f, 0.56f, 0.33f);
        public static readonly Color Steel = new Color(0.84f, 0.84f, 0.82f);

        public static readonly Color RoomClear = new Color(0.105f, 0.095f, 0.088f, 1f);
        public static readonly Color Floor = new Color(0.20f, 0.175f, 0.15f);
        public static readonly Color Rug = new Color(0.27f, 0.19f, 0.15f);
        public static readonly Color Wall = new Color(0.23f, 0.215f, 0.20f);
        public static readonly Color Ambient = new Color(0.40f, 0.38f, 0.36f);

        private static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();
        private static Shader _lit;

        public static Material Lit(Color colour)
        {
            Material m;
            if (Cache.TryGetValue(colour, out m) && m != null) return m;

            if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit");
            if (_lit == null) _lit = Shader.Find("Standard");
            if (_lit == null) return Theme.UnlitMaterial(colour);

            m = new Material(_lit);
            m.color = colour;
            // Satin finish: metal parts a little shinier than wood.
            float smooth = colour == Nickel || colour == Steel || colour == Bronze ? 0.7f : 0.35f;
            m.SetFloat("_Smoothness", smooth);   // URP
            m.SetFloat("_Glossiness", smooth);   // Built-in Standard
            Cache[colour] = m;
            return m;
        }
    }
}
#endif

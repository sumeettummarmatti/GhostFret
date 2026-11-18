using System;
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.Util;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.UI
{
    /// <summary>
    /// Builds world-space UI in code (no prefabs needed): dark-glass panels, text, poke buttons,
    /// and places them within seated reach. Convention for every panel: transform.forward points
    /// away from the player, so the readable face (local −Z) faces them.
    /// </summary>
    public static class UIKit
    {
        /// <summary>The player's head. Falls back to the world origin at seated eye height.</summary>
        public static Transform Head
        {
            get { return Camera.main != null ? Camera.main.transform : null; }
        }

        public static Vector3 HeadPosition { get { return Head != null ? Head.position : new Vector3(0, 1.2f, 0); } }

        /// <summary>Creates an empty panel root with a dark-glass background quad.</summary>
        public static Transform CreatePanel(string name, Vector2 sizeMeters, Transform parent = null)
        {
            var root = new GameObject(name).transform;
            if (parent != null) root.SetParent(parent, false);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Background";
            UnityEngine.Object.Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(root, false);
            bg.transform.localScale = new Vector3(sizeMeters.x, sizeMeters.y, 1f);
            bg.transform.localPosition = new Vector3(0, 0, 0.002f); // just behind text and buttons
            bg.GetComponent<Renderer>().material = Theme.UnlitMaterial(Theme.PanelBg);
            return root;
        }

        /// <summary>World-space text. heightMeters ≈ capital-letter height.</summary>
        public static TextMeshPro CreateText(Transform parent, string text, float heightMeters, Vector3 localPos, float widthMeters, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            FitText(tmp, heightMeters, widthMeters, heightMeters * TextBoxLines);
            tmp.color = Theme.Text;
            tmp.alignment = align;
            return tmp;
        }

        /// <summary>
        /// Text box height in multiples of the cap height. One line fits at full size; a long line wraps
        /// to two and shrinks a little. Text can never spill into the line above or below (that was the
        /// overlapping-text bug seen on the headset).
        /// </summary>
        public const float TextBoxLines = 2.4f;

        /// <summary>How far cards float above the guitar body (m). 0.05 put them over the strings on device.</summary>
        public static float CardRise = 0.20f;

        /// <summary>
        /// Sizes a TMP text to a fixed box: auto-size down to 50% so long strings fit, then truncate.
        /// Measured on device: TMP world text cap height ≈ fontSize × 0.1 m.
        /// </summary>
        public static void FitText(TextMeshPro tmp, float capHeightMeters, float boxWidth, float boxHeight)
        {
            float max = capHeightMeters * 10f;
            tmp.rectTransform.sizeDelta = new Vector2(boxWidth, boxHeight);
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = max;
            tmp.fontSizeMin = max * 0.5f;
            tmp.fontSize = max;
            tmp.lineSpacing = -10f;   // a little tighter than TMP's default for two-line labels
            tmp.overflowMode = TextOverflowModes.Truncate;   // the built-in font has no "…" glyph
        }

        public static PokeButton CreateButton(Transform parent, string label, Vector3 localPos, Vector2 size, Action onPress)
        {
            var go = new GameObject("Button " + label);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Background";
            UnityEngine.Object.Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(go.transform, false);
            bg.transform.localScale = new Vector3(size.x, size.y, 1f);
            bg.transform.localPosition = new Vector3(0, 0, 0.001f);
            var r = bg.GetComponent<Renderer>();
            r.material = Theme.UnlitMaterial(Theme.ButtonBg);

            var button = go.AddComponent<PokeButton>();
            button.size = size;
            button.Background = r;
            button.Label = CreateText(go.transform, label, Theme.BodyHeight, new Vector3(0, 0, -0.001f), size.x * 0.9f);
            // The label must stay inside its button: box = button minus a small margin.
            FitText(button.Label, Mathf.Min(Theme.BodyHeight, size.y * 0.42f), size.x * 0.9f, size.y * 0.86f);
            if (onPress != null) button.Pressed += onPress;
            return button;
        }

        /// <summary>
        /// Turns a panel to face the player's eyes: tilts up/down toward them (so cards below eye level
        /// don't look slanted) but never rolls, so text stays level.
        /// </summary>
        public static void FacePlayer(Transform t)
        {
            Vector3 away = t.position - HeadPosition;
            if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
            t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>
        /// Seated default: in front of the player, slightly down, within arm's reach (~0.45 m).
        /// Used before the fretboard is fitted and in Air mode.
        /// </summary>
        public static void PlaceInFront(Transform t, float distance = 0.45f, float below = 0.18f, float sideways = 0f)
        {
            Transform head = Head;
            Vector3 fwd = head != null ? head.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            t.position = HeadPosition + fwd * distance + right * sideways + Vector3.down * below;
            FacePlayer(t);
        }

        /// <summary>
        /// Beside the guitar body: past the 12th fret along the neck, lifted above the strings,
        /// so the player reads it without looking away from the guitar.
        /// </summary>
        public static void PlaceBesideGuitar(Transform t, FretboardGeometry g, float alongPastBody = 0.12f, float up = 0.10f)
        {
            Vector3 body = (g.TwelfthLowE + g.NeckAxis * alongPastBody).ToUnity();
            Vector3 lift = (g.SurfaceNormal * up).ToUnity();
            // Lift toward the player if the surface normal points away from them.
            if (Vector3.Dot(lift, HeadPosition - body) < 0f) lift = -lift;
            // Float clearly above the body (not over the strings, where the strumming hand works).
            t.position = body + lift + Vector3.up * CardRise;
            FacePlayer(t);
        }

        /// <summary>Above the headstock (for the tuner): just before the nut along the neck.</summary>
        public static void PlaceAtHeadstock(Transform t, FretboardGeometry g, float beforeNut = 0.10f, float up = 0.08f)
        {
            Vector3 nutMid = ((g.NutLowE + g.NutHighE) * 0.5f).ToUnity();
            Vector3 p = nutMid - g.NeckAxis.ToUnity() * beforeNut + Vector3.up * up;
            t.position = p;
            FacePlayer(t);
        }
    }
}

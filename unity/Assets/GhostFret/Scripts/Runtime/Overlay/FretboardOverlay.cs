using System.Collections.Generic;
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.UI;
using GhostFret.Runtime.Util;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostFret.Runtime.Overlay
{
    /// <summary>
    /// Draws frets (0–12), six strings and inlay markers on the real neck (DESIGN.md §10.5, UX_GUIDE.md):
    /// thin soft-white lines, frets a little brighter than strings, lifted a few millimetres toward the
    /// player so they never flicker inside the wood. Works for any FretboardGeometry (calibrated or the
    /// virtual Air-mode guitar). Everything fades in/out over Theme.EaseSeconds; refits glide.
    ///
    /// String feedback: HighlightString = a soft, eased pulse for a few seconds (e.g. amber "muted");
    /// GlowString = steady glow until ClearGlow (e.g. "strum these strings"). Colour is never alone:
    /// Theme.Good adds a "✓" and Theme.Hint adds a "!" beside that string.
    /// </summary>
    public sealed class FretboardOverlay : MonoBehaviour
    {
        public int fretCount = 12;
        public float liftMeters = 0.003f;
        public float fretWidth = 0.0014f;
        public float nutLineWidth = 0.0024f;
        public float stringWidthLowE = 0.0011f;
        public float stringWidthHighE = 0.0006f;
        public float highlightWidth = 0.0035f;
        public float markerDiameter = 0.006f;
        public float labelHeight = 0.0055f;
        /// <summary>Gentle breathing period for highlight pulses (seconds). Slow on purpose: no flashing.</summary>
        public float pulsePeriod = 1.2f;
        public bool showStringLabels = true;

        /// <summary>
        /// Left-handed players calibrate on their own guitar, so the geometry is already mirrored and every
        /// line, label and marker follows it. Kept so the app can store the setting in one place.
        /// </summary>
        public bool LeftHanded;

        public FretboardGeometry Geometry { get; private set; }
        public bool IsVisible { get { return _visible; } }

        private static readonly int[] MarkerFrets = { 3, 5, 7, 9, 12 };
        private static readonly string[] StringNames = { "E", "A", "D", "G", "B", "e" };

        private Transform _root;
        private readonly List<LineRenderer> _frets = new List<LineRenderer>();
        private readonly LineRenderer[] _strings = new LineRenderer[6];
        private readonly LineRenderer[] _highlights = new LineRenderer[6];
        private readonly List<Renderer> _markers = new List<Renderer>();
        private readonly TextMeshPro[] _labels = new TextMeshPro[6];
        private readonly TextMeshPro[] _marks = new TextMeshPro[6];
        private readonly StringFx[] _fx = new StringFx[6];

        // Every drawn point, in a fixed order, so a refit can glide from old to new positions.
        private readonly List<Vector3> _from = new List<Vector3>();
        private readonly List<Vector3> _to = new List<Vector3>();
        private readonly List<Vector3> _cur = new List<Vector3>();
        private float _glide = 1f;

        private bool _visible;
        private float _alpha;

        private struct StringFx
        {
            public bool Glowing;
            public Color GlowColour;
            public float GlowAlpha;
            public Color PulseColour;
            public float PulseStart;
            public float PulseSeconds;
        }

        // ---------- Public API ----------

        public void Show(FretboardGeometry geometry)
        {
            if (geometry == null) return;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            EnsureBuilt();

            bool glide = _visible && _alpha > 0.01f && Geometry != null;
            Geometry = geometry;
            ComputePoints(geometry, _to);
            if (glide && _cur.Count == _to.Count)
            {
                _from.Clear();
                _from.AddRange(_cur);
                _glide = 0f;
            }
            else
            {
                _cur.Clear();
                _cur.AddRange(_to);
                _glide = 1f;
                ApplyPoints();
            }
            _visible = true;
        }

        /// <summary>Fades everything out (including glows).</summary>
        public void Hide()
        {
            _visible = false;
            ClearGlow();
            for (int s = 0; s < 6; s++) _fx[s].PulseSeconds = 0f;
        }

        /// <summary>A soft, eased pulse on one string for a few seconds. 0 = low E … 5 = high E.</summary>
        public void HighlightString(int stringIndex, Color colour, float seconds)
        {
            if (stringIndex < 0 || stringIndex > 5) return;
            EnsureBuilt();
            _fx[stringIndex].PulseColour = colour;
            _fx[stringIndex].PulseStart = Time.unscaledTime;
            _fx[stringIndex].PulseSeconds = Mathf.Max(0.6f, seconds);
        }

        /// <summary>A steady glow on one string until ClearGlow.</summary>
        public void GlowString(int stringIndex, Color colour)
        {
            if (stringIndex < 0 || stringIndex > 5) return;
            EnsureBuilt();
            _fx[stringIndex].Glowing = true;
            _fx[stringIndex].GlowColour = colour;
        }

        /// <summary>Clears every string glow (fades out).</summary>
        public void ClearGlow()
        {
            for (int s = 0; s < 6; s++) _fx[s].Glowing = false;
        }

        public void ClearGlow(int stringIndex)
        {
            if (stringIndex >= 0 && stringIndex < 6) _fx[stringIndex].Glowing = false;
        }

        // ---------- Frame update ----------

        private void Update()
        {
            if (_root == null) return;
            float dt = Time.unscaledDeltaTime;

            _alpha = Mathf.MoveTowards(_alpha, _visible ? 1f : 0f, dt / Theme.EaseSeconds);
            float a = Theme.Ease(_alpha);
            float bright = Mathf.Clamp(Theme.OverlayBrightness, 0.2f, 2.5f);

            if (_glide < 1f)
            {
                _glide = Mathf.Min(1f, _glide + dt / Theme.EaseSeconds);
                float t = Theme.Ease(_glide);
                for (int i = 0; i < _cur.Count; i++) _cur[i] = Vector3.Lerp(_from[i], _to[i], t);
                ApplyPoints();
            }

            for (int f = 0; f < _frets.Count; f++) OverlayDraw.SetLineColour(_frets[f], Scale(Theme.FretLine, a, bright));
            for (int s = 0; s < 6; s++) OverlayDraw.SetLineColour(_strings[s], Scale(Theme.StringLine, a, bright));
            Color marker = Scale(new Color(1f, 1f, 1f, 0.3f), a, bright);
            for (int m = 0; m < _markers.Count; m++) _markers[m].material.color = marker;

            bool labelsOn = showStringLabels;
            for (int s = 0; s < 6; s++)
            {
                _labels[s].alpha = labelsOn ? Mathf.Clamp01(0.75f * a * bright) : 0f;
                UpdateStringFx(s, dt, bright);
            }

            if (a > 0.001f || AnyFx()) FaceTexts();
        }

        private void UpdateStringFx(int s, float dt, float bright)
        {
            StringFx fx = _fx[s];
            fx.GlowAlpha = Mathf.MoveTowards(fx.GlowAlpha, fx.Glowing ? 1f : 0f, dt / Theme.EaseSeconds);

            float pulse = 0f;
            float age = Time.unscaledTime - fx.PulseStart;
            if (fx.PulseSeconds > 0f && age < fx.PulseSeconds)
            {
                // Ease in, breathe gently, ease out. Never a hard on/off.
                float env = Mathf.Min(Theme.Ease(age / Theme.EaseSeconds), Theme.Ease((fx.PulseSeconds - age) / Theme.EaseSeconds));
                float breathe = 0.7f + 0.3f * Mathf.Cos(2f * Mathf.PI * age / pulsePeriod);
                pulse = env * breathe;
            }
            else fx.PulseSeconds = 0f;

            Color colour;
            float alpha;
            if (pulse > 0.001f && pulse >= Theme.Ease(fx.GlowAlpha) * 0.85f)
            {
                colour = fx.PulseColour;
                alpha = pulse;
            }
            else
            {
                colour = fx.GlowColour;
                alpha = Theme.Ease(fx.GlowAlpha) * 0.85f;
            }

            LineRenderer hl = _highlights[s];
            hl.enabled = alpha > 0.001f && Geometry != null;
            OverlayDraw.SetLineColour(hl, Theme.WithAlpha(colour, Mathf.Clamp01(alpha * bright)));

            TextMeshPro mark = _marks[s];
            string symbol = MarkFor(colour);
            mark.text = symbol;
            mark.color = Theme.WithAlpha(colour, Mathf.Clamp01(alpha * 1.15f));
            mark.alpha = symbol.Length > 0 ? Mathf.Clamp01(alpha * 1.15f) : 0f;

            _fx[s] = fx;
        }

        private bool AnyFx()
        {
            for (int s = 0; s < 6; s++) if (_fx[s].Glowing || _fx[s].GlowAlpha > 0f || _fx[s].PulseSeconds > 0f) return true;
            return false;
        }

        /// <summary>Colour is never alone: green gets ✓, amber gets !.</summary>
        private static string MarkFor(Color c)
        {
            if (Close(c, Theme.Good)) return Theme.GoodMark;
            if (Close(c, Theme.Hint)) return Theme.HintMark;
            return "";
        }

        private static bool Close(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.15f;
        }

        private static Color Scale(Color c, float alpha, float bright)
        {
            return Theme.WithAlpha(c, Mathf.Clamp01(c.a * alpha * bright));
        }

        // ---------- Building ----------

        private void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new GameObject("Fretboard Lines").transform;
            _root.SetParent(transform, false);

            for (int f = 0; f <= fretCount; f++)
                _frets.Add(OverlayDraw.Line(_root, "Fret " + f, f == 0 ? nutLineWidth : fretWidth, Theme.FretLine));

            for (int s = 0; s < 6; s++)
            {
                float w = Mathf.Lerp(stringWidthLowE, stringWidthHighE, s / 5f);
                _strings[s] = OverlayDraw.Line(_root, "String " + s, w, Theme.StringLine);
                _highlights[s] = OverlayDraw.Line(_root, "Highlight " + s, highlightWidth, Theme.Ghost);
                _highlights[s].enabled = false;
                _labels[s] = OverlayDraw.Label(_root, StringNames[s], labelHeight, Theme.TextDim);
                _marks[s] = OverlayDraw.Label(_root, "", Theme.SmallHeight * 0.8f, Theme.Good);
                _marks[s].alpha = 0f;
            }

            for (int i = 0; i < MarkerFrets.Length; i++)
            {
                if (MarkerFrets[i] > fretCount) continue;
                int dots = MarkerFrets[i] == 12 ? 2 : 1;
                for (int d = 0; d < dots; d++)
                    _markers.Add(OverlayDraw.Dot(_root, "Marker " + MarkerFrets[i], markerDiameter, new Color(1, 1, 1, 0.3f)));
            }
        }

        /// <summary>Fills every drawn point in a fixed order (see ApplyPoints).</summary>
        private void ComputePoints(FretboardGeometry g, List<Vector3> pts)
        {
            pts.Clear();
            Vector3 up = g.NormalToward(UIKit.HeadPosition.ToNumerics()).ToUnity();
            Vector3 lift = up * liftMeters;
            float end = FretboardGeometry.FretDistance(g.ScaleLength, fretCount) + 0.01f;

            for (int f = 0; f <= fretCount; f++)
            {
                System.Numerics.Vector3 lo, hi;
                g.FretLine(f, out lo, out hi);
                // Frets reach a little past the outer strings, like the real board edge.
                Vector3 across = (hi - lo).ToUnity().normalized * 0.002f;
                pts.Add(lo.ToUnity() - across + lift);
                pts.Add(hi.ToUnity() + across + lift);
            }
            for (int s = 0; s < 6; s++)
            {
                pts.Add(g.StringPoint(s, 0f).ToUnity() + lift);
                pts.Add(g.StringPoint(s, end).ToUnity() + lift);
            }
            for (int s = 0; s < 6; s++)   // highlights sit just above the string lines
            {
                pts.Add(g.StringPoint(s, 0f).ToUnity() + lift * 1.6f);
                pts.Add(g.StringPoint(s, end).ToUnity() + lift * 1.6f);
            }
            for (int i = 0; i < MarkerFrets.Length; i++)
            {
                int fret = MarkerFrets[i];
                if (fret > fretCount) continue;
                float d = 0.5f * (FretboardGeometry.FretDistance(g.ScaleLength, fret - 1) + FretboardGeometry.FretDistance(g.ScaleLength, fret));
                if (fret == 12)
                {
                    pts.Add(g.BoardPoint(1.5f, d).ToUnity() + lift);
                    pts.Add(g.BoardPoint(3.5f, d).ToUnity() + lift);
                }
                else pts.Add(g.BoardPoint(2.5f, d).ToUnity() + lift);
            }
            for (int s = 0; s < 6; s++)   // string names just before the nut (over the headstock end)
                pts.Add(g.StringPoint(s, -0.014f).ToUnity() + up * 0.004f);
            for (int s = 0; s < 6; s++)   // ✓ / ! marks just past the end of each string line
                pts.Add(g.StringPoint(s, end + 0.014f).ToUnity() + up * 0.006f);
        }

        private void ApplyPoints()
        {
            int i = 0;
            for (int f = 0; f < _frets.Count; f++) { _frets[f].SetPosition(0, _cur[i++]); _frets[f].SetPosition(1, _cur[i++]); }
            for (int s = 0; s < 6; s++) { _strings[s].SetPosition(0, _cur[i++]); _strings[s].SetPosition(1, _cur[i++]); }
            for (int s = 0; s < 6; s++) { _highlights[s].SetPosition(0, _cur[i++]); _highlights[s].SetPosition(1, _cur[i++]); }
            for (int m = 0; m < _markers.Count; m++) _markers[m].transform.position = _cur[i++];
            for (int s = 0; s < 6; s++) _labels[s].transform.position = _cur[i++];
            for (int s = 0; s < 6; s++) _marks[s].transform.position = _cur[i++];
        }

        private void FaceTexts()
        {
            for (int s = 0; s < 6; s++)
            {
                OverlayDraw.Billboard(_labels[s].transform);
                OverlayDraw.Billboard(_marks[s].transform);
            }
        }
    }

    /// <summary>Small drawing helpers shared by the overlay, ghost hand and calibration visuals.</summary>
    public static class OverlayDraw
    {
        public static LineRenderer Line(Transform parent, string name, float width, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = width;
            lr.numCapVertices = 3;
            lr.material = Theme.UnlitMaterial(Color.white);   // colour comes from vertex colours
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;
            SetLineColour(lr, colour);
            return lr;
        }

        public static void SetLineColour(LineRenderer lr, Color c)
        {
            lr.startColor = c;
            lr.endColor = c;
        }

        /// <summary>A soft, unlit dot (sphere) with no collider or shadows.</summary>
        public static Renderer Dot(Transform parent, string name, float diameter, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * diameter;
            var r = go.GetComponent<Renderer>();
            r.material = Theme.UnlitMaterial(colour);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <summary>Small world-space label (no wrapping), turned to the player each frame by Billboard.</summary>
        public static TextMeshPro Label(Transform parent, string text, float height, Color colour)
        {
            TextMeshPro tmp = UIKit.CreateText(parent, text, height, Vector3.zero, Mathf.Max(0.02f, height * 8f));
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = colour;
            return tmp;
        }

        /// <summary>Turns text fully toward the player's eyes (readable face = local −Z toward them).</summary>
        public static void Billboard(Transform t)
        {
            Vector3 away = t.position - UIKit.HeadPosition;
            if (away.sqrMagnitude < 1e-6f) return;
            t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>A circle (or arc) in the plane facing the player. fill 0..1 draws that fraction, from 12 o'clock.</summary>
        public static void SetRing(LineRenderer lr, Vector3 centre, float radius, float fill, int segments = 48)
        {
            Vector3 toHead = UIKit.HeadPosition - centre;
            if (toHead.sqrMagnitude < 1e-6f) toHead = Vector3.back;
            Quaternion q = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
            int n = Mathf.Max(0, Mathf.CeilToInt(segments * Mathf.Clamp01(fill)));
            lr.positionCount = n < 1 ? 0 : n + 1;
            for (int i = 0; i <= n && n >= 1; i++)
            {
                float ang = Mathf.Clamp01(fill) * 2f * Mathf.PI * i / n;
                // Clockwise from 12 o'clock as the player sees it.
                Vector3 local = new Vector3(Mathf.Sin(ang), Mathf.Cos(ang), 0f) * radius;
                lr.SetPosition(i, centre + q * local);
            }
        }
    }
}

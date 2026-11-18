using System.Collections.Generic;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Music;
using GhostFret.Runtime.Hands;
using GhostFret.Runtime.UI;
using GhostFret.Runtime.Util;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.Overlay
{
    /// <summary>
    /// Shows where fingers go (DESIGN.md §7.2, UX_GUIDE.md): translucent cyan dots numbered 1–4 at
    /// FretboardGeometry.FingerPosition, breathing gently; "×" above strings to skip, "○" above open
    /// strings; a small chord name above the neck. Offset slightly toward the player so it never looks
    /// "inside" the wood. Works for any geometry (real guitar or the virtual Air-mode guitar).
    ///
    /// MorphTo glides each finger's dot to its new spot (Song Mode calls it one beat early).
    /// Pulse() is a single soft swell for "check the ghost hand" — never a flash.
    ///
    /// Optional finger hints (off by default, rule 4/5): only when hand tracking is confident and a
    /// fretting fingertip has stayed over the wrong fret for a while, a small amber "!" ring marks it.
    /// Audio still decides whether the chord is right.
    /// </summary>
    public sealed class GhostHand : MonoBehaviour
    {
        public float dotDiameter = 0.011f;
        public float haloScale = 1.9f;
        /// <summary>How far dots float above the board, toward the player.</summary>
        public float dotLift = 0.008f;
        public float numberHeight = 0.0065f;
        public float stringMarkHeight = 0.0085f;
        public float chordLabelHeight = Theme.BodyHeight;
        public float breathePeriod = 2.4f;
        public float breatheAmount = 0.06f;
        public float pulseSeconds = 0.8f;
        public float pulseAmount = 0.35f;

        public bool showFingerHints = false;
        public float hintMinConfidence = 0.95f;
        public float hintHoldSeconds = 0.8f;
        public float hintMaxHeight = 0.025f;

        /// <summary>
        /// Left-handed mode. The calibrated geometry is already mirrored (the player touches their own
        /// guitar), so dots and marks follow it; this only picks the fretting hand (right) for hints.
        /// </summary>
        public bool LeftHanded;

        public ChordShape Chord { get; private set; }
        public FretboardGeometry Geometry { get; private set; }
        public bool IsVisible { get { return _visible; } }

        private sealed class Dot
        {
            public Transform Root;
            public Renderer Core;
            public Renderer Halo;
            public TextMeshPro Number;
            public LineRenderer Bar;      // barre: one finger across several strings (e.g. mini F)
            public int Key;
            public Vector3 From, To, Pos;
            public Vector3 BarFromEnd, BarToEnd, BarEnd;
            public bool HasBar, HadBar;
            public float AlphaFrom, AlphaTo, Alpha;
            public bool Alive;
        }

        private sealed class FadingText
        {
            public TextMeshPro Text;
            public string Target = "";
            public Color TargetColour;
            public float Alpha;
        }

        private Transform _root;
        private readonly List<Dot> _dots = new List<Dot>();
        private readonly FadingText[] _stringMarks = new FadingText[6];
        private FadingText _chordLabel;
        private readonly Vector3[] _markPos = new Vector3[6];
        private Vector3 _labelPos;

        private bool _visible;
        private float _alpha;
        private float _morphT = 1f;
        private float _morphSeconds = Theme.EaseSeconds;
        private float _pulseStart = -100f;

        private LineRenderer _hintRing;
        private TextMeshPro _hintMark;
        private float _hintAlpha;
        private float _wrongSince = -1f;
        private int _wrongFinger;

        // ---------- Public API ----------

        public void ShowChord(ChordShape chord, FretboardGeometry geometry)
        {
            if (chord == null || geometry == null) return;
            bool wasShowing = _visible && _alpha > 0.01f && Chord != null;
            MorphTo(chord, geometry, wasShowing ? Theme.EaseSeconds * 1.2f : 0f);
        }

        public void Hide()
        {
            _visible = false;
        }

        /// <summary>Glide the dots from the current chord to the next one over `seconds`.</summary>
        public void MorphTo(ChordShape next, FretboardGeometry geometry, float seconds)
        {
            if (next == null || geometry == null || next.frets == null || next.frets.Length < 6) return;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            EnsureBuilt();

            bool instant = seconds <= 0.01f || !_visible || _alpha < 0.01f;
            Chord = next;
            Geometry = geometry;
            _visible = true;
            _morphSeconds = Mathf.Max(0.05f, seconds);
            _morphT = instant ? 1f : 0f;

            Vector3 up = geometry.NormalToward(UIKit.HeadPosition.ToNumerics()).ToUnity();

            // Mark every current dot as leaving; targets below revive the ones that stay.
            for (int i = 0; i < _dots.Count; i++)
            {
                Dot d = _dots[i];
                d.From = d.Pos;
                d.AlphaFrom = instant ? 0f : d.Alpha;
                d.AlphaTo = 0f;
                d.To = d.Pos;
                d.BarFromEnd = d.BarEnd;
                d.HadBar = d.HasBar && d.Alpha > 0.01f;
                d.HasBar = false;
            }

            int[] seen = new int[6];
            for (int s = 0; s < 6; s++)
            {
                int fret = next.frets[s];
                if (fret <= 0) continue;
                int finger = next.fingers != null && next.fingers.Length > s ? next.fingers[s] : 0;
                Vector3 target = geometry.FingerPosition(s, fret).ToUnity() + up * dotLift;

                // Barre: the same finger already placed at this fret on a lower string → extend that dot's bar.
                if (finger >= 1 && finger <= 4 && seen[finger] > 0)
                {
                    Dot first = FindKey(finger * 10);
                    if (first != null && Chord.frets[FirstStringOf(next, finger)] == fret)
                    {
                        first.HasBar = true;
                        first.BarToEnd = target;
                    }
                }

                int key = finger >= 1 && finger <= 4 ? finger * 10 + seen[finger] : 100 + s;
                if (finger >= 1 && finger <= 4) seen[finger]++;

                Dot dot = FindKey(key);
                if (dot == null || !dot.Alive)
                {
                    if (dot == null) dot = NewDot(key);
                    dot.From = target;            // appears in place and fades in
                    dot.AlphaFrom = 0f;
                    dot.Alive = true;
                    dot.Root.gameObject.SetActive(true);
                }
                if (instant) dot.From = target;
                dot.To = target;
                dot.AlphaTo = 1f;
                dot.Number.text = finger >= 1 && finger <= 4 ? finger.ToString() : "";
            }

            for (int i = 0; i < _dots.Count; i++)
            {
                Dot d = _dots[i];
                if (!d.HasBar) d.BarToEnd = d.To;
                if (!d.HadBar) d.BarFromEnd = d.From;
                if (instant) d.BarFromEnd = d.BarToEnd;
            }

            // × above strings to skip, ○ above open strings, just before the nut.
            for (int s = 0; s < 6; s++)
            {
                int fret = next.frets[s];
                _markPos[s] = geometry.StringPoint(s, -0.011f).ToUnity() + up * (dotLift + 0.002f);
                _stringMarks[s].Target = fret < 0 ? Theme.SkipMark : fret == 0 ? Theme.OpenMark : "";
                _stringMarks[s].TargetColour = fret < 0 ? Theme.TextDim : Theme.WithAlpha(Theme.Ghost, 1f);
                if (instant) _stringMarks[s].Text.text = _stringMarks[s].Target;
            }

            _labelPos = ChordLabelPosition(geometry, up);
            _chordLabel.Target = string.IsNullOrEmpty(next.name) ? "" : next.name;
            _chordLabel.TargetColour = Theme.Text;
            if (instant) _chordLabel.Text.text = _chordLabel.Target;

            if (instant) for (int i = 0; i < _dots.Count; i++) Step(_dots[i], 1f);
        }

        /// <summary>One soft swell of the dots (for "That sounds like G, not Em. Check the ghost hand.").</summary>
        public void Pulse()
        {
            _pulseStart = Time.unscaledTime;
        }

        // ---------- Frame update ----------

        private void Update()
        {
            if (_root == null) return;
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            _alpha = Mathf.MoveTowards(_alpha, _visible ? 1f : 0f, dt / Theme.EaseSeconds);
            float master = Theme.Ease(_alpha) * Mathf.Clamp(Theme.OverlayBrightness, 0.3f, 1.6f);

            if (_morphT < 1f) _morphT = Mathf.Min(1f, _morphT + dt / _morphSeconds);
            float t = Theme.Ease(_morphT);

            float breathe = 1f + breatheAmount * Mathf.Sin(2f * Mathf.PI * now / breathePeriod);
            float pulseAge = now - _pulseStart;
            float pulse = pulseAge >= 0f && pulseAge < pulseSeconds ? Mathf.Sin(Mathf.PI * pulseAge / pulseSeconds) : 0f;
            float scale = breathe + pulseAmount * pulse;

            for (int i = 0; i < _dots.Count; i++)
            {
                Dot d = _dots[i];
                if (!d.Alive) continue;
                Step(d, t);
                float a = d.Alpha * master;
                d.Root.localScale = Vector3.one * scale;
                d.Core.material.color = Theme.WithAlpha(Theme.Ghost, Mathf.Clamp01(Theme.Ghost.a * a));
                d.Halo.material.color = Theme.WithAlpha(Theme.Ghost, Mathf.Clamp01((0.18f + 0.22f * pulse) * a));
                d.Number.alpha = Mathf.Clamp01(a);
                PlaceNumber(d);

                bool barOn = (d.HasBar || d.HadBar) && a > 0.001f;
                d.Bar.enabled = barOn;
                if (barOn)
                {
                    float barAlpha = d.HasBar && d.HadBar ? 1f : d.HasBar ? t : 1f - t;
                    OverlayDraw.SetLineColour(d.Bar, Theme.WithAlpha(Theme.Ghost, 0.35f * barAlpha * a));
                    d.Bar.SetPosition(0, d.Pos);
                    d.Bar.SetPosition(1, d.BarEnd);
                }

                if (_morphT >= 1f && d.AlphaTo <= 0f)
                {
                    d.Alive = false;
                    d.Root.gameObject.SetActive(false);
                    d.Bar.enabled = false;
                }
            }

            for (int s = 0; s < 6; s++) UpdateFadingText(_stringMarks[s], _markPos[s], master, dt);
            UpdateFadingText(_chordLabel, _labelPos, master, dt);

            UpdateHint(dt, master);
        }

        private void Step(Dot d, float t)
        {
            d.Pos = Vector3.Lerp(d.From, d.To, t);
            d.Alpha = Mathf.Lerp(d.AlphaFrom, d.AlphaTo, t);
            d.BarEnd = Vector3.Lerp(d.BarFromEnd, d.BarToEnd, t);
            d.Root.position = d.Pos;
        }

        /// <summary>Text fades out, swaps, then fades in — so marks never snap.</summary>
        private void UpdateFadingText(FadingText f, Vector3 pos, float master, float dt)
        {
            float speed = dt / (Theme.EaseSeconds * 0.5f);
            if (f.Text.text != f.Target)
            {
                f.Alpha = Mathf.MoveTowards(f.Alpha, 0f, speed);
                if (f.Alpha <= 0f) f.Text.text = f.Target;
            }
            else f.Alpha = Mathf.MoveTowards(f.Alpha, f.Target.Length > 0 ? 1f : 0f, speed);

            f.Text.transform.position = pos;
            OverlayDraw.Billboard(f.Text.transform);
            f.Text.color = Theme.WithAlpha(f.TargetColour, Mathf.Clamp01(Theme.Ease(f.Alpha) * master));
        }

        private void PlaceNumber(Dot d)
        {
            Vector3 toHead = UIKit.HeadPosition - d.Pos;
            float r = dotDiameter * 0.5f * d.Root.localScale.x;
            d.Number.transform.position = d.Pos + toHead.normalized * (r + 0.0012f);
            OverlayDraw.Billboard(d.Number.transform);
        }

        /// <summary>Above the neck's upper edge around fret 2–3, never on the strings.</summary>
        private Vector3 ChordLabelPosition(FretboardGeometry g, Vector3 up)
        {
            Vector3 across = g.AcrossAxis.ToUnity();
            bool lowEdgeIsUp = Vector3.Dot(-across, Vector3.up) >= Vector3.Dot(across, Vector3.up);
            Vector3 edgeDir = lowEdgeIsUp ? -across : across;
            float d = FretboardGeometry.FretDistance(g.ScaleLength, 2);
            Vector3 edge = g.StringPoint(lowEdgeIsUp ? 0 : 5, d).ToUnity();
            return edge + edgeDir * 0.026f + up * 0.012f;
        }

        // ---------- Optional finger hints ----------

        private void UpdateHint(float dt, float master)
        {
            bool show = false;
            Vector3 tip = Vector3.zero;
            IHandInput hands = HandsHub.Input;

            if (showFingerHints && _visible && Chord != null && Geometry != null && hands != null)
            {
                Handedness fretHand = LeftHanded ? Handedness.Right : Handedness.Left;
                int wrong = 0;
                if (hands.IsTracked(fretHand) && hands.Confidence(fretHand) >= hintMinConfidence)
                {
                    for (int s = 0; s < 6 && wrong == 0; s++)
                    {
                        int fret = Chord.frets[s];
                        int finger = Chord.fingers != null && Chord.fingers.Length > s ? Chord.fingers[s] : 0;
                        if (fret <= 0 || finger < 1 || finger > 4) continue;
                        Vector3 p = hands.FingerTip(fretHand, finger);
                        FretboardPoint fp = Geometry.Locate(p.ToNumerics());
                        bool onBoard = Mathf.Abs(fp.Height) < hintMaxHeight && fp.Fret >= 1 && fp.Fret <= 12 &&
                                       fp.StringPosition > -0.5f && fp.StringPosition < 5.5f;
                        // Only a clear miss: a whole fret away, not a fingertip resting on the wire.
                        if (onBoard && Mathf.Abs(fp.Fret - fret) >= 1 && !NearFretWire(fp, fret))
                        {
                            wrong = finger;
                            tip = p;
                        }
                    }
                }

                if (wrong != 0 && wrong == _wrongFinger) { if (_wrongSince < 0f) _wrongSince = Time.unscaledTime; }
                else { _wrongFinger = wrong; _wrongSince = wrong != 0 ? Time.unscaledTime : -1f; }
                show = wrong != 0 && Time.unscaledTime - _wrongSince >= hintHoldSeconds;
            }
            else { _wrongFinger = 0; _wrongSince = -1f; }

            _hintAlpha = Mathf.MoveTowards(_hintAlpha, show ? 1f : 0f, dt / Theme.EaseSeconds);
            if (_hintRing == null) return;
            _hintRing.enabled = _hintAlpha > 0.001f;
            _hintMark.alpha = Theme.Ease(_hintAlpha) * master;
            if (show)
            {
                OverlayDraw.SetRing(_hintRing, tip, 0.008f, 1f, 32);
                _hintMark.transform.position = tip + Vector3.up * 0.016f;
                OverlayDraw.Billboard(_hintMark.transform);
            }
            OverlayDraw.SetLineColour(_hintRing, Theme.WithAlpha(Theme.Hint, 0.9f * Theme.Ease(_hintAlpha) * master));
        }

        /// <summary>True if the fingertip is within ~25% of a fret space of the target fret's boundaries.</summary>
        private bool NearFretWire(FretboardPoint fp, int targetFret)
        {
            float lo = FretboardGeometry.FretDistance(Geometry.ScaleLength, targetFret - 1);
            float hi = FretboardGeometry.FretDistance(Geometry.ScaleLength, targetFret);
            float margin = 0.25f * (hi - lo);
            return fp.Along > lo - margin && fp.Along < hi + margin;
        }

        // ---------- Building ----------

        private void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new GameObject("Ghost Hand").transform;
            _root.SetParent(transform, false);

            for (int s = 0; s < 6; s++)
            {
                _stringMarks[s] = new FadingText();
                _stringMarks[s].Text = OverlayDraw.Label(_root, "", stringMarkHeight, Theme.TextDim);
                _stringMarks[s].Text.alpha = 0f;
            }
            _chordLabel = new FadingText();
            _chordLabel.Text = OverlayDraw.Label(_root, "", chordLabelHeight, Theme.Text);
            _chordLabel.Text.fontStyle = FontStyles.Bold;
            _chordLabel.Text.alpha = 0f;

            _hintRing = OverlayDraw.Line(_root, "Finger Hint", 0.0015f, Theme.Hint);
            _hintRing.loop = false;
            _hintRing.enabled = false;
            _hintMark = OverlayDraw.Label(_root, Theme.HintMark, Theme.SmallHeight * 0.7f, Theme.Hint);
            _hintMark.alpha = 0f;
        }

        private Dot NewDot(int key)
        {
            var d = new Dot();
            d.Key = key;
            d.Root = new GameObject("Dot " + key).transform;
            d.Root.SetParent(_root, false);
            d.Halo = OverlayDraw.Dot(d.Root, "Halo", dotDiameter * haloScale, Theme.WithAlpha(Theme.Ghost, 0f));
            d.Core = OverlayDraw.Dot(d.Root, "Core", dotDiameter, Theme.WithAlpha(Theme.Ghost, 0f));
            // Numbers are separate (not scaled with the breathing dot) so they stay crisp.
            d.Number = OverlayDraw.Label(_root, "", numberHeight, new Color(0.04f, 0.10f, 0.14f, 1f));
            d.Number.fontStyle = FontStyles.Bold;
            d.Bar = OverlayDraw.Line(_root, "Barre " + key, dotDiameter * 0.75f, Theme.Ghost);
            d.Bar.enabled = false;
            _dots.Add(d);
            return d;
        }

        private Dot FindKey(int key)
        {
            for (int i = 0; i < _dots.Count; i++) if (_dots[i].Key == key) return _dots[i];
            return null;
        }

        private static int FirstStringOf(ChordShape chord, int finger)
        {
            for (int s = 0; s < 6; s++)
                if (chord.fingers != null && chord.fingers.Length > s && chord.fingers[s] == finger && chord.frets[s] > 0) return s;
            return 0;
        }

        private void OnDisable()
        {
            if (_hintRing != null) _hintRing.enabled = false;
        }
    }
}

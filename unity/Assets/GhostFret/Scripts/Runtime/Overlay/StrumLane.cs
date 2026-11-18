using System.Collections.Generic;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Strum;
using GhostFret.Core.Timing;
using GhostFret.Runtime.Audio;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.Overlay
{
    /// <summary>
    /// Vertical rhythm lane beside the guitar body (DESIGN.md §7.3). Down/up arrows (built from quads,
    /// so no font glyphs are needed) scroll down to a hit line in time with the BeatGrid; the current and
    /// next chord sit on top. Feedback is gentle: green + ✓ when on time, warm amber + ! otherwise, never red.
    /// Everything is built in code; add it with AddComponent and call Play(pattern, grid).
    /// </summary>
    public sealed class StrumLane : MonoBehaviour
    {
        [Header("Layout (metres)")]
        public Vector2 size = new Vector2(0.11f, 0.34f);
        public float lookaheadBeats = 3f;      // how far ahead arrows appear
        public float pastBeats = 0.6f;         // how long they linger below the hit line

        [Header("Feedback")]
        public float judgementSeconds = 1.2f;  // how long a word stays up
        public float glowSeconds = 0.45f;      // hit-line glow on a good strum (eases, never flashes)

        public bool Playing { get; private set; }
        public bool Paused { get; private set; }
        public BeatGrid Grid { get { return _grid; } }
        public StrumPattern Pattern { get { return _pattern; } }

        // Layout inside the panel (local Y up, face toward the player on −Z).
        private float HitY { get { return -size.y * 0.5f + 0.055f; } }
        private float TopY { get { return size.y * 0.5f - 0.075f; } }
        private float UnitsPerBeat { get { return (TopY - HitY) / Mathf.Max(0.5f, lookaheadBeats); } }

        private Transform _visual;
        private TextMeshPro _current, _next, _judgement, _count;
        private Renderer _hitLine, _glow;
        private readonly List<Arrow> _arrowPool = new List<Arrow>();
        private readonly Dictionary<long, Arrow> _arrows = new Dictionary<long, Arrow>();
        private readonly Dictionary<long, Color> _slotState = new Dictionary<long, Color>();
        private readonly List<Renderer> _beatLines = new List<Renderer>();
        private readonly MissedStrumDetector _missed = new MissedStrumDetector();

        private BeatGrid _grid;
        private StrumPattern _pattern;
        private double _pausedBeat;
        private float _judgementAt = -100f, _countAt = -100f, _glowAt = -100f;
        private float _visibleAlpha;
        private Transform _anchor;
        private bool _placed;

        private sealed class Arrow
        {
            public Transform Root;
            public Renderer[] Parts;
            public long Slot;
        }

        private static readonly Color ArrowDown = Theme.Ghost;
        private static readonly Color ArrowUp = new Color(0.62f, 0.94f, 1f, 0.75f);
        private static readonly Color MissedColour = new Color(1f, 1f, 1f, 0.18f);

        // ---------- Public API ----------

        /// <summary>Starts scrolling a strum pattern (e.g. ["D","-","D","U"]) on a beat grid.</summary>
        public void Play(string[] pattern, BeatGrid grid)
        {
            Play(pattern, grid, BeatsPerBarFor(pattern));
        }

        /// <summary>Same, with the time signature's beats per bar (3 for 3/4).</summary>
        public void Play(string[] pattern, BeatGrid grid, int beatsPerBar)
        {
            Build();
            _grid = grid;
            _pattern = StrumPattern.Parse(pattern, beatsPerBar);
            if (_grid != null) _grid.Subdivision = _pattern.SlotsPerBeat;
            _missed.Reset();
            _slotState.Clear();
            ReleaseAllArrows();
            Playing = true;
            Paused = false;
            if (!_placed && _anchor == null) PlaceInFront();
            _visual.gameObject.SetActive(true);
        }

        public void Stop()
        {
            // Arrows and chord names fade out with the lane rather than vanishing.
            Playing = false;
            Paused = false;
        }

        /// <summary>Freeze the arrows where they are (song paused).</summary>
        public void Pause()
        {
            if (!Playing || Paused || _grid == null) return;
            _pausedBeat = _grid.BeatAt(DspClock.Now);
            Paused = true;
        }

        public void Resume() { Paused = false; }

        public void SetChords(string current, string next)
        {
            Build();
            _current.text = current ?? "";
            _next.text = string.IsNullOrEmpty(next) ? "" : "next  " + next;
        }

        /// <summary>Big count-in number (1, 2, 3, 4) in the middle of the lane.</summary>
        public void ShowCount(int number)
        {
            Build();
            _count.text = number.ToString();
            _countAt = Time.unscaledTime;
        }

        /// <summary>Timing only: "On time ✓", "A little early", "A little late".</summary>
        public void ShowJudgement(TimingResult result)
        {
            bool good;
            string text = StrumFeedback.ForTiming(result, out good);
            Say(text, good);
            MarkSlot(result, good);
        }

        /// <summary>Timing plus direction: a wrong-way strum gets the one tip ("Try an up-strum here").</summary>
        public void ShowStrum(TimingResult result, StrumDirection heard, StrumDirection expected)
        {
            bool good;
            string text = StrumFeedback.ForStrum(result, heard, expected, out good);
            Say(text, good);
            MarkSlot(result, good);
        }

        /// <summary>Sit beside the guitar body, facing the player.</summary>
        public void PlaceBesideGuitar(FretboardGeometry geometry)
        {
            Build();
            _anchor = null;
            if (geometry == null) { PlaceInFront(); return; }
            UIKit.PlaceBesideGuitar(transform, geometry, 0.14f, 0.12f);
            _placed = true;
        }

        /// <summary>Follow an anchor (e.g. beside the Air guitar).</summary>
        public void PlaceAt(Transform anchor)
        {
            Build();
            _anchor = anchor;
            _placed = anchor != null;
            FollowAnchor();
        }

        /// <summary>Seated default before the guitar is fitted: front-right, within reach.</summary>
        public void PlaceInFront()
        {
            Build();
            _anchor = null;
            UIKit.PlaceInFront(transform, 0.48f, 0.1f, 0.24f);
            _placed = true;
        }

        // ---------- Frame update ----------

        private void Update()
        {
            if (_visual == null) return;
            FollowAnchor();

            // Whole lane eases in/out with Playing.
            float target = Playing ? 1f : 0f;
            _visibleAlpha = Mathf.MoveTowards(_visibleAlpha, target, Time.unscaledDeltaTime / Theme.EaseSeconds);
            if (_visibleAlpha <= 0f && !Playing)
            {
                if (_visual.gameObject.activeSelf)
                {
                    ReleaseAllArrows();
                    SetChords("", "");
                    _visual.gameObject.SetActive(false);
                }
                return;
            }
            float lane = Theme.Ease(_visibleAlpha);

            UpdateTexts(lane);
            UpdateHitLine(lane);
            if (_grid == null || _pattern == null) return;

            double beatNow = Paused ? _pausedBeat : _grid.BeatAt(DspClock.Now);
            UpdateBeatLines(beatNow, lane);
            UpdateArrows(beatNow, lane);

            if (Playing && !Paused)
            {
                double graceBeats = (_grid.OnTimeWindowMs * 2.0 + _grid.LatencyMs + 60.0) / 1000.0 / _grid.SecondsPerBeat;
                long missedSlot;
                if (_missed.CheckMissedUp(_pattern, beatNow, graceBeats, out missedSlot))
                {
                    _slotState[missedSlot] = MissedColour;
                    Say(StrumFeedback.MissedUp, false);
                }
            }
        }

        private void UpdateTexts(float lane)
        {
            _current.alpha = lane;
            _next.alpha = lane * 0.8f;

            float j = Time.unscaledTime - _judgementAt;
            float jAlpha = j < judgementSeconds ? 1f : 1f - (j - judgementSeconds) / Theme.EaseSeconds;
            _judgement.alpha = lane * Theme.Ease(Mathf.Min(1f, j / 0.12f)) * Mathf.Clamp01(jAlpha);

            float c = Time.unscaledTime - _countAt;
            float cAlpha = c < 0.35f ? Theme.Ease(c / 0.12f) : 1f - (c - 0.35f) / 0.25f;
            _count.alpha = lane * Mathf.Clamp01(cAlpha);
        }

        private void UpdateHitLine(float lane)
        {
            _hitLine.material.color = Theme.WithAlpha(Color.white, 0.55f * lane);
            float g = Time.unscaledTime - _glowAt;
            float env = g < 0.08f ? Theme.Ease(g / 0.08f) : Mathf.Clamp01(1f - (g - 0.08f) / glowSeconds);
            env = Theme.Ease(env);
            _glow.material.color = Theme.WithAlpha(Theme.Good, 0.45f * env * lane);
            _glow.transform.localScale = new Vector3(size.x * (0.9f + 0.08f * env), 0.018f + 0.01f * env, 1f);
        }

        private void UpdateBeatLines(double beatNow, float lane)
        {
            int first = Mathf.CeilToInt((float)(beatNow - pastBeats));
            int i = 0;
            for (int b = first; b <= beatNow + lookaheadBeats && i < _beatLines.Count; b++, i++)
            {
                var r = _beatLines[i];
                float y = HitY + (float)(b - beatNow) * UnitsPerBeat;
                bool barStart = ((b % _pattern.BeatsPerBar) + _pattern.BeatsPerBar) % _pattern.BeatsPerBar == 0;
                r.transform.localPosition = new Vector3(0, y, -0.0005f);
                r.transform.localScale = new Vector3(size.x * (barStart ? 0.86f : 0.6f), barStart ? 0.002f : 0.0012f, 1f);
                r.material.color = Theme.WithAlpha(Color.white, (barStart ? 0.28f : 0.12f) * EdgeFade(y) * lane);
                r.enabled = true;
            }
            for (; i < _beatLines.Count; i++) _beatLines[i].enabled = false;
        }

        private void UpdateArrows(double beatNow, float lane)
        {
            int perBeat = _pattern.SlotsPerBeat;
            long sMin = (long)System.Math.Floor((beatNow - pastBeats) * perBeat);
            long sMax = (long)System.Math.Ceiling((beatNow + lookaheadBeats) * perBeat);

            // Release arrows that scrolled out.
            var gone = new List<long>();
            foreach (var pair in _arrows) if (pair.Key < sMin || pair.Key > sMax) gone.Add(pair.Key);
            foreach (long s in gone) { Release(_arrows[s]); _arrows.Remove(s); _slotState.Remove(s); }

            for (long s = sMin; s <= sMax; s++)
            {
                StrumDirection dir = _pattern.At(s);
                if (dir == StrumDirection.None) continue;
                Arrow a;
                if (!_arrows.TryGetValue(s, out a))
                {
                    a = Take();
                    a.Slot = s;
                    _arrows[s] = a;
                    a.Root.localRotation = dir == StrumDirection.Up ? Quaternion.Euler(0, 0, 180f) : Quaternion.identity;
                }

                float y = HitY + (float)(_pattern.SlotBeat(s) - beatNow) * UnitsPerBeat;
                bool onBeat = s % perBeat == 0;
                float scale = onBeat ? 1f : 0.82f;
                a.Root.localPosition = new Vector3(0, y, -0.001f);
                a.Root.localScale = new Vector3(scale, scale, 1f);

                Color baseColour = dir == StrumDirection.Up ? ArrowUp : ArrowDown;
                Color state;
                Color c = _slotState.TryGetValue(s, out state) ? state : baseColour;
                float alpha = c.a * EdgeFade(y) * lane;
                foreach (var r in a.Parts) r.material.color = Theme.WithAlpha(c, alpha);
            }
        }

        // Soft fade at the top (arrows arrive gently) and below the hit line.
        private float EdgeFade(float y)
        {
            float top = Mathf.Clamp01((TopY - y) / 0.04f);
            float bottom = Mathf.Clamp01((y - (HitY - pastBeats * UnitsPerBeat)) / (pastBeats * UnitsPerBeat * 0.8f + 1e-4f));
            return Theme.Ease(top) * Theme.Ease(bottom);
        }

        // ---------- Feedback helpers ----------

        private void Say(string text, bool good)
        {
            Build();
            _judgement.text = (good ? Theme.GoodMark + " " : Theme.HintMark + " ") + text;
            _judgement.color = good ? Theme.Good : Theme.Hint;
            _judgementAt = Time.unscaledTime;
        }

        private void MarkSlot(TimingResult result, bool good)
        {
            if (_pattern == null) return;
            long slot = (long)System.Math.Round(result.NearestSlot * _pattern.SlotsPerBeat);
            _slotState[slot] = good ? Theme.Good : Theme.Hint;
            _missed.MarkHit(slot, result.NearestSlot);
            if (good) _glowAt = Time.unscaledTime;
        }

        private void FollowAnchor()
        {
            if (_anchor == null) return;
            transform.position = _anchor.position;
            UIKit.FacePlayer(transform);
        }

        private static int BeatsPerBarFor(string[] pattern)
        {
            // Without a time signature: 6 items reads as 3/4 eighths, otherwise 4/4.
            return pattern != null && pattern.Length == 6 ? 3 : 4;
        }

        // ---------- Building (all in code) ----------

        private void Build()
        {
            if (_visual != null) return;
            _visual = UIKit.CreatePanel("Strum lane", size, transform);

            float w = size.x;
            _current = UIKit.CreateText(_visual, "", Theme.TitleHeight, new Vector3(0, size.y * 0.5f - 0.03f, -0.002f), w * 0.95f);
            _next = UIKit.CreateText(_visual, "", Theme.SmallHeight, new Vector3(0, size.y * 0.5f - 0.058f, -0.002f), w * 0.95f);
            _next.color = Theme.TextDim;
            _judgement = UIKit.CreateText(_visual, "", Theme.SmallHeight, new Vector3(0, HitY - 0.03f, -0.002f), w * 1.6f);
            _count = UIKit.CreateText(_visual, "", Theme.TitleHeight * 2f, new Vector3(0, (HitY + TopY) * 0.5f, -0.003f), w);
            _count.alpha = 0f;
            _judgement.alpha = 0f;

            _glow = Quad("Hit glow", _visual, new Vector3(0, HitY, -0.0008f), new Vector2(w * 0.9f, 0.018f), Theme.WithAlpha(Theme.Good, 0f));
            _hitLine = Quad("Hit line", _visual, new Vector3(0, HitY, -0.0012f), new Vector2(w * 0.86f, 0.003f), Theme.WithAlpha(Color.white, 0.55f));

            for (int i = 0; i < 8; i++)
            {
                var line = Quad("Beat line", _visual, Vector3.zero, new Vector2(w * 0.6f, 0.0012f), Theme.WithAlpha(Color.white, 0f));
                line.enabled = false;
                _beatLines.Add(line);
            }
            _visual.gameObject.SetActive(false);
        }

        private Arrow Take()
        {
            Arrow a;
            if (_arrowPool.Count > 0)
            {
                a = _arrowPool[_arrowPool.Count - 1];
                _arrowPool.RemoveAt(_arrowPool.Count - 1);
            }
            else a = BuildArrow();
            a.Root.gameObject.SetActive(true);
            return a;
        }

        private void Release(Arrow a)
        {
            a.Root.gameObject.SetActive(false);
            _arrowPool.Add(a);
        }

        private void ReleaseAllArrows()
        {
            foreach (var pair in _arrows) Release(pair.Value);
            _arrows.Clear();
        }

        /// <summary>A down arrow from three quads: a shaft and a chevron head pointing down.</summary>
        private Arrow BuildArrow()
        {
            var root = new GameObject("Arrow").transform;
            root.SetParent(_visual, false);
            const float thick = 0.0065f;
            const float shaft = 0.026f;
            const float head = 0.019f;
            var parts = new Renderer[3];
            parts[0] = Quad("Shaft", root, new Vector3(0, 0.004f, 0), new Vector2(thick, shaft), ArrowDown);
            // Head bars meet at the tip (bottom), each tilted 45° outward.
            float tipY = 0.004f - shaft * 0.5f;
            float off = head * 0.5f * 0.7071f;
            parts[1] = Quad("Head L", root, new Vector3(-off, tipY + off, 0), new Vector2(thick, head), ArrowDown);
            parts[1].transform.localRotation = Quaternion.Euler(0, 0, 45f);
            parts[2] = Quad("Head R", root, new Vector3(off, tipY + off, 0), new Vector2(thick, head), ArrowDown);
            parts[2].transform.localRotation = Quaternion.Euler(0, 0, -45f);
            return new Arrow { Root = root, Parts = parts };
        }

        private static Renderer Quad(string name, Transform parent, Vector3 localPos, Vector2 scale, Color colour)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = localPos;
            q.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var r = q.GetComponent<Renderer>();
            r.material = Theme.UnlitMaterial(colour);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }
    }
}

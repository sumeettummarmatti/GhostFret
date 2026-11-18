using System;
using System.Collections;
using System.Collections.Generic;
using GhostFret.Core.AirMode;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Music;
using GhostFret.Core.Strum;
using GhostFret.Runtime.Hands;
using GhostFret.Runtime.Overlay;
using GhostFret.Runtime.UI;
using GhostFret.Runtime.Util;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.AirMode
{
    /// <summary>
    /// Air Guitar mode (DESIGN.md §7.6): a pretend guitar at lap height, for players without one and for judges.
    /// The same overlay and ghost hand render on it. The strumming hand crossing the strings plays a
    /// synthesised chord; the fretting hand's shape drives gentle, occasional hints (approximate by design).
    /// Generous on purpose: when tracking is unsure, a strum counts as fine and the coach stays quiet (rule #5).
    /// RunDay1 is a self-contained mini session, so Air mode is always completable with hands only.
    /// Works when added by code: Init(...) then Begin(...). No inspector wiring needed.
    /// </summary>
    public sealed class AirGuitar : MonoBehaviour
    {
        /// <summary>The Air guitar's own card (for placing other cards beside it).</summary>
        public Transform PanelTransform { get { return _panel; } }

        public const string HonestyNote = "Air mode practises shapes and rhythm. Real-guitar mode checks your actual sound.";

        // Tuning values (listed in the report for PROGRESS.md).
        public float hintIntervalSeconds = 6f;     // at most one shape hint this often
        public float hintAfterSeconds = 2f;        // the shape must look off this long before a hint
        public float matchRateHz = 10f;            // shape checks per second
        public float minTrackingConfidence = 0.5f; // below this: no judging, strums count as fine
        public float tipBlend = 0.6f;              // strum point between wrist (0) and index tip (1)
        public float strumVolume = 0.85f;
        public float voiceFadeSeconds = 0.08f;     // a new strum damps the last one, like a real guitar
        public float ghostMorphSeconds = 0.3f;
        public bool shapeHints = true;

        // Day 1 mini session.
        public float day1ShapeTimeout = 10f;       // move on even if the shape never "looks right"
        public float day1SwitchShapeTimeout = 6f;
        public float day1HelpAfterSeconds = 10f;   // no strum yet: say how to strum
        public float day1NextAfterSeconds = 20f;   // still nothing: show a "Next" button

        /// <summary>(dspTime, shapeLooksRight, chordName, direction). Wire to SongPlayer.ReportStrum.</summary>
        public event Action<double, bool, string, StrumDirection> Strummed;

        public FretboardGeometry Geometry { get { return _guitar != null && _placed ? _guitar.Geometry : null; } }
        /// <summary>Low E → high E, world space. Movement along it = down-strum (StrumTracker convention).</summary>
        public Vector3 StrumAxis { get { return _guitar != null && _placed ? _guitar.Zone.StrumAxis.ToUnity() : Vector3.down; } }
        public bool IsActive { get { return _active; } }
        public bool LeftHanded { get; private set; }
        public AirShapeResult LastShape { get { return _lastShape; } }
        public int StrumCount { get; private set; }

        public string TargetChord
        {
            get { return _target; }
            set { SetTarget(value); }
        }

        private static bool _noteShown;

        private FretboardOverlay _overlay;
        private GhostHand _ghost;
        private IHandInput _hands;
        private bool _active;
        private bool _placed;
        private string _target;
        private bool _ghostShown;

        private readonly VirtualGuitar _guitar = new VirtualGuitar();
        private readonly AirChordMatcher _matcher = new AirChordMatcher();
        private readonly PluckSynth _synth = new PluckSynth();
        private AirStrumDetector _detector;
        private bool _strumTracked;

        private AirShapeResult _lastShape = new AirShapeResult { LooksRight = true, Confidence = 1f, Hint = "" };
        private readonly float[] _curl = new float[5];
        private readonly System.Numerics.Vector3[] _tips = new System.Numerics.Vector3[5];
        private float _nextMatch;
        private float _offSince = -1f;
        private float _lastHint = -100f;
        private bool _hintGiven;

        private AirGuitarVisual _visual;
        private Transform _sound;
        private AudioSource[] _voices;
        private int _voice;
        private readonly bool[] _voiceFading = new bool[2];
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        private Transform _panel;
        private TextMeshPro _title;
        private TextMeshPro _note;
        private PokeButton _nextButton;
        private Transform _captionAnchor;
        private Transform _previousAnchor;
        private float _panelShow;
        private float _noteShownAt = -100f;

        private Coroutine _day1;
        private bool _nextPressed;

        private Handedness StrumHand { get { return LeftHanded ? Handedness.Left : Handedness.Right; } }
        private Handedness FretHand { get { return LeftHanded ? Handedness.Right : Handedness.Left; } }
        private string FretHandName { get { return LeftHanded ? "right" : "left"; } }
        private string StrumHandName { get { return LeftHanded ? "left" : "right"; } }

        private ChordShape TargetShape
        {
            get { return ChordLibrary.Contains(_target) ? ChordLibrary.Get(_target) : null; }
        }

        // ================= public API =================

        public void Init(FretboardOverlay overlay, GhostHand ghost)
        {
            _overlay = overlay;
            _ghost = ghost;
        }

        public void Begin(IHandInput hands, bool leftHanded = false)
        {
            gameObject.SetActive(true);
            _hands = hands;
            LeftHanded = leftHanded;
            EnsureBuilt();

            _active = true;
            PlaceGuitar();
            _visual.FadeIn();

            if (Coach.Anchor != _captionAnchor) _previousAnchor = Coach.Anchor;
            Coach.Anchor = _captionAnchor;

            bool firstTime = !_noteShown;
            _note.text = firstTime ? HonestyNote : "";
            _title.text = "Air guitar";
            if (firstTime)
            {
                _noteShown = true;
                _noteShownAt = Time.unscaledTime;
                // The note is on the panel; repeating it as a caption stacked three copies on device.
            }
            if (TargetShape != null) Precache(TargetShape);
        }

        public void End()
        {
            if (_day1 != null) { StopCoroutine(_day1); _day1 = null; }
            _active = false;
            if (_visual != null) _visual.FadeOut();
            if (_overlay != null) _overlay.Hide();
            if (_ghost != null) _ghost.Hide();
            _ghostShown = false;
            if (_panel != null) _panel.gameObject.SetActive(false);
            if (Coach.Anchor == _captionAnchor) Coach.Anchor = _previousAnchor;
            if (_voices != null) for (int i = 0; i < _voices.Length; i++) _voiceFading[i] = true;
            if (_detector != null) _detector.Reset();
        }

        /// <summary>"Move guitar here": rebuilds the guitar in front of where the player now sits and looks.</summary>
        public void Recenter()
        {
            if (!_active) return;
            PlaceGuitar();      // rebuilds the shape; it fades in again gently
            _visual.FadeIn();
        }

        /// <summary>
        /// Built-in Day 1: Em shape → 4 strums → D shape → switch Em↔D 4 times → small celebration → done().
        /// Uses only Air mode's own code + Coach + UIKit, so it completes even if other modules fail.
        /// </summary>
        public void RunDay1(Action done)
        {
            if (!_active) Begin(_hands ?? HandsHub.Input, LeftHanded);
            if (_day1 != null) StopCoroutine(_day1);
            _day1 = StartCoroutine(Day1(done));
        }

        // ================= frame loop =================

        private void Update()
        {
            UpdateVoices();
            UpdatePanel();
            if (!_active || !_placed) return;
            IHandInput hands = _hands ?? HandsHub.Input;
            if (hands == null) return;
            UpdateStrum(hands);
            UpdateShape(hands);
        }

        private void UpdateStrum(IHandInput hands)
        {
            Handedness h = StrumHand;
            if (!hands.IsTracked(h))
            {
                if (_strumTracked) _detector.Reset();
                _strumTracked = false;
                return;
            }
            _strumTracked = true;

            Vector3 p = Vector3.Lerp(hands.Wrist(h), hands.IndexTip(h), tipBlend);
            double now = Time.realtimeSinceStartupAsDouble; // smooth per-frame clock (dspTime moves in buffer steps)
            AirStrum strum;
            if (_detector.AddSample(now, p.ToNumerics(), out strum)) OnStrum(strum, now, hands);
        }

        private void OnStrum(AirStrum strum, double now, IHandInput hands)
        {
            double dsp = AudioSettings.dspTime - (now - strum.Time);
            Handedness fh = FretHand;
            bool unsure = !hands.IsTracked(fh) || hands.Confidence(fh) < minTrackingConfidence || !_lastShape.Judged;
            bool looksRight = unsure || _lastShape.LooksRight;

            ChordShape shape = TargetShape;
            bool up = strum.Direction == StrumDirection.Up;
            if (shape != null)
            {
                float strength = Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.3f, 2.0f, strum.Speed));
                PlayClip(ClipFor(shape, strum.Direction), strength);

                var played = new bool[6];
                for (int s = 0; s < 6; s++)
                {
                    played[s] = shape.IsStringPlayed(s);
                    if (played[s] && _overlay != null) _overlay.HighlightString(s, Theme.Ghost, 0.3f);
                }
                _visual.Sparkle(played, up);
            }
            else _visual.Sparkle(null, up);

            StrumCount++;
            if (Strummed != null)
            {
                try { Strummed(dsp, looksRight, _target, strum.Direction); }
                catch (Exception e) { Debug.LogException(e); } // a listener's bug must never stop Air mode
            }
        }

        private void UpdateShape(IHandInput hands)
        {
            float now = Time.unscaledTime;
            if (now < _nextMatch) return;
            _nextMatch = now + 1f / Mathf.Max(1f, matchRateHz);

            ChordShape target = TargetShape;
            Handedness fh = FretHand;
            if (target == null || !hands.IsTracked(fh))
            {
                _lastShape = new AirShapeResult { LooksRight = true, Confidence = 1f, Hint = "", Judged = false };
                _offSince = -1f;
                return;
            }

            for (int f = 0; f < 5; f++)
            {
                _curl[f] = hands.FingerCurl(fh, f);
                _tips[f] = hands.FingerTip(fh, f).ToNumerics();
            }
            _matcher.MinTrackingConfidence = minTrackingConfidence;
            _lastShape = _matcher.Match(target, _curl, _tips, _guitar.Geometry, hands.Confidence(fh));

            if (_lastShape.LooksRight || !_lastShape.Judged)
            {
                if (_lastShape.Judged && _hintGiven && shapeHints)
                {
                    Coach.Say("That's it. Nice " + _target + ".", CoachTone.Good);
                    _lastHint = now;
                }
                _hintGiven = false;
                _offSince = -1f;
                return;
            }

            if (_offSince < 0f) _offSince = now;
            if (shapeHints && !string.IsNullOrEmpty(_lastShape.Hint)
                && now - _offSince >= hintAfterSeconds && now - _lastHint >= hintIntervalSeconds)
            {
                Coach.Say(_lastShape.Hint, CoachTone.Hint, false); // quiet caption: a tip, not a buzzer
                _lastHint = now;
                _hintGiven = true;
            }
        }

        // ================= placement =================

        private void PlaceGuitar()
        {
            Transform head = UIKit.Head;
            Vector3 forward = head != null ? head.forward : Vector3.forward;
            _guitar.Build(UIKit.HeadPosition.ToNumerics(), forward.ToNumerics(), LeftHanded);
            _placed = true;

            if (_detector == null) _detector = new AirStrumDetector(_guitar.Zone);
            else { _detector.Zone = _guitar.Zone; _detector.Reset(); }

            _visual.Build(_guitar);
            _sound.position = _guitar.Zone.Center.ToUnity();

            FretboardGeometry g = _guitar.Geometry;
            Vector3 toward = _guitar.Zone.TowardPlayer.ToUnity();
            // Panel floats above the middle of the neck: reachable by either hand, clear of the strum and fret hands.
            Vector3 mid = VirtualGuitar.StringCentre(g, 0.30f).ToUnity();
            _panel.position = mid + Vector3.up * 0.20f + toward * 0.04f;
            UIKit.FacePlayer(_panel);
            _panel.gameObject.SetActive(true);
            _panelShow = 0f;
            _captionAnchor.position = _panel.position + Vector3.up * 0.125f;   // clear of the panel's top edge

            if (_overlay != null)
            {
                _overlay.gameObject.SetActive(true);
                _overlay.Show(g);
            }
            _ghostShown = false;
            ShowGhost();
        }

        private void SetTarget(string chord)
        {
            if (chord == _target) return;
            _target = chord;
            _offSince = -1f;
            _hintGiven = false;
            ChordShape shape = TargetShape;
            if (shape != null) Precache(shape);
            if (_active && _placed) ShowGhost();
        }

        private void ShowGhost()
        {
            if (_ghost == null || !_placed) return;
            ChordShape shape = TargetShape;
            if (shape == null) { _ghost.Hide(); _ghostShown = false; return; }
            if (_ghostShown) _ghost.MorphTo(shape, _guitar.Geometry, ghostMorphSeconds);
            else { _ghost.ShowChord(shape, _guitar.Geometry); _ghostShown = true; }
        }

        // ================= sound =================

        private AudioClip ClipFor(ChordShape shape, StrumDirection direction)
        {
            string key = shape.name + "|" + direction;
            AudioClip clip;
            if (_clips.TryGetValue(key, out clip) && clip != null) return clip;
            float[] pcm = _synth.RenderChord(shape, direction);
            clip = AudioClip.Create("GhostFret_Air_" + shape.name + "_" + direction, pcm.Length, 1, _synth.SampleRate, false);
            clip.SetData(pcm, 0);
            _clips[key] = clip;
            return clip;
        }

        /// <summary>Render both strum directions ahead of time so the first strum never hitches.</summary>
        private void Precache(ChordShape shape)
        {
            ClipFor(shape, StrumDirection.Down);
            ClipFor(shape, StrumDirection.Up);
        }

        private void PlayClip(AudioClip clip, float strength)
        {
            if (clip == null || _voices == null) return;
            _voiceFading[_voice] = true;                  // damp the ringing chord…
            _voice = (_voice + 1) % _voices.Length;       // …and strike the new one on the other voice
            AudioSource src = _voices[_voice];
            _voiceFading[_voice] = false;
            src.Stop();
            src.clip = clip;
            src.volume = strumVolume * strength;
            src.Play();
        }

        private void UpdateVoices()
        {
            if (_voices == null) return;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (!_voiceFading[i] || !_voices[i].isPlaying) continue;
                _voices[i].volume = Mathf.MoveTowards(_voices[i].volume, 0f, Time.unscaledDeltaTime * strumVolume / voiceFadeSeconds);
                if (_voices[i].volume <= 0f) { _voices[i].Stop(); _voiceFading[i] = false; }
            }
        }

        // ================= building =================

        private void EnsureBuilt()
        {
            if (_visual != null) return;

            var visualGo = new GameObject("Air Guitar Visual");
            visualGo.transform.SetParent(transform, false);
            _visual = visualGo.AddComponent<AirGuitarVisual>();

            _sound = new GameObject("Air Guitar Sound").transform;
            _sound.SetParent(transform, false);
            _voices = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                var src = _sound.gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 1f;        // sounds from the guitar on your lap
                src.minDistance = 0.5f;
                src.maxDistance = 5f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.dopplerLevel = 0f;
                _voices[i] = src;
            }

            _panel = UIKit.CreatePanel("Air Guitar Panel", new Vector2(0.32f, 0.135f), transform);
            _title = UIKit.CreateText(_panel, "Air guitar", Theme.BodyHeight, new Vector3(0f, 0.044f, -0.001f), 0.30f);
            _note = UIKit.CreateText(_panel, "", Theme.SmallHeight, new Vector3(0f, 0.008f, -0.001f), 0.30f);
            _note.color = Theme.TextDim;
            UIKit.CreateButton(_panel, "Move guitar here", new Vector3(-0.065f, -0.040f, 0f), new Vector2(0.16f, 0.042f), Recenter);
            _nextButton = UIKit.CreateButton(_panel, "Next", new Vector3(0.095f, -0.040f, 0f), new Vector2(0.09f, 0.042f), OnNextPressed);
            _nextButton.gameObject.SetActive(false);

            _captionAnchor = new GameObject("Air Guitar Caption Anchor").transform;
            _captionAnchor.SetParent(transform, false);
        }

        private void UpdatePanel()
        {
            if (_panel == null || !_panel.gameObject.activeSelf) return;
            // Ease the panel in (scale), so it never pops.
            _panelShow = Mathf.MoveTowards(_panelShow, 1f, Time.unscaledDeltaTime / Theme.EaseSeconds);
            _panel.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, Theme.Ease(_panelShow));
            if (_note != null && _note.text.Length > 0 && Time.unscaledTime - _noteShownAt > 30f) _note.text = "";
        }

        private void OnNextPressed() { _nextPressed = true; }

        private void SetStep(string text) { if (_title != null) _title.text = text; }

        // ================= Day 1 =================

        private IEnumerator Day1(Action done)
        {
            // Let the honesty note be read first if it just appeared.
            float sinceNote = Time.unscaledTime - _noteShownAt;
            if (sinceNote < 4f) yield return WaitSeconds(4f - sinceNote);

            // 1. Em shape.
            TargetChord = "Em";
            SetStep("Step 1 of 3: Em");
            Coach.Say("This is Em. Copy the ghost fingers with your " + FretHandName + " hand.");
            yield return WaitSeconds(2.5f);
            yield return WaitForShape(day1ShapeTimeout);

            // 2. Four strums.
            Coach.Say("Now strum 4 times over the round hole.");
            _visual.SetStrumHint(true);
            yield return WaitForStrums(4, n => SetStep("Em: strum " + n + " of 4"));
            _visual.SetStrumHint(false);
            Coach.Say("Lovely. That's your first chord!", CoachTone.Good);
            yield return WaitSeconds(2.5f);

            // 3. D shape.
            TargetChord = "D";
            SetStep("Step 2 of 3: D");
            Coach.Say("Next is D. Follow the ghost hand.");
            yield return WaitSeconds(2.5f);
            yield return WaitForShape(day1ShapeTimeout);
            Coach.Say("Nice D!", CoachTone.Good);
            yield return WaitSeconds(1.5f);

            // 4. Switch Em ↔ D four times, one strum on each.
            Coach.Say("Now switch: Em, then D. Strum once on each.");
            yield return WaitSeconds(3f);
            string[] sequence = { "Em", "D", "Em", "D" };
            for (int i = 0; i < sequence.Length; i++)
            {
                TargetChord = sequence[i];
                SetStep("Step 3 of 3: switch " + (i + 1) + " of 4, " + sequence[i]);
                Coach.Say("Switch to " + sequence[i] + ".");
                yield return WaitForShape(day1SwitchShapeTimeout);
                yield return WaitForStrums(1, null);
            }

            // 5. Small celebration by the strings.
            _visual.Celebrate();
            UiSounds.PlayAt(UiSounds.Chime, _sound.position, 0.8f);
            Coach.Say("You did it: Em to D, four times!", CoachTone.Good, false);
            SetStep("Day 1 done!");
            yield return WaitSeconds(3.5f);

            _day1 = null;
            if (done != null) done();
        }

        private IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        /// <summary>Waits until the shape looks right for half a second, or the timeout (always moves on).</summary>
        private IEnumerator WaitForShape(float timeout)
        {
            float end = Time.unscaledTime + timeout;
            float goodSince = -1f;
            while (Time.unscaledTime < end)
            {
                if (_lastShape.LooksRight)
                {
                    if (goodSince < 0f) goodSince = Time.unscaledTime;
                    if (Time.unscaledTime - goodSince >= 0.5f) yield break;
                }
                else goodSince = -1f;
                yield return null;
            }
        }

        /// <summary>Waits for n strums. If none come, explains how, then offers a Next button (never a dead end).</summary>
        private IEnumerator WaitForStrums(int count, Action<int> onProgress)
        {
            int start = StrumCount;
            int shown = -1;
            float lastProgress = Time.unscaledTime;
            bool helped = false;
            _nextPressed = false;
            while (StrumCount - start < count)
            {
                int done = StrumCount - start;
                if (done != shown)
                {
                    shown = done;
                    lastProgress = Time.unscaledTime;
                    if (onProgress != null) onProgress(done);
                }
                float idle = Time.unscaledTime - lastProgress;
                if (!helped && idle > day1HelpAfterSeconds)
                {
                    helped = true;
                    Coach.Say("Swipe your " + StrumHandName + " hand down across the strings, over the round hole.");
                }
                if (idle > day1NextAfterSeconds && !_nextButton.gameObject.activeSelf) _nextButton.gameObject.SetActive(true);
                if (_nextPressed) break;
                yield return null;
            }
            if (onProgress != null) onProgress(Mathf.Min(count, StrumCount - start));
            _nextButton.gameObject.SetActive(false);
            _nextPressed = false;
        }

        private void OnDisable()
        {
            if (Coach.Anchor == _captionAnchor && _captionAnchor != null) Coach.Anchor = _previousAnchor;
        }

        private void OnDestroy()
        {
            foreach (var clip in _clips.Values) if (clip != null) Destroy(clip);
            _clips.Clear();
        }
    }
}

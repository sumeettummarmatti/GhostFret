using System;
using GhostFret.Core.Audio;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Music;
using GhostFret.Runtime.Audio;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.Tuner
{
    /// <summary>
    /// Guitar tuner floating just above the headstock (DESIGN.md §7.1, §11.2).
    /// Every ~100 ms: newest 4096 mic samples → YIN pitch → TunerSmoother (noise gate, octave fix,
    /// median + smoothing, string lock). Shows a dial with a needle, the note name, a short tip
    /// ("Tune up a little" / "Tune down a little" / "Perfect") and six string chips that turn
    /// green with ✓ once each string has held in tune for ~0.5 s.
    /// Colour is never the only signal: in tune also shows ✓ and the word "Perfect"; out of tune is amber.
    /// "Skip tuning" is always there (a judge may have no guitar).
    ///
    /// Add with AddComponent; Init(mic) is optional (it finds a MicrophoneInput itself).
    /// Begin(geometry or null) shows it; End() hides it. The mic is started if it isn't running.
    /// </summary>
    public sealed class TunerPanel : MonoBehaviour
    {
        [SerializeField] private MicrophoneInput microphone;

        [Tooltip("Within ± this many cents counts as in tune (DESIGN: ±5).")]
        public double inTuneCents = 5;
        [Tooltip("How long a string must stay in tune to be ticked off (s).")]
        public double holdSeconds = 0.5;
        [Tooltip("Seconds between pitch readings.")]
        public float readInterval = 0.1f;
        [Tooltip("Samples per pitch reading (4096 ≈ 85 ms at 48 kHz: enough for low E).")]
        public int windowSamples = 4096;
        [Tooltip("Needle swing at ±50 cents (degrees).")]
        public float dialDegrees = 60f;

        /// <summary>The tuner is done: all six strings in tune, or the player skipped (check WasSkipped).</summary>
        public event Action AllInTune;
        /// <summary>Only when the player pressed "Skip tuning" (AllInTune fires too, so the flow always moves on).</summary>
        public event Action Skipped;
        /// <summary>A stable reading: string index (0 = low E … 5 = high E) and cents (+ sharp, − flat). For the overlay to glow the string.</summary>
        public event Action<int, double> StringReading;

        public bool IsActive { get { return _active; } }
        public bool WasSkipped { get; private set; }
        public TunerSmoother Smoother { get { return _smoother; } }

        private readonly YinPitchDetector _yin = new YinPitchDetector();
        private TunerSmoother _smoother = new TunerSmoother(GuitarTuning.Standard);
        private GuitarTuning _tuning = GuitarTuning.Standard;
        private float[] _buffer;
        private float _nextRead;
        private bool _active;
        private bool _reportedAll;

        // Visuals
        private Transform _root;
        private TextMeshPro _status, _note, _advice;
        private Transform _needlePivot;
        private Renderer _needle, _hub;
        private Transform _meterFill;
        private readonly Renderer[] _chipBg = new Renderer[6];
        private readonly TextMeshPro[] _chipText = new TextMeshPro[6];
        private float _needleAngle;
        private float _shownAt;

        private const float MeterWidth = 0.09f;
        private static readonly Vector3 DialCenter = new Vector3(0f, 0.012f, 0f);
        private const float DialRadius = 0.074f;

        // ---------------------------------------------------------------- public API

        public void Init(MicrophoneInput mic) { microphone = mic; }

        /// <summary>Shows the tuner at the headstock (or in front of the player if geometry is null) and starts listening.</summary>
        public void Begin(FretboardGeometry geometryOrNull)
        {
            Build();
            _smoother.InTuneCents = inTuneCents;
            _smoother.InTuneHoldSeconds = holdSeconds;
            _smoother.Reset();
            _reportedAll = false;
            WasSkipped = false;
            _needleAngle = 0f;
            RefreshChips();
            Reposition(geometryOrNull);
            _root.gameObject.SetActive(true);
            _root.localScale = Vector3.one * 0.92f;
            _shownAt = Time.unscaledTime;
            _active = true;
            _nextRead = Time.unscaledTime + readInterval;

            MicrophoneInput mic = Mic;
            if (mic != null && !mic.IsRunning) mic.StartListening();
            Coach.Say("Pluck one string at a time. I'll show you which way to turn.");
        }

        /// <summary>Moves the panel (e.g. after the guitar was re-fitted).</summary>
        public void Reposition(FretboardGeometry geometryOrNull)
        {
            if (_root == null) return;
            if (geometryOrNull != null) UIKit.PlaceAtHeadstock(_root, geometryOrNull);
            else UIKit.PlaceInFront(_root, 0.45f, 0.10f);
        }

        /// <summary>Hides the tuner. Leaves the mic running for whatever comes next.</summary>
        public void End()
        {
            _active = false;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>Move on without tuning (always available).</summary>
        public void Skip()
        {
            if (!_active) return;
            WasSkipped = true;
            Coach.Say("No problem. We'll skip tuning for now.");
            End();
            if (Skipped != null) Skipped();
            if (AllInTune != null) AllInTune();
        }

        /// <summary>Alternate tunings (Drop D, half step down). Resets progress.</summary>
        public void SetTuning(GuitarTuning tuning)
        {
            _tuning = tuning ?? GuitarTuning.Standard;
            _smoother.SetTuning(_tuning);
            _reportedAll = false;
            RefreshChips();
        }

        // ---------------------------------------------------------------- listening

        private MicrophoneInput Mic
        {
            get
            {
                if (microphone == null) microphone = FindFirstObjectByType<MicrophoneInput>();
                return microphone;
            }
        }

        private void Update()
        {
            if (!_active || _root == null) return;

            float grow = Theme.Ease((Time.unscaledTime - _shownAt) / Theme.EaseSeconds);
            _root.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, grow);
            UIKit.FacePlayer(_root);   // stays readable as the player leans

            MicrophoneInput mic = Mic;
            bool hearing = mic != null && mic.IsRunning;
            UpdateMeter(hearing ? mic.Level01 : 0f);

            if (hearing && Time.unscaledTime >= _nextRead)
            {
                _nextRead = Time.unscaledTime + readInterval;
                Read(mic);
            }
            if (!hearing) _status.text = mic != null && mic.IsAskingPermission ? "Waiting for the microphone…" : "The microphone is off";

            AnimateNeedle();
        }

        private void Read(MicrophoneInput mic)
        {
            if (_buffer == null || _buffer.Length != windowSamples) _buffer = new float[Mathf.Max(1024, windowSamples)];
            double time;
            if (!mic.GetLatest(_buffer, out time)) return;
            PitchResult pitch = _yin.Detect(_buffer, mic.SampleRate);
            _smoother.Push(pitch, time);

            if (_smoother.IsStable && StringReading != null) StringReading(_smoother.StringIndex, _smoother.Cents);

            int finished = _smoother.JustFinishedString;
            if (finished >= 0)
            {
                RefreshChips();
                if (!_smoother.AllDone)   // the last string gets the all-done caption instead
                    Coach.Say(Capitalise(GuitarTuning.StringNames[finished]) + " string is in tune.", CoachTone.Good);
            }
            if (_smoother.AllDone && !_reportedAll)
            {
                _reportedAll = true;
                Coach.Say("All six strings are in tune. Lovely!", CoachTone.Good);
                if (AllInTune != null) AllInTune();
            }
            UpdateReadout();
        }

        private void UpdateReadout()
        {
            TunerAdvice advice = _smoother.Advice;
            if (!_smoother.IsStable)
            {
                _status.text = _smoother.DoneCount == 6 ? "All in tune " + Theme.GoodMark : "Pluck one string";
                _note.text = "–";
                _note.color = Theme.TextDim;
                _advice.text = TunerSmoother.AdviceText(TunerAdvice.Listening);
                _advice.color = Theme.TextDim;
                SetColour(_needle, Theme.WithAlpha(Theme.TextDim, 0.5f));
                SetColour(_hub, Theme.WithAlpha(Theme.TextDim, 0.5f));
            }
            else
            {
                int s = _smoother.StringIndex;
                bool good = advice == TunerAdvice.InTune;
                Color c = good ? Theme.Good : Theme.Hint;
                _status.text = Capitalise(GuitarTuning.StringNames[s]) + " string";
                _note.text = NoteMath.PitchClassNames[NoteMath.PitchClass(_tuning.OpenStringMidi[s])];
                _note.color = good ? Theme.Good : Theme.Text;
                _advice.text = good ? Theme.GoodMark + " " + TunerSmoother.AdviceText(advice) : TunerSmoother.AdviceText(advice);
                _advice.color = c;
                SetColour(_needle, c);
                SetColour(_hub, c);
            }
            HighlightCurrentChip();
        }

        private void AnimateNeedle()
        {
            float target = 0f;
            if (_smoother.IsStable)
            {
                float cents = Mathf.Clamp((float)_smoother.Cents, -50f, 50f);
                target = -cents / 50f * dialDegrees;   // sharp → clockwise (to the right, as the player sees it)
            }
            float k = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);   // calm, no snapping
            _needleAngle = Mathf.Lerp(_needleAngle, target, k);
            _needlePivot.localRotation = Quaternion.Euler(0f, 0f, _needleAngle);
        }

        private void UpdateMeter(float level01)
        {
            float w = Mathf.Max(0.0005f, MeterWidth * level01);
            _meterFill.localScale = new Vector3(w, 0.004f, 1f);
            _meterFill.localPosition = new Vector3(-0.122f + w * 0.5f, -0.118f, -0.001f);
        }

        // ---------------------------------------------------------------- building

        private void Build()
        {
            if (_root != null) return;
            _root = UIKit.CreatePanel("GhostFret Tuner", new Vector2(0.30f, 0.29f));
            _root.SetParent(transform, true);

            _status = UIKit.CreateText(_root, "Pluck one string", Theme.SmallHeight, new Vector3(0f, 0.122f, -0.0015f), 0.27f);
            _status.color = Theme.TextDim;

            // Dial: ticks every 10 cents, longer at 0 and the ends, and a soft green band for ±inTuneCents.
            for (int cents = -50; cents <= 50; cents += 10)
            {
                bool major = cents == 0 || cents == -50 || cents == 50;
                float len = major ? 0.016f : 0.009f;
                float angle = cents / 50f * dialDegrees;
                Quad(_root, "Tick", new Vector2(major ? 0.0022f : 0.0015f, len), DialPoint(angle, DialRadius - len * 0.5f, -0.0005f),
                     -angle, cents == 0 ? Theme.Good : Theme.WithAlpha(Theme.Text, major ? 0.8f : 0.45f));
            }
            float zoneDeg = (float)inTuneCents / 50f * dialDegrees;
            float zoneWidth = 2f * DialRadius * Mathf.Sin(zoneDeg * Mathf.Deg2Rad);
            Quad(_root, "In-tune zone", new Vector2(Mathf.Max(zoneWidth, 0.006f), 0.022f), DialPoint(0f, DialRadius - 0.011f, -0.0003f),
                         0f, Theme.WithAlpha(Theme.Good, 0.25f));

            var flat = UIKit.CreateText(_root, "flat", Theme.SmallHeight * 0.8f, DialPoint(-dialDegrees - 14f, DialRadius - 0.004f, -0.0015f), 0.05f);
            flat.color = Theme.TextDim;
            var sharp = UIKit.CreateText(_root, "sharp", Theme.SmallHeight * 0.8f, DialPoint(dialDegrees + 14f, DialRadius - 0.004f, -0.0015f), 0.05f);
            sharp.color = Theme.TextDim;

            _needlePivot = new GameObject("Needle pivot").transform;
            _needlePivot.SetParent(_root, false);
            _needlePivot.localPosition = new Vector3(DialCenter.x, DialCenter.y, -0.001f);
            float needleLen = DialRadius - 0.004f;
            _needle = Quad(_needlePivot, "Needle", new Vector2(0.003f, needleLen), new Vector3(0f, needleLen * 0.5f, 0f), 0f, Theme.TextDim);
            _hub = Quad(_needlePivot, "Hub", new Vector2(0.009f, 0.009f), new Vector3(0f, 0f, -0.0002f), 45f, Theme.TextDim);

            _note = UIKit.CreateText(_root, "–", Theme.TitleHeight * 1.5f, new Vector3(0f, -0.022f, -0.0015f), 0.12f);
            _advice = UIKit.CreateText(_root, TunerSmoother.AdviceText(TunerAdvice.Listening), Theme.BodyHeight, new Vector3(0f, -0.054f, -0.0015f), 0.27f);
            _advice.color = Theme.TextDim;

            // Six string chips, low E on the left (as the strings read from the player's side).
            for (int s = 0; s < 6; s++)
            {
                var pos = new Vector3((s - 2.5f) * 0.044f, -0.085f, -0.0008f);
                _chipBg[s] = Quad(_root, "Chip " + s, new Vector2(0.038f, 0.024f), pos, 0f, Theme.ButtonBg);
                _chipText[s] = UIKit.CreateText(_root, "", Theme.SmallHeight, pos + new Vector3(0f, 0f, -0.0008f), 0.038f);
            }

            // Little "I can hear you" meter (bottom left), and Skip (bottom right).
            Quad(_root, "Meter bg", new Vector2(MeterWidth, 0.004f), new Vector3(-0.122f + MeterWidth * 0.5f, -0.118f, -0.0006f), 0f, Theme.WithAlpha(Theme.Text, 0.15f));
            _meterFill = Quad(_root, "Meter", new Vector2(0.0005f, 0.004f), new Vector3(-0.122f, -0.118f, -0.001f), 0f, Theme.WithAlpha(Theme.Ghost, 0.9f)).transform;
            var mic = UIKit.CreateText(_root, "mic", Theme.SmallHeight * 0.7f, new Vector3(-0.122f + MeterWidth * 0.5f, -0.106f, -0.0015f), 0.05f);
            mic.color = Theme.TextDim;
            UIKit.CreateButton(_root, "Skip tuning", new Vector3(0.068f, -0.118f, -0.002f), new Vector2(0.13f, 0.04f), Skip);

            _root.gameObject.SetActive(false);
        }

        private void RefreshChips()
        {
            if (_root == null) return;
            for (int s = 0; s < 6; s++)
            {
                string name = NoteMath.PitchClassNames[NoteMath.PitchClass(_tuning.OpenStringMidi[s])];
                bool done = _smoother.IsDone(s);
                _chipText[s].text = done ? name + Theme.GoodMark : name;
                _chipText[s].color = done ? Theme.PanelBg : Theme.Text;
                SetColour(_chipBg[s], done ? Theme.Good : Theme.ButtonBg);
            }
        }

        private void HighlightCurrentChip()
        {
            int current = _smoother.IsStable ? _smoother.StringIndex : -1;
            for (int s = 0; s < 6; s++)
            {
                if (_smoother.IsDone(s)) continue;
                SetColour(_chipBg[s], s == current ? Theme.ButtonHover : Theme.ButtonBg);
            }
        }

        private static Vector3 DialPoint(float angleDeg, float radius, float z)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(DialCenter.x + Mathf.Sin(a) * radius, DialCenter.y + Mathf.Cos(a) * radius, z);
        }

        /// <summary>A flat coloured rectangle. Positive rotationDeg turns it anticlockwise as the player sees it.</summary>
        private static Renderer Quad(Transform parent, string name, Vector2 size, Vector3 localPos, float rotationDeg, Color colour)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = localPos;
            q.transform.localRotation = Quaternion.Euler(0f, 0f, rotationDeg);
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = q.GetComponent<Renderer>();
            r.material = Theme.UnlitMaterial(colour);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static void SetColour(Renderer r, Color c)
        {
            if (r != null) r.material.color = c;
        }

        private static string Capitalise(string s)
        {
            return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}

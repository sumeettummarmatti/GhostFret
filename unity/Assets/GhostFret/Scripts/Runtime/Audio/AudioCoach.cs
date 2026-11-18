using System;
using GhostFret.Core.Audio;
using GhostFret.Core.Feedback;
using GhostFret.Core.Music;
using GhostFret.Runtime.UI;
using UnityEngine;

namespace GhostFret.Runtime.Audio
{
    /// <summary>
    /// Listens for strums and turns them into ChordFeedback (DESIGN.md §8.1, §11.3, §11.4).
    /// Each frame it hands only the NEW mic samples to Core's StrumListener, which finds onsets on
    /// 1024-sample hops, times them to a few milliseconds, waits for 8192 samples after the attack,
    /// and runs ChordAnalyzer. All buffers are allocated once (no per-frame garbage on Quest).
    ///
    /// Audio decides chord correctness (CLAUDE.md #4). When the analysis isn't sure it reports
    /// NotSure with a general tip, never a specific wrong claim (CLAUDE.md #5).
    ///
    /// Add with AddComponent, then Init(mic) (or it finds a MicrophoneInput itself). Set
    /// Listening = true to start; it starts the mic if needed.
    /// </summary>
    public sealed class AudioCoach : MonoBehaviour
    {
        [SerializeField] private MicrophoneInput microphone;

        [Tooltip("Chord the player should be playing. Null/empty = only report strum times.")]
        public string TargetChord = "Em";
        [Tooltip("Also show each result as a coach caption. Off by default: the lesson runners decide what to say.")]
        public bool speakFeedback = false;
        [Tooltip("Input latency (s) measured by the latency calibration. Subtracted from strum times.")]
        public double inputLatencySeconds = 0.0;

        /// <summary>Feedback for one strum (one result per strum; quick strums merge).</summary>
        public event Action<ChordFeedback> StrumHeard;
        /// <summary>A strum's onset, in AudioSettings.dspTime seconds. Fires for every strum, even without a target chord.</summary>
        public event Action<double> StrumTimed;

        /// <summary>dspTime of the last strum onset, or 0 if none yet.</summary>
        public double LastStrumDspTime { get; private set; }

        /// <summary>Seconds since the last strum, or since listening started if there hasn't been one.</summary>
        public float SecondsSinceLastStrum
        {
            get { return (float)(AudioSettings.dspTime - Math.Max(LastStrumDspTime, _listeningSince)); }
        }

        /// <summary>Exposed for on-device tuning (onset thresholds, analyzer thresholds).</summary>
        public StrumListener Listener { get { return _listener; } }

        public bool Listening
        {
            get { return _listening; }
            set
            {
                if (value == _listening) return;
                _listening = value;
                if (value) BeginListening();
            }
        }

        private bool _listening;
        private double _listeningSince;
        private StrumListener _listener;
        private int _listenerRate;
        private long _readFrom = -1;              // next mic sample index to hand to the listener
        private readonly float[] _scratch = new float[4096];

        /// <summary>Optional explicit wiring (otherwise found automatically).</summary>
        public void Init(MicrophoneInput mic)
        {
            microphone = mic;
            _readFrom = -1;
        }

        private MicrophoneInput Mic
        {
            get
            {
                if (microphone == null) microphone = FindFirstObjectByType<MicrophoneInput>();
                return microphone;
            }
        }

        private void BeginListening()
        {
            _listeningSince = AudioSettings.dspTime;
            _readFrom = -1;   // process only audio that arrives from now on
            MicrophoneInput mic = Mic;
            if (mic != null && !mic.IsRunning) mic.StartListening();
        }

        private void EnsureListener(int rate)
        {
            if (_listener != null && _listenerRate == rate) return;
            _listener = new StrumListener(rate, GuitarTuning.Standard);
            _listenerRate = rate;
            _listener.StrumOnset += OnOnset;
            _listener.ChordHeard += OnChord;
        }

        private void Update()
        {
            if (!_listening) return;
#if UNITY_EDITOR
            if (PreviewIgnoreMic) { _readFrom = -1; return; }   // preview: keyboard strums only, no laptop-mic noise
#endif
            MicrophoneInput mic = Mic;
            if (mic == null || !mic.IsRunning) { _readFrom = -1; return; }

            EnsureListener(mic.SampleRate);
            _listener.TargetChord = string.IsNullOrEmpty(TargetChord) ? null : TargetChord;

            long written = mic.SamplesWritten;
            if (_readFrom < 0 || written < _readFrom || written - _readFrom > 32768)
            {
                // First frame, mic restarted, or we fell far behind: start fresh from "now".
                _listener.Reset();
                _readFrom = written;
                return;
            }

            while (_readFrom < written)
            {
                int count = (int)Math.Min(_scratch.Length, written - _readFrom);
                if (!mic.CopySamples(_readFrom, _scratch, 0, count)) { _readFrom = -1; return; }
                long last = _readFrom + count - 1;
                _readFrom += count;
                _listener.Push(_scratch, 0, count, mic.SampleIndexToDspTime(last));
            }
        }

        private void OnOnset(double dspTime)
        {
            double t = dspTime - inputLatencySeconds;
            LastStrumDspTime = t;
            if (StrumTimed != null) StrumTimed(t);
        }

        private void OnChord(ChordFeedback feedback, double onsetTime)
        {
            if (!_listening || feedback == null) return;
            if (speakFeedback) Speak(feedback);
            if (StrumHeard != null) StrumHeard(feedback);
        }

#if UNITY_EDITOR
        /// <summary>Editor preview only: ignore the real microphone (keyboard strums only).</summary>
        public bool PreviewIgnoreMic { get; set; }

        /// <summary>
        /// Editor preview only (Runtime/EditorPreview): pretend the mic heard a strum at dspTime with this
        /// result. Goes through the same latency correction and events as a real strum (StrumTimed, then
        /// StrumHeard). Ignored while not listening, exactly like real audio. Returns false if ignored.
        /// </summary>
        public bool InjectForPreview(ChordFeedback fb, double dspTime)
        {
            if (!_listening) return false;
            OnOnset(dspTime);
            if (fb != null) OnChord(fb, dspTime);
            return true;
        }
#endif

        private static void Speak(ChordFeedback fb)
        {
            if (string.IsNullOrEmpty(fb.Message)) return;
            CoachTone tone = fb.Kind == FeedbackKind.Clean ? CoachTone.Good
                           : fb.Kind == FeedbackKind.NotSure || fb.Kind == FeedbackKind.TooQuiet ? CoachTone.Neutral
                           : CoachTone.Hint;
            Coach.Say(fb.Message, tone);
        }

        private void OnDisable()
        {
            _readFrom = -1;
        }
    }
}

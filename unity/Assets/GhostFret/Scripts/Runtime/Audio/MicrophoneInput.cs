using System;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GhostFret.Runtime.Audio
{
    /// <summary>
    /// The headset microphone, read into our own ring buffer every frame.
    /// Privacy (CLAUDE.md #6): samples live only in memory for about a second, are processed live,
    /// and are never saved, logged or sent.
    ///
    /// Permission: before Android's system dialog we show a small panel saying why we listen
    /// ("Continue" / "Not now"). Granted → Started. Denied or unavailable → a kind caption and
    /// PermissionDenied, so the app can offer Air mode.
    ///
    /// Timing: every sample gets a time on the AudioSettings.dspTime clock. Each frame we see how
    /// many samples the mic has written; the newest one was captured "about now". We keep a smoothed
    /// offset between sample index and dspTime, so strum onsets line up with metronome clicks.
    /// (The mic's own input latency is measured separately by the latency calibration step.)
    ///
    /// Works when added with AddComponent: nothing needs wiring in the inspector.
    /// </summary>
    [DefaultExecutionOrder(-50)]   // read the mic before AudioCoach/TunerPanel run their Update
    public sealed class MicrophoneInput : MonoBehaviour
    {
        [Tooltip("Requested sample rate. The device may give another; SampleRate tells the real one.")]
        public int sampleRate = 48000;
        [Tooltip("Length of Unity's looping record clip (seconds).")]
        public int clipSeconds = 2;
        [Tooltip("Show the 'why we listen' panel before the system permission dialog.")]
        public bool explainBeforeAsking = true;
        [Tooltip("Editor only: show the permission panel even though the Editor doesn't need it (to test the flow).")]
        public bool showExplainerInEditor = false;
        [Tooltip("Level meter smoothing: how fast it rises and falls (per second).")]
        public float levelRise = 30f, levelFall = 4f;

        /// <summary>The mic is running and samples are arriving.</summary>
        public event Action Started;
        /// <summary>The player said no, or there's no usable microphone. Offer Air mode.</summary>
        public event Action PermissionDenied;

        public bool IsRunning { get; private set; }
        /// <summary>The real sample rate of the running mic (falls back to the requested one).</summary>
        public int SampleRate { get { return _rate > 0 ? _rate : sampleRate; } }
        /// <summary>Smoothed RMS of the latest audio (0..1 full scale). For a small "listening" meter.</summary>
        public float Level { get; private set; }
        /// <summary>Level mapped from −60 dB…−12 dB to 0..1, ready for a meter bar.</summary>
        public float Level01
        {
            get
            {
                if (Level <= 1e-6f) return 0f;
                float db = 20f * Mathf.Log10(Level);
                return Mathf.Clamp01((db + 60f) / 48f);
            }
        }
        /// <summary>Total samples written to our ring since the mic started (absolute index of the next sample).</summary>
        public long SamplesWritten { get { return _total; } }
        /// <summary>Waiting for the player on the permission panel or the system dialog.</summary>
        public bool IsAskingPermission { get { return _explainer != null || _waitingForPermission; } }

        /// <summary>dspTime of the newest sample in the ring.</summary>
        public double NewestSampleTime { get { return SampleIndexToDspTime(_total - 1); } }

        private const int RingSize = 1 << 16;   // ~1.4 s at 48 kHz, plenty for an 8192-sample analysis after a strum
        private readonly float[] _ring = new float[RingSize];
        private long _total;

        private AudioClip _clip;
        private string _device;
        private int _rate;
        private int _channels = 1;
        private int _clipSamples;
        private int _readPos;
        private int _chunkFrames;
        private float[] _chunk;
        private bool _wantRunning;
        private bool _waitingForFirstSamples;
        private float _startRequestedAt;

        // Sample index → dspTime mapping.
        private bool _timingSeeded;
        private double _offset;              // dspTime of sample index 0
        private double _lastDsp = -1, _lastDspRealtime;

        // Permission flow.
        private Transform _explainer;
        private float _explainerShownAt;
        private bool _waitingForPermission;
        private float _nextPermissionPoll;
        private volatile int _permissionResult;   // 0 = waiting, 1 = granted, −1 = denied (set from Android callbacks)
        private bool _resumeAfterPause;
        private float _retryAt;

        // ---------------------------------------------------------------- public API

        /// <summary>Starts listening. Shows the permission explainer first if needed. Safe to call repeatedly.</summary>
        public void StartListening()
        {
            _wantRunning = true;
            if (IsRunning || _waitingForFirstSamples || IsAskingPermission) return;

            if (!HasPermission() || (Application.isEditor && showExplainerInEditor && !_explainedOnce))
            {
                if (explainBeforeAsking) ShowExplainer();
                else RequestPermission();
                return;
            }
            BeginCapture();
        }

        public void StopListening()
        {
            _wantRunning = false;
            HideExplainer();
            _waitingForPermission = false;
            StopCapture();
        }

        /// <summary>Copies the most recent samples into target (newest at the end). False if not running.</summary>
        public bool GetLatest(float[] target)
        {
            double unused;
            return GetLatest(target, out unused);
        }

        /// <summary>
        /// Copies the most recent target.Length samples (newest at the end) and gives the dspTime of
        /// the newest one. If fewer samples exist yet, the start is zero-filled.
        /// </summary>
        public bool GetLatest(float[] target, out double newestSampleDspTime)
        {
            newestSampleDspTime = NewestSampleTime;
            if (!IsRunning || target == null || target.Length == 0 || target.Length > RingSize) return false;
            int n = target.Length;
            long start = _total - n;
            if (start < 0)
            {
                int pad = (int)(-start);
                Array.Clear(target, 0, pad);
                CopyFromRing(0, target, pad, n - pad);
            }
            else CopyFromRing(start, target, 0, n);
            return true;
        }

        /// <summary>
        /// Copies count samples starting at absolute index startSample (see SamplesWritten).
        /// False if they're not written yet or have already been overwritten.
        /// </summary>
        public bool CopySamples(long startSample, float[] dst, int dstOffset, int count)
        {
            if (count <= 0) return true;
            if (startSample < 0 || startSample + count > _total || _total - startSample > RingSize) return false;
            CopyFromRing(startSample, dst, dstOffset, count);
            return true;
        }

        /// <summary>dspTime at which an absolute sample index was captured.</summary>
        public double SampleIndexToDspTime(long sampleIndex)
        {
            if (!_timingSeeded) return SmoothDspNow();
            return _offset + (double)sampleIndex / SampleRate;
        }

        // ---------------------------------------------------------------- capture

        private void BeginCapture()
        {
            HideExplainer();
            _waitingForPermission = false;
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Unavailable();
                return;
            }
            _device = Microphone.devices[0];

            int min, max;
            Microphone.GetDeviceCaps(_device, out min, out max);
            int rate = sampleRate;
            if (min > 0 || max > 0) rate = Mathf.Clamp(rate, Mathf.Max(min, 1), max > 0 ? max : rate);

            _clip = Microphone.Start(_device, true, Mathf.Max(1, clipSeconds), rate);
            if (_clip == null)
            {
                Unavailable();
                return;
            }
            _rate = _clip.frequency;
            _channels = Mathf.Max(1, _clip.channels);
            _clipSamples = _clip.samples;
            _chunkFrames = PickChunk(_clipSamples);
            _chunk = new float[_chunkFrames * _channels];
            _readPos = 0;
            _total = 0;
            _timingSeeded = false;
            Level = 0f;
            _waitingForFirstSamples = true;
            _startRequestedAt = Time.unscaledTime;
        }

        private void StopCapture()
        {
            if (_clip != null || IsRunning || _waitingForFirstSamples) Microphone.End(_device);
            _clip = null;
            IsRunning = false;
            _waitingForFirstSamples = false;
            _timingSeeded = false;
            Level = 0f;
        }

        /// <summary>
        /// Reads whole chunks so GetData never needs a fresh array. A chunk size that divides the clip
        /// means a read never straddles the loop point.
        /// </summary>
        private static int PickChunk(int clipSamples)
        {
            for (int c = 256; c >= 64; c--) if (clipSamples % c == 0) return c;
            return 256;   // rare: rely on GetData wrapping around the looping clip
        }

        private void Update()
        {
            UpdatePermissionFlow();
            AnimateExplainer();

            if (_clip == null)
            {
                if (_wantRunning && _retryAt > 0f && Time.unscaledTime >= _retryAt)
                {
                    _retryAt = 0f;
                    BeginCapture();
                }
                return;
            }
            int pos = Microphone.GetPosition(_device);

            if (_waitingForFirstSamples)
            {
                if (pos > 0)
                {
                    _waitingForFirstSamples = false;
                    _readPos = 0;
                    IsRunning = true;
                    if (Started != null) Started();
                }
                else if (Time.unscaledTime - _startRequestedAt > 3f)
                {
                    StopCapture();
                    Unavailable();
                    return;
                }
                else return;
            }

            if (!Microphone.IsRecording(_device))
            {
                // The OS took the mic away (e.g. another app). Try again shortly if still wanted.
                StopCapture();
                _retryAt = Time.unscaledTime + 1f;
                return;
            }

            int available = pos - _readPos;
            if (available < 0) available += _clipSamples;

            // A long hitch (or a pause) can lap the clip: skip to fresh audio and re-learn timing.
            if (Time.unscaledDeltaTime > clipSeconds * 0.8f || available > _clipSamples - 2 * _chunkFrames)
            {
                _readPos = pos;
                _timingSeeded = false;
                return;
            }

            double sumSq = 0;
            int newSamples = 0;
            while (available >= _chunkFrames)
            {
                _clip.GetData(_chunk, _readPos);
                for (int f = 0; f < _chunkFrames; f++)
                {
                    float v = _chunk[f * _channels];
                    for (int c = 1; c < _channels; c++) v += _chunk[f * _channels + c];
                    if (_channels > 1) v /= _channels;
                    _ring[(int)(_total % RingSize)] = v;
                    _total++;
                    sumSq += v * v;
                }
                newSamples += _chunkFrames;
                _readPos = (_readPos + _chunkFrames) % _clipSamples;
                available -= _chunkFrames;
            }

            UpdateTiming(available);
            UpdateLevel(newSamples, sumSq);
        }

        /// <summary>
        /// estimate = dspTime(now) − (samples the mic has written so far) / rate = dspTime of sample 0.
        /// The mic delivers in bursts, so right after a burst the estimate is most accurate and between
        /// bursts it drifts late. We therefore follow the estimate down quickly and up only slowly
        /// (enough to track tiny clock drift between the mic and the audio output).
        /// </summary>
        private void UpdateTiming(int unreadOnDevice)
        {
            double estimate = SmoothDspNow() - (double)(_total + unreadOnDevice) / SampleRate;
            if (!_timingSeeded || Math.Abs(estimate - _offset) > 0.25)
            {
                _offset = estimate;
                _timingSeeded = true;
                return;
            }
            if (estimate < _offset) _offset += 0.5 * (estimate - _offset);
            else _offset += 0.003 * (estimate - _offset);
        }

        /// <summary>dspTime only ticks once per audio buffer; extrapolate between ticks with the real-time clock.</summary>
        private double SmoothDspNow()
        {
            double dsp = AudioSettings.dspTime;
            double real = Time.realtimeSinceStartupAsDouble;
            if (dsp != _lastDsp)
            {
                _lastDsp = dsp;
                _lastDspRealtime = real;
                return dsp;
            }
            return dsp + Math.Min(real - _lastDspRealtime, 0.05);
        }

        private void UpdateLevel(int newSamples, double sumSq)
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            float target = newSamples > 0 ? (float)Math.Sqrt(sumSq / newSamples) : Level;
            float rate = target > Level ? levelRise : levelFall;
            Level = Mathf.Lerp(Level, target, 1f - Mathf.Exp(-rate * dt));
        }

        private void CopyFromRing(long start, float[] dst, int dstOffset, int count)
        {
            int read = (int)(start % RingSize);
            int first = Math.Min(count, RingSize - read);
            Array.Copy(_ring, read, dst, dstOffset, first);
            if (count > first) Array.Copy(_ring, 0, dst, dstOffset + first, count - first);
        }

        // ---------------------------------------------------------------- permission

        private bool _explainedOnce;

        private static bool HasPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            return true;
#endif
        }

        private void RequestPermission()
        {
            _waitingForPermission = true;
            _permissionResult = 0;
            _nextPermissionPoll = Time.unscaledTime + 0.5f;
#if UNITY_ANDROID && !UNITY_EDITOR
            var callbacks = new PermissionCallbacks();
            // Callbacks may arrive off the main thread: just record the answer, act in Update.
            callbacks.PermissionGranted += name => _permissionResult = 1;
            callbacks.PermissionDenied += name => _permissionResult = -1;
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
#else
            _permissionResult = 1;   // Editor / desktop: the OS handles mic access
#endif
        }

        private void UpdatePermissionFlow()
        {
            if (!_waitingForPermission) return;
            int result = _permissionResult;
            // Some devices skip the callback: poll as a backup.
            if (result == 0 && Time.unscaledTime >= _nextPermissionPoll)
            {
                _nextPermissionPoll = Time.unscaledTime + 0.5f;
                if (HasPermission()) result = 1;
            }
            if (result == 0) return;

            _waitingForPermission = false;
            _permissionResult = 0;
            if (result > 0 && _wantRunning) BeginCapture();
            else if (result < 0) Denied(false);
        }

        private void Denied(bool choseNotNow)
        {
            _wantRunning = false;
            Coach.Say(choseNotNow
                ? "No problem. Air mode works without the microphone."
                : "No problem. You can still practise in Air mode.");
            if (PermissionDenied != null) PermissionDenied();
        }

        private void Unavailable()
        {
            _wantRunning = false;
            Coach.Say("I can't hear the microphone right now. Air mode still works.", CoachTone.Hint);
            if (PermissionDenied != null) PermissionDenied();
        }

        private void ShowExplainer()
        {
            if (_explainer != null) return;
            _explainedOnce = true;
            _explainer = UIKit.CreatePanel("GhostFret Mic Permission", new Vector2(0.36f, 0.20f));
            UIKit.CreateText(_explainer, "May GhostFret listen?", Theme.TitleHeight, new Vector3(0f, 0.062f, -0.001f), 0.33f);
            TextMeshPro body = UIKit.CreateText(_explainer,
                "GhostFret listens to your guitar to check your chords. Sound is never recorded or sent.",
                Theme.BodyHeight * 0.85f, new Vector3(0f, 0.008f, -0.001f), 0.32f);
            body.color = Theme.TextDim;
            UIKit.CreateButton(_explainer, "Continue", new Vector3(0.075f, -0.06f, -0.002f), new Vector2(0.13f, 0.045f), OnContinue);
            UIKit.CreateButton(_explainer, "Not now", new Vector3(-0.075f, -0.06f, -0.002f), new Vector2(0.13f, 0.045f), OnNotNow);
            UIKit.PlaceInFront(_explainer, 0.45f, 0.12f);
            _explainer.localScale = Vector3.one * 0.9f;
            _explainerShownAt = Time.unscaledTime;
        }

        private void AnimateExplainer()
        {
            if (_explainer == null) return;
            float t = Theme.Ease((Time.unscaledTime - _explainerShownAt) / Theme.EaseSeconds);
            _explainer.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, t);
        }

        private void HideExplainer()
        {
            if (_explainer == null) return;
            Destroy(_explainer.gameObject);
            _explainer = null;
        }

        private void OnContinue()
        {
            HideExplainer();
            if (HasPermission()) { if (_wantRunning) BeginCapture(); }
            else RequestPermission();
        }

        private void OnNotNow()
        {
            HideExplainer();
            Denied(true);
        }

        // ---------------------------------------------------------------- lifecycle

        private void OnApplicationPause(bool paused)
        {
            // Android stops the mic while the app is in the background; restart cleanly on return.
            if (paused)
            {
                _resumeAfterPause = _wantRunning && (IsRunning || _waitingForFirstSamples);
                StopCapture();
            }
            else if (_resumeAfterPause && _wantRunning)
            {
                _resumeAfterPause = false;
                BeginCapture();
            }
        }

        private void OnEnable()
        {
            // Re-enabled after being switched off while wanted: pick up where we left off.
            if (_wantRunning && _clip == null && !IsAskingPermission && HasPermission()) BeginCapture();
        }

        private void OnDisable()
        {
            StopCapture();
            HideExplainer();
            _waitingForPermission = false;
        }
    }
}

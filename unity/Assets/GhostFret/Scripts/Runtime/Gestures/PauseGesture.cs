using System;
using GhostFret.Core.Lesson;
using GhostFret.Runtime.Hands;
using GhostFret.Runtime.Util;
using UnityEngine;

namespace GhostFret.Runtime.Gestures
{
    /// <summary>
    /// Pause = palm facing up, held still for 1 s, NOT pinching (either hand). The same pose again resumes.
    /// Never uses the reserved system gesture (palm toward headset + pinch): any pinch resets the hold.
    /// Also notices silence: no strum for silenceSeconds while WatchSilence is on → SilenceNoticed.
    /// The detection logic is in Core (PalmUpPauseDetector, SilenceWatcher) and unit-tested.
    /// </summary>
    public sealed class PauseGesture : MonoBehaviour
    {
        public float holdSeconds = 1f;
        public float silenceSeconds = 10f;
        public float palmUpDot = 0.8f;              // dot(palmNormal, world up)
        public float stillMetersPerSecond = 0.12f;  // wrist speed below this = still
        public float minConfidence = 0.5f;          // ignore low-confidence tracking

        /// <summary>Palm-up held while not paused.</summary>
        public event Action Paused;
        /// <summary>Palm-up held while paused.</summary>
        public event Action Resumed;
        /// <summary>No strum for silenceSeconds while WatchSilence.</summary>
        public event Action SilenceNoticed;

        /// <summary>Set by the app: are we paused right now? Decides Paused vs Resumed.</summary>
        public bool IsPaused { get; set; }
        /// <summary>Set by the app: gesture only listens in pausable states and while paused.</summary>
        public bool GestureEnabled { get; set; }

        private bool _watchSilence;
        public bool WatchSilence
        {
            get { return _watchSilence; }
            set
            {
                if (_watchSilence == value) return;
                _watchSilence = value;
                _silence.Reset(Time.unscaledTime);   // the wait starts when watching starts
            }
        }

        private IHandInput _hands;
        private readonly PalmUpPauseDetector[] _detectors = { new PalmUpPauseDetector(), new PalmUpPauseDetector() };
        private readonly SilenceWatcher _silence = new SilenceWatcher();

        public void Init(IHandInput hands) { _hands = hands; }

        public void NotifyActivity() { _silence.OnActivity(Time.unscaledTime); }

        /// <summary>Call after any pause/resume so a palm that is still up doesn't fire straight away.</summary>
        public void Disarm()
        {
            foreach (PalmUpPauseDetector d in _detectors) d.Disarm();
            _silence.Reset(Time.unscaledTime);
        }

        private void Update()
        {
            double now = Time.unscaledTime;
            IHandInput hands = _hands ?? HandsHub.Input;

            if (hands != null && GestureEnabled)
            {
                for (int h = 0; h < 2; h++)
                {
                    var hand = (Handedness)h;
                    PalmUpPauseDetector d = _detectors[h];
                    d.HoldSeconds = holdSeconds;
                    d.PalmUpDot = palmUpDot;
                    d.StillMetersPerSecond = stillMetersPerSecond;

                    bool tracked = hands.IsTracked(hand) && hands.Confidence(hand) >= minConfidence;
                    bool fired = d.Update(now, tracked,
                        hands.PalmNormal(hand).ToNumerics(), hands.Wrist(hand).ToNumerics(), hands.IsPinching(hand));
                    if (!fired) continue;

                    Disarm();
                    if (IsPaused) { if (Resumed != null) Resumed(); }
                    else if (Paused != null) Paused();
                    return;
                }
            }

            _silence.SilenceSeconds = silenceSeconds;
            if (_watchSilence && !IsPaused && _silence.Update(now) && SilenceNoticed != null) SilenceNoticed();
        }
    }
}

using System;
using System.Numerics;

namespace GhostFret.Core.Fretboard
{
    /// <summary>
    /// Detects a "touch and hold": a fingertip that stays within StillnessMeters for HoldSeconds.
    /// Used for the calibration touches and the quick re-fit.
    ///
    /// Why the extra parts:
    ///  - Smoothing: tracked fingertips jitter by a few millimetres; a short low-pass filter keeps a
    ///    genuinely still finger from restarting the hold.
    ///  - Arm travel: the finger must first move ArmTravelMeters before a hold can count. A hand that is
    ///    just resting (or the touch we just accepted) can't trigger again by staying put.
    ///  - Lost grace: a brief tracking drop-out pauses the hold instead of resetting it.
    /// </summary>
    public sealed class DwellDetector
    {
        public float HoldSeconds = 1f;
        public float StillnessMeters = 0.007f;
        public float SmoothingSeconds = 0.06f;
        public float ArmTravelMeters = 0.03f;
        public float LostGraceSeconds = 0.2f;

        /// <summary>0..1 while holding still; 0 otherwise.</summary>
        public float Progress { get; private set; }
        /// <summary>True once the finger has travelled far enough for a hold to count.</summary>
        public bool IsArmed { get; private set; }
        /// <summary>Where the current hold started (smoothed).</summary>
        public Vector3 Anchor { get; private set; }
        /// <summary>The latest smoothed fingertip position.</summary>
        public Vector3 Smoothed { get; private set; }
        /// <summary>Average raw position over the completed hold (more accurate than one sample).</summary>
        public Vector3 Result { get; private set; }

        private bool _hasSample;
        private bool _holding;
        private float _holdTime;
        private float _lostTime;
        private float _travel;
        private Vector3 _sum;
        private int _count;

        public DwellDetector() { Reset(true); }

        /// <param name="requireTravel">False = a hold can start right away (e.g. the very first touch).</param>
        public void Reset(bool requireTravel = true)
        {
            _hasSample = false;
            _holding = false;
            _holdTime = 0f;
            _lostTime = 0f;
            _travel = 0f;
            _sum = Vector3.Zero;
            _count = 0;
            Progress = 0f;
            IsArmed = !requireTravel || ArmTravelMeters <= 0f;
        }

        /// <summary>Feed one frame. Returns true on the frame a hold completes (then read Result).</summary>
        public bool Update(Vector3 point, bool tracked, float deltaSeconds)
        {
            if (deltaSeconds < 0f) deltaSeconds = 0f;

            if (!tracked)
            {
                _lostTime += deltaSeconds;
                if (_lostTime > LostGraceSeconds) CancelHold();
                return false;
            }
            _lostTime = 0f;

            Vector3 previous = Smoothed;
            if (!_hasSample || SmoothingSeconds <= 0f)
            {
                Smoothed = point;
                if (!_hasSample) previous = point;
                _hasSample = true;
            }
            else
            {
                float a = 1f - (float)Math.Exp(-deltaSeconds / SmoothingSeconds);
                Smoothed = previous + (point - previous) * a;
            }

            if (!IsArmed)
            {
                _travel += Vector3.Distance(previous, Smoothed);
                if (_travel < ArmTravelMeters) return false;
                IsArmed = true;
            }

            if (!_holding || Vector3.Distance(Smoothed, Anchor) > StillnessMeters)
            {
                // Start (or restart) the hold where the finger is now.
                _holding = true;
                Anchor = Smoothed;
                _holdTime = 0f;
                _sum = Vector3.Zero;
                _count = 0;
            }

            _holdTime += deltaSeconds;
            _sum += point;
            _count++;
            Progress = HoldSeconds > 0f ? Math.Min(1f, _holdTime / HoldSeconds) : 1f;

            if (_holdTime < HoldSeconds) return false;

            Result = _sum / _count;
            // Disarm so the same resting finger can't complete again.
            CancelHold();
            IsArmed = ArmTravelMeters <= 0f;
            _travel = 0f;
            return true;
        }

        private void CancelHold()
        {
            _holding = false;
            _holdTime = 0f;
            _sum = Vector3.Zero;
            _count = 0;
            Progress = 0f;
        }
    }
}

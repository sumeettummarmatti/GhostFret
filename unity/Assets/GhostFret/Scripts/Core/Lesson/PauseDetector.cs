using System.Numerics;

namespace GhostFret.Core.Lesson
{
    /// <summary>
    /// Pause gesture: palm facing up, NOT pinching, hand still, all held for HoldSeconds.
    /// This is deliberately different from the Quest system gesture (palm toward headset + pinch):
    /// any pinch resets the timer, so it can never fire while pinching (DESIGN.md §13.3).
    /// Fires once, then needs the pose to be released before it can fire again.
    /// </summary>
    public sealed class PalmUpPauseDetector
    {
        public double HoldSeconds = 1.0;
        public double PalmUpDot = 0.8;          // dot(palmNormal, up) above this = palm up
        public double StillMetersPerSecond = 0.12; // hand speed below this = still

        private bool _hasLast;
        private Vector3 _lastPos;
        private double _lastTime;
        private double _heldSince = -1;
        private bool _armed = true;

        public bool IsHolding { get { return _heldSince >= 0; } }

        /// <summary>0..1 progress of the current hold (for a gentle fill ring).</summary>
        public double Progress(double now)
        {
            if (_heldSince < 0) return 0;
            double p = (now - _heldSince) / HoldSeconds;
            return p > 1 ? 1 : p;
        }

        /// <summary>Feed one frame. Returns true exactly once when the hold completes.</summary>
        public bool Update(double now, bool tracked, Vector3 palmNormal, Vector3 handPosition, bool pinching)
        {
            double speed = 0;
            if (tracked && _hasLast && now > _lastTime)
                speed = Vector3.Distance(handPosition, _lastPos) / (now - _lastTime);
            _hasLast = tracked;
            _lastPos = handPosition;
            _lastTime = now;

            bool pose = tracked && !pinching && IsPalmUp(palmNormal) && speed <= StillMetersPerSecond;
            if (!pose)
            {
                _heldSince = -1;
                _armed = true;   // released: next hold may fire
                return false;
            }
            if (!_armed) return false;
            if (_heldSince < 0) _heldSince = now;
            if (now - _heldSince >= HoldSeconds)
            {
                _armed = false;
                _heldSince = -1;
                return true;
            }
            return false;
        }

        public bool IsPalmUp(Vector3 palmNormal)
        {
            float len = palmNormal.Length();
            if (len < 1e-6f) return false;
            return Vector3.Dot(palmNormal / len, Vector3.UnitY) > PalmUpDot;
        }

        /// <summary>Call after the app pauses/resumes by other means, so a held palm doesn't instantly re-fire.</summary>
        public void Disarm()
        {
            _armed = false;
            _heldSince = -1;
        }
    }

    /// <summary>
    /// "Stop playing for 10 seconds and we offer a pause." Fires once per silence;
    /// any strum (or Reset) starts the wait again.
    /// </summary>
    public sealed class SilenceWatcher
    {
        public double SilenceSeconds = 10.0;
        private double _lastActivity;
        private bool _fired;

        public SilenceWatcher(double now = 0) { Reset(now); }

        public void Reset(double now) { _lastActivity = now; _fired = false; }
        public void OnActivity(double now) { Reset(now); }

        public bool Update(double now)
        {
            if (_fired || now - _lastActivity < SilenceSeconds) return false;
            _fired = true;
            return true;
        }
    }
}

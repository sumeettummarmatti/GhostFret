using System;

namespace GhostFret.Core.Timing
{
    /// <summary>
    /// The audio clock (Unity's AudioSettings.dspTime) only moves once per audio buffer, so several
    /// frames can read the same value. Hand samples need a smooth time on that same clock to line up
    /// with strum onsets. This keeps a smoothed offset between a smooth frame clock and the audio clock.
    /// </summary>
    public sealed class DspClockSmoother
    {
        public double Smoothing = 0.05;       // how fast the offset follows each new audio-clock reading
        public double ResyncSeconds = 0.1;    // a jump this big (e.g. after a pause) snaps straight to it

        private bool _has;
        private double _offset;
        private double _lastDsp = double.NaN;
        private double _lastReturned = double.NegativeInfinity;

        /// <summary>realtime = a smooth clock (e.g. Time.realtimeSinceStartupAsDouble). Returns smoothed dsp time.</summary>
        public double Update(double realtime, double dsp)
        {
            if (!_has)
            {
                _offset = dsp - realtime;
                _has = true;
                _lastDsp = dsp;
            }
            else if (dsp != _lastDsp)
            {
                _lastDsp = dsp;
                double o = dsp - realtime;
                if (Math.Abs(o - _offset) > ResyncSeconds) { _offset = o; _lastReturned = double.NegativeInfinity; }
                else _offset += Smoothing * (o - _offset);
            }
            double now = realtime + _offset;
            if (now < _lastReturned) now = _lastReturned;   // never run backwards
            _lastReturned = now;
            return now;
        }
    }
}

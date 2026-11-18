using System;
using System.Collections.Generic;

namespace GhostFret.Core.Audio
{
    /// <summary>
    /// Detects the start of strums using spectral flux:
    /// how much the spectrum suddenly gets louder from one frame to the next.
    /// Feed it one magnitude spectrum per hop.
    /// </summary>
    public sealed class OnsetDetector
    {
        public int HistoryFrames = 20;          // frames used for the adaptive threshold
        public double ThresholdMultiplier = 2.5;
        public double MinFlux = 0.5;            // absolute floor (depends on input level)
        public double MinIntervalSeconds = 0.09;

        private double[] _previous;
        private readonly Queue<double> _history = new Queue<double>();
        private double _lastOnsetTime = double.NegativeInfinity;
        private double _previousFlux;

        public void Reset()
        {
            _previous = null;
            _history.Clear();
            _lastOnsetTime = double.NegativeInfinity;
            _previousFlux = 0;
        }

        /// <summary>Returns true if this frame starts a new strum.</summary>
        public bool Process(double[] mags, double frameTimeSeconds)
        {
            double flux = 0;
            if (_previous != null)
            {
                for (int k = 0; k < mags.Length; k++)
                {
                    double rise = mags[k] - _previous[k];
                    if (rise > 0) flux += rise;
                }
            }
            if (_previous == null || _previous.Length != mags.Length) _previous = new double[mags.Length];
            Array.Copy(mags, _previous, mags.Length);

            double mean = 0;
            foreach (double h in _history) mean += h;
            mean = _history.Count > 0 ? mean / _history.Count : 0;
            double threshold = Math.Max(MinFlux, mean * ThresholdMultiplier);

            bool rising = flux > _previousFlux;
            bool onset = flux > threshold && rising && frameTimeSeconds - _lastOnsetTime >= MinIntervalSeconds;
            if (onset) _lastOnsetTime = frameTimeSeconds;

            _history.Enqueue(flux);
            while (_history.Count > HistoryFrames) _history.Dequeue();
            _previousFlux = flux;
            return onset;
        }
    }
}

using System;
using System.Collections.Generic;

namespace GhostFret.Core.Audio
{
    /// <summary>
    /// Detects the start of strums using spectral flux:
    /// how much the spectrum suddenly gets louder from one frame to the next.
    /// Feed it one magnitude spectrum per hop.
    /// The adaptive threshold uses the median of recent flux, not the mean: one big strum
    /// shouldn't raise the bar so high that the next quick strum is missed.
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
        private double[] _sortScratch = new double[0];

        /// <summary>Flux and threshold of the last frame (for tuning and tests).</summary>
        public double LastFlux { get; private set; }
        public double LastThreshold { get; private set; }

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

            double threshold = Math.Max(MinFlux, MedianOfHistory() * ThresholdMultiplier);

            LastFlux = flux;
            LastThreshold = threshold;
            bool rising = flux > _previousFlux;
            bool onset = flux > threshold && rising && frameTimeSeconds - _lastOnsetTime >= MinIntervalSeconds;
            if (onset) _lastOnsetTime = frameTimeSeconds;

            _history.Enqueue(flux);
            while (_history.Count > HistoryFrames) _history.Dequeue();
            _previousFlux = flux;
            return onset;
        }
    
        private double MedianOfHistory()
        {
            int n = _history.Count;
            if (n == 0) return 0;
            if (_sortScratch.Length < n) _sortScratch = new double[Math.Max(n, HistoryFrames)];
            int i = 0;
            foreach (double h in _history) _sortScratch[i++] = h;
            Array.Sort(_sortScratch, 0, n);
            return n % 2 == 1 ? _sortScratch[n / 2] : 0.5 * (_sortScratch[n / 2 - 1] + _sortScratch[n / 2]);
        }
    }
}

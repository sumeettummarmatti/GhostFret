using System;
using System.Collections.Generic;

namespace GhostFret.Core.Timing
{
    public struct LatencyEstimate
    {
        public bool Ok;
        public double LatencyMs;   // put this in BeatGrid.LatencyMs
        public int Pairs;          // strums matched to a click
        public double SpreadMs;    // median distance from the median: how steady the strums were
    }

    /// <summary>
    /// "Strum on the four clicks": each strum is paired with its nearest click; the median offset
    /// is the delay from a real strum to the app hearing it (mic + processing). Median, not mean,
    /// so one stray strum can't skew it (DESIGN.md §11.4).
    /// </summary>
    public static class LatencyCalibration
    {
        public const double MaxPairMs = 350;     // a strum further than this from every click is ignored
        public const double MaxLatencyMs = 300;  // anything bigger is a mistake, not latency
        public const double MaxSpreadMs = 80;    // strums all over the place → ask to try again
        public const int MinPairs = 3;

        public static LatencyEstimate Estimate(IList<double> clickTimes, IList<double> strumTimes)
        {
            var result = new LatencyEstimate();
            if (clickTimes == null || strumTimes == null || clickTimes.Count == 0) return result;

            // Best (closest) strum for each click; each click used once.
            var best = new double?[clickTimes.Count];
            foreach (double strum in strumTimes)
            {
                int nearest = -1;
                double nearestMs = double.MaxValue;
                for (int c = 0; c < clickTimes.Count; c++)
                {
                    double ms = (strum - clickTimes[c]) * 1000.0;
                    if (Math.Abs(ms) < Math.Abs(nearestMs)) { nearestMs = ms; nearest = c; }
                }
                if (nearest < 0 || Math.Abs(nearestMs) > MaxPairMs) continue;
                if (!best[nearest].HasValue || Math.Abs(nearestMs) < Math.Abs(best[nearest].Value)) best[nearest] = nearestMs;
            }

            var offsets = new List<double>();
            foreach (var b in best) if (b.HasValue) offsets.Add(b.Value);
            result.Pairs = offsets.Count;
            if (offsets.Count < MinPairs) return result;

            double median = Median(offsets);
            var deviations = new List<double>();
            foreach (double o in offsets) deviations.Add(Math.Abs(o - median));
            result.SpreadMs = Median(deviations);
            result.LatencyMs = Math.Max(0, Math.Min(MaxLatencyMs, median));
            result.Ok = result.SpreadMs <= MaxSpreadMs && median <= MaxLatencyMs;
            return result;
        }

        public static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = new List<double>(values);
            sorted.Sort();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }
    }
}

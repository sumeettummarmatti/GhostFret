using System;

namespace GhostFret.Core.Audio
{
    public struct PitchResult
    {
        public bool HasPitch;
        public double Frequency;
        /// <summary>0..1, higher is more certain.</summary>
        public double Confidence;

        public static PitchResult None { get { return new PitchResult { HasPitch = false }; } }
    }

    /// <summary>
    /// YIN pitch detector for one note at a time (used by the tuner).
    /// de Cheveigné & Kawahara (2002).
    /// </summary>
    public sealed class YinPitchDetector
    {
        public double MinFrequency = 70.0;     // a bit below low E (82 Hz) for down-tunings
        public double MaxFrequency = 1000.0;
        public double Threshold = 0.15;        // lower = stricter
        public double MinRms = 0.005;          // ignore near-silence

        public PitchResult Detect(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length < 512) return PitchResult.None;
            if (Fft.Rms(samples) < MinRms) return PitchResult.None;

            int maxTau = Math.Min(samples.Length / 2, (int)(sampleRate / MinFrequency));
            int minTau = Math.Max(2, (int)(sampleRate / MaxFrequency));
            int windowSize = samples.Length - maxTau;

            // 1. Difference function
            var diff = new double[maxTau + 1];
            for (int tau = 1; tau <= maxTau; tau++)
            {
                double sum = 0;
                for (int i = 0; i < windowSize; i++)
                {
                    double d = samples[i] - samples[i + tau];
                    sum += d * d;
                }
                diff[tau] = sum;
            }

            // 2. Cumulative mean normalised difference
            var cmnd = new double[maxTau + 1];
            cmnd[0] = 1;
            double running = 0;
            for (int tau = 1; tau <= maxTau; tau++)
            {
                running += diff[tau];
                cmnd[tau] = running > 0 ? diff[tau] * tau / running : 1;
            }

            // 3. First dip below the threshold, then walk to its bottom
            int bestTau = -1;
            for (int tau = minTau; tau <= maxTau; tau++)
            {
                if (cmnd[tau] < Threshold)
                {
                    while (tau + 1 <= maxTau && cmnd[tau + 1] < cmnd[tau]) tau++;
                    bestTau = tau;
                    break;
                }
            }
            if (bestTau < 0) return PitchResult.None;

            // 4. Parabolic interpolation for sub-sample accuracy
            double refined = bestTau;
            if (bestTau > 1 && bestTau < maxTau)
            {
                double a = cmnd[bestTau - 1], b = cmnd[bestTau], c = cmnd[bestTau + 1];
                double denom = a - 2 * b + c;
                if (Math.Abs(denom) > 1e-12) refined = bestTau + 0.5 * (a - c) / denom;
            }

            return new PitchResult
            {
                HasPitch = true,
                Frequency = sampleRate / refined,
                Confidence = Math.Max(0, Math.Min(1, 1 - cmnd[bestTau]))
            };
        }
    }
}

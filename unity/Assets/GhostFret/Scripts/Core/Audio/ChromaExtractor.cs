using System;
using GhostFret.Core.Music;

namespace GhostFret.Core.Audio
{
    /// <summary>
    /// Turns a magnitude spectrum into a 12-value "chroma" vector:
    /// how much energy each note name (C, C#, … B) has, regardless of octave.
    /// Only spectral peaks are counted, at their interpolated frequency. Why: at 8192 points a
    /// bin is ~6 Hz wide, so a low E (82 Hz) also lights up the F bin next to it. Counting each
    /// peak once, at its true frequency, keeps the low strings from smearing into wrong notes.
    /// </summary>
    public static class ChromaExtractor
    {
        public const double MinHz = 60.0;
        public const double MaxHz = 2000.0;

        /// <summary>Peaks quieter than this fraction of the loudest peak are ignored (−40 dB). Keeps noise out.</summary>
        public const double MinPeakRatio = 0.01;

        public static double[] FromSpectrum(double[] mags, int sampleRate, int fftSize)
        {
            var chroma = new double[12];
            FromSpectrum(mags, sampleRate, fftSize, chroma);
            return chroma;
        }

        /// <summary>Allocation-free version: writes into chroma (length 12).</summary>
        public static void FromSpectrum(double[] mags, int sampleRate, int fftSize, double[] chroma)
        {
            Array.Clear(chroma, 0, 12);
            double binHz = (double)sampleRate / fftSize;
            int lo = Math.Max(1, (int)Math.Floor(MinHz / binHz));
            int hi = Math.Min(mags.Length - 2, (int)Math.Ceiling(MaxHz / binHz));

            double loudest = 0;
            for (int k = lo; k <= hi; k++) if (mags[k] > loudest) loudest = mags[k];
            if (loudest <= 1e-12) return;
            double floor = loudest * MinPeakRatio;

            for (int k = lo; k <= hi; k++)
            {
                double b = mags[k];
                if (b < floor || b <= mags[k - 1] || b < mags[k + 1]) continue;   // local maxima only

                // Parabolic interpolation: true peak position and height between bins.
                double a = mags[k - 1], c = mags[k + 1];
                double denom = a - 2 * b + c;
                double p = Math.Abs(denom) > 1e-12 ? 0.5 * (a - c) / denom : 0;
                double f = (k + p) * binHz;
                if (f < MinHz || f > MaxHz) continue;
                double height = b - 0.25 * (a - c) * p;

                int midi = (int)Math.Round(NoteMath.FrequencyToMidi(f));
                chroma[NoteMath.PitchClass(midi)] += height;   // magnitude, not energy: squaring lets one loud harmonic drown the rest
            }
            Normalize(chroma);
        }

        public static void Normalize(double[] v)
        {
            double len = 0;
            for (int i = 0; i < v.Length; i++) len += v[i] * v[i];
            len = Math.Sqrt(len);
            if (len < 1e-12) return;
            for (int i = 0; i < v.Length; i++) v[i] /= len;
        }

        public static double Cosine(double[] a, double[] b)
        {
            double dot = 0, la = 0, lb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                la += a[i] * a[i];
                lb += b[i] * b[i];
            }
            if (la < 1e-12 || lb < 1e-12) return 0;
            return dot / Math.Sqrt(la * lb);
        }
    }
}

using System;
using GhostFret.Core.Music;

namespace GhostFret.Core.Audio
{
    /// <summary>
    /// Turns a magnitude spectrum into a 12-value "chroma" vector:
    /// how much energy each note name (C, C#, … B) has, regardless of octave.
    /// </summary>
    public static class ChromaExtractor
    {
        public const double MinHz = 60.0;
        public const double MaxHz = 2000.0;

        public static double[] FromSpectrum(double[] mags, int sampleRate, int fftSize)
        {
            var chroma = new double[12];
            for (int k = 1; k < mags.Length; k++)
            {
                double f = Fft.BinFrequency(k, sampleRate, fftSize);
                if (f < MinHz || f > MaxHz) continue;
                int midi = (int)Math.Round(NoteMath.FrequencyToMidi(f));
                chroma[NoteMath.PitchClass(midi)] += mags[k] * mags[k];   // energy
            }
            Normalize(chroma);
            return chroma;
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

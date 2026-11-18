using System;

namespace GhostFret.Core.Audio
{
    /// <summary>
    /// Simple radix-2 FFT plus window and magnitude helpers.
    /// Readable over fast; fine for a few FFTs per frame on Quest.
    /// </summary>
    public static class Fft
    {
        public static bool IsPowerOfTwo(int n)
        {
            return n > 0 && (n & (n - 1)) == 0;
        }

        /// <summary>In-place complex FFT. Length must be a power of two.</summary>
        public static void Transform(double[] re, double[] im)
        {
            int n = re.Length;
            if (!IsPowerOfTwo(n) || im.Length != n)
                throw new ArgumentException("FFT length must be a power of two and arrays must match.");

            // Bit-reversal reordering
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double tr = re[i]; re[i] = re[j]; re[j] = tr;
                    double ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }

            // Butterflies
            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2.0 * Math.PI / len;
                double wRe = Math.Cos(angle), wIm = Math.Sin(angle);
                for (int start = 0; start < n; start += len)
                {
                    double curRe = 1.0, curIm = 0.0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = start + k, b = a + len / 2;
                        double tRe = re[b] * curRe - im[b] * curIm;
                        double tIm = re[b] * curIm + im[b] * curRe;
                        re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                        re[a] += tRe; im[a] += tIm;
                        double nextRe = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = nextRe;
                    }
                }
            }
        }

        /// <summary>Hann window coefficients.</summary>
        public static double[] HannWindow(int n)
        {
            var w = new double[n];
            for (int i = 0; i < n; i++) w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
            return w;
        }

        /// <summary>
        /// Windowed magnitude spectrum of the first fftSize samples (zero-padded if shorter).
        /// Returns fftSize/2 bins; bin k is at k * sampleRate / fftSize Hz.
        /// </summary>
        public static double[] MagnitudeSpectrum(float[] samples, int fftSize, double[] window = null)
        {
            if (window == null) window = HannWindow(fftSize);
            var re = new double[fftSize];
            var im = new double[fftSize];
            int count = Math.Min(samples.Length, fftSize);
            for (int i = 0; i < count; i++) re[i] = samples[i] * window[i];
            Transform(re, im);
            var mags = new double[fftSize / 2];
            for (int k = 0; k < mags.Length; k++) mags[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
            return mags;
        }

        public static double BinFrequency(int bin, int sampleRate, int fftSize)
        {
            return (double)bin * sampleRate / fftSize;
        }

        /// <summary>Largest magnitude between two frequencies (inclusive).</summary>
        public static double PeakInRange(double[] mags, int sampleRate, int fftSize, double lowHz, double highHz)
        {
            int lo = Math.Max(0, (int)Math.Floor(lowHz * fftSize / sampleRate));
            int hi = Math.Min(mags.Length - 1, (int)Math.Ceiling(highHz * fftSize / sampleRate));
            double peak = 0;
            for (int k = lo; k <= hi; k++) if (mags[k] > peak) peak = mags[k];
            return peak;
        }

        public static double Rms(float[] samples)
        {
            if (samples.Length == 0) return 0;
            double sum = 0;
            for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            return Math.Sqrt(sum / samples.Length);
        }
    }
}

using System;
using System.Collections.Generic;
using GhostFret.Core.Music;

namespace GhostFret.Core.Audio
{
    public sealed class StringCheckResult
    {
        /// <summary>Strings that should sound but are much quieter than the rest (likely muted).</summary>
        public readonly List<int> WeakStrings = new List<int>();
        /// <summary>Strings that should be skipped but seem to be ringing.</summary>
        public readonly List<int> UnwantedStrings = new List<int>();
        /// <summary>Strings we can't judge, because another string shares the same frequency.</summary>
        public readonly List<int> UncheckableStrings = new List<int>();
        /// <summary>
        /// Share (0..1) of the spectrum's peak energy that no target string (or its harmonics) explains.
        /// High = a note is sounding that the chord shouldn't have (wrong fret, another chord, noise).
        /// </summary>
        public double UnexplainedFraction;
    }

    /// <summary>
    /// Per-string check after a strum: looks for energy at each string's expected note.
    /// Only reports a problem when the string's note can't be explained by another string,
    /// so we stay quiet rather than give wrong feedback.
    /// </summary>
    public sealed class StringChecker
    {
        public double WeakRatio = 0.15;        // weaker than 15% of the median string = "weak"
        public double UnwantedRatio = 0.5;     // a skipped string at 50% of median = "ringing"
        public int MaxHarmonicForMasking = 4;
        public double MatchToleranceCents = 35;
        /// <summary>Harmonics of each target string counted as "expected" when explaining peaks.</summary>
        public int MaxHarmonicExplained = 8;
        /// <summary>A peak within this many cents of an expected harmonic is explained (allows slight detune and string stretch).</summary>
        public double ExplainToleranceCents = 40;
        /// <summary>Peaks quieter than this fraction of the loudest are ignored when explaining (−26 dB, above window side lobes).</summary>
        public double ExplainPeakFloor = 0.05;
        public double ExplainMinHz = 60, ExplainMaxHz = 2500;

        public StringCheckResult Check(double[] mags, int sampleRate, int fftSize, ChordShape target, GuitarTuning tuning)
        {
            var result = new StringCheckResult();
            result.UnexplainedFraction = Unexplained(mags, sampleRate, fftSize, target, tuning);
            var strength = new double[6];
            var playedStrengths = new List<double>();

            for (int s = 0; s < 6; s++)
            {
                int midi = target.SoundingMidi(s, tuning);
                if (midi < 0) continue;
                strength[s] = StrengthAt(mags, sampleRate, fftSize, NoteMath.MidiToFrequency(midi));
                playedStrengths.Add(strength[s]);
            }
            if (playedStrengths.Count == 0) return result;
            double median = Median(playedStrengths);
            if (median <= 1e-9) return result;

            for (int s = 0; s < 6; s++)
            {
                int midi = target.SoundingMidi(s, tuning);
                if (midi < 0) continue;
                if (IsMaskedByOtherString(s, NoteMath.MidiToFrequency(midi), target, tuning))
                {
                    result.UncheckableStrings.Add(s);
                    continue;
                }
                if (strength[s] < WeakRatio * median) result.WeakStrings.Add(s);
            }

            HashSet<int> chordPitchClasses = target.PitchClasses(tuning);
            for (int s = 0; s < 6; s++)
            {
                if (target.IsStringPlayed(s)) continue;
                int openMidi = tuning.OpenStringMidi[s];
                if (chordPitchClasses.Contains(NoteMath.PitchClass(openMidi))) continue;   // can't tell it apart
                double f = NoteMath.MidiToFrequency(openMidi);
                if (IsMaskedByOtherString(s, f, target, tuning)) continue;
                if (StrengthAt(mags, sampleRate, fftSize, f) > UnwantedRatio * median) result.UnwantedStrings.Add(s);
            }
            return result;
        }

        /// <summary>
        /// Walks the spectral peaks and asks of each: is this a harmonic of a note the chord should sound?
        /// Returns the magnitude share of peaks that aren't. This is what lets us tell E from Em:
        /// a G# on the G string isn't a harmonic of any Em note.
        /// </summary>
        public double Unexplained(double[] mags, int sampleRate, int fftSize, ChordShape target, GuitarTuning tuning)
        {
            double binHz = (double)sampleRate / fftSize;
            int lo = Math.Max(1, (int)Math.Floor(ExplainMinHz / binHz));
            int hi = Math.Min(mags.Length - 2, (int)Math.Ceiling(ExplainMaxHz / binHz));
            double loudest = 0;
            for (int k = lo; k <= hi; k++) if (mags[k] > loudest) loudest = mags[k];
            if (loudest <= 1e-12) return 0;
            double floor = loudest * ExplainPeakFloor;

            double total = 0, unexplained = 0;
            for (int k = lo; k <= hi; k++)
            {
                double b = mags[k];
                if (b < floor || b <= mags[k - 1] || b < mags[k + 1]) continue;
                double a = mags[k - 1], c = mags[k + 1];
                double denom = a - 2 * b + c;
                double p = Math.Abs(denom) > 1e-12 ? 0.5 * (a - c) / denom : 0;
                double f = (k + p) * binHz;
                double height = b - 0.25 * (a - c) * p;
                total += height;
                if (!IsExplained(f, target, tuning)) unexplained += height;
            }
            return total > 0 ? unexplained / total : 0;
        }

        private bool IsExplained(double frequency, ChordShape target, GuitarTuning tuning)
        {
            for (int s = 0; s < 6; s++)
            {
                int midi = target.SoundingMidi(s, tuning);
                if (midi < 0) continue;
                double f0 = NoteMath.MidiToFrequency(midi);
                double ratio = frequency / f0;
                int h = (int)Math.Round(ratio);
                if (h < 1 || h > MaxHarmonicExplained) continue;
                if (Math.Abs(NoteMath.CentsBetween(frequency, f0 * h)) < ExplainToleranceCents) return true;
            }
            return false;
        }

        /// <summary>Peak energy near a frequency (± 3% or ± 2 bins, whichever is wider).</summary>
        public static double StrengthAt(double[] mags, int sampleRate, int fftSize, double frequency)
        {
            double binHz = (double)sampleRate / fftSize;
            double half = Math.Max(0.03 * frequency, 2 * binHz);
            return Fft.PeakInRange(mags, sampleRate, fftSize, frequency - half, frequency + half);
        }

        /// <summary>True if any other played string has a harmonic at this frequency.</summary>
        private bool IsMaskedByOtherString(int stringIndex, double frequency, ChordShape target, GuitarTuning tuning)
        {
            for (int other = 0; other < 6; other++)
            {
                if (other == stringIndex) continue;
                int midi = target.SoundingMidi(other, tuning);
                if (midi < 0) continue;
                double f0 = NoteMath.MidiToFrequency(midi);
                for (int h = 1; h <= MaxHarmonicForMasking; h++)
                {
                    if (Math.Abs(NoteMath.CentsBetween(frequency, f0 * h)) < MatchToleranceCents) return true;
                }
            }
            return false;
        }

        private static double Median(List<double> values)
        {
            var sorted = new List<double>(values);
            sorted.Sort();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
        }
    }
}

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

        public StringCheckResult Check(double[] mags, int sampleRate, int fftSize, ChordShape target, GuitarTuning tuning)
        {
            var result = new StringCheckResult();
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

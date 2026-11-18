using System;
using System.Collections.Generic;
using GhostFret.Core.Music;

namespace GhostFret.Core.Audio
{
    public struct ChordMatchResult
    {
        public string BestChord;
        public double BestScore;      // cosine similarity 0..1
        public double TargetScore;
        /// <summary>True if the played sound matches the target chord (or is basically tied with the best).</summary>
        public bool MatchesTarget;
        /// <summary>
        /// Share (0..1) of the heard note energy that falls on note names the target chord doesn't have.
        /// High = something extra is sounding (a wrong fret, a skipped string, another chord).
        /// </summary>
        public double OutsideFraction;
    }

    /// <summary>
    /// Compares a chroma vector with templates built from the chord library.
    /// </summary>
    public sealed class ChordMatcher
    {
        /// <summary>Target counts as matched if within this much of the best score.</summary>
        public double TieMargin = 0.02;

        private readonly Dictionary<string, double[]> _templates = new Dictionary<string, double[]>();
        private readonly Dictionary<string, bool[]> _members = new Dictionary<string, bool[]>();

        public ChordMatcher(GuitarTuning tuning)
        {
            foreach (var shape in ChordLibrary.All())
            {
                _templates[shape.name] = BuildTemplate(shape, tuning);
                var member = new bool[12];
                foreach (int pc in shape.PitchClasses(tuning)) member[pc] = true;
                _members[shape.name] = member;
            }
        }

        /// <summary>Harmonics included in each template (1 = fundamentals only).</summary>
        public const int TemplateHarmonics = 6;

        /// <summary>
        /// Template = which pitch classes the chord sounds, weighted by how many strings sound each.
        /// Each string also adds its first few harmonics (weight 1/h), because a real string's
        /// 3rd harmonic (a fifth up) and 5th harmonic (a major third up) show up in the chroma too.
        /// Without them, a minor chord's own harmonics make it look a little "major".
        /// </summary>
        public static double[] BuildTemplate(ChordShape shape, GuitarTuning tuning)
        {
            var t = new double[12];
            for (int s = 0; s < 6; s++)
            {
                int midi = shape.SoundingMidi(s, tuning);
                if (midi < 0) continue;
                for (int h = 1; h <= TemplateHarmonics; h++)
                {
                    int harmonicMidi = midi + (int)Math.Round(12.0 * Math.Log(h, 2.0));
                    t[NoteMath.PitchClass(harmonicMidi)] += 1.0 / h;
                }
            }
            ChromaExtractor.Normalize(t);
            return t;
        }

        public ChordMatchResult Match(double[] chroma, string targetChord)
        {
            var result = new ChordMatchResult { BestScore = -1 };
            foreach (var pair in _templates)
            {
                double score = ChromaExtractor.Cosine(chroma, pair.Value);
                if (score > result.BestScore)
                {
                    result.BestScore = score;
                    result.BestChord = pair.Key;
                }
                if (pair.Key == targetChord) result.TargetScore = score;
            }
            result.MatchesTarget = result.BestChord == targetChord
                                   || result.TargetScore >= result.BestScore - TieMargin;

            bool[] member;
            if (targetChord != null && _members.TryGetValue(targetChord, out member))
            {
                double total = 0, outside = 0;
                for (int pc = 0; pc < 12; pc++)
                {
                    total += chroma[pc];
                    if (!member[pc]) outside += chroma[pc];
                }
                result.OutsideFraction = total > 1e-12 ? outside / total : 0;
            }
            return result;
        }
    }
}

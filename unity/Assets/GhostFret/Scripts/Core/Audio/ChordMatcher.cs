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
    }

    /// <summary>
    /// Compares a chroma vector with templates built from the chord library.
    /// </summary>
    public sealed class ChordMatcher
    {
        /// <summary>Target counts as matched if within this much of the best score.</summary>
        public double TieMargin = 0.02;

        private readonly Dictionary<string, double[]> _templates = new Dictionary<string, double[]>();

        public ChordMatcher(GuitarTuning tuning)
        {
            foreach (var shape in ChordLibrary.All())
            {
                _templates[shape.name] = BuildTemplate(shape, tuning);
            }
        }

        /// <summary>
        /// Template = which pitch classes the chord sounds, weighted by how many strings sound each.
        /// </summary>
        public static double[] BuildTemplate(ChordShape shape, GuitarTuning tuning)
        {
            var t = new double[12];
            for (int s = 0; s < 6; s++)
            {
                int midi = shape.SoundingMidi(s, tuning);
                if (midi >= 0) t[NoteMath.PitchClass(midi)] += 1.0;
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
            return result;
        }
    }
}

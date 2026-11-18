using System;
using System.Numerics;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Music;

namespace GhostFret.Core.AirMode
{
    public struct AirShapeResult
    {
        /// <summary>0..1, how much the hand looks like the target shape.</summary>
        public float Confidence;
        /// <summary>True when the shape looks close enough (or tracking is too unsure to say otherwise).</summary>
        public bool LooksRight;
        /// <summary>One short, kind tip, or "" when there's nothing useful (and true) to say.</summary>
        public string Hint;
        /// <summary>Finger the hint is about (1 index … 4 pinky), or 0.</summary>
        public int FocusFinger;
        /// <summary>False when tracking was too unsure to judge; the result is then generous.</summary>
        public bool Judged;
    }

    /// <summary>
    /// Approximate chord-shape check for Air mode, from the fretting hand's finger curl and (optionally)
    /// fingertip positions against the virtual neck. Approximate by design (DESIGN.md §10.4):
    /// it only gives gentle hints, never says a chord is wrong, and stays quiet when tracking is unsure.
    /// Finger numbers match ChordShape: 1 index, 2 middle, 3 ring, 4 pinky (0 thumb is ignored).
    /// </summary>
    public sealed class AirChordMatcher
    {
        public float MinTrackingConfidence = 0.5f; // below this we don't judge (rule #5)
        public float CurlLow = 0.20f;              // a pressing finger with curl ≤ this scores 0
        public float CurlGood = 0.45f;             // … and ≥ this scores 1
        public float UnusedCurlLimit = 0.85f;      // a free finger curled past this costs a little
        public float UnusedWeight = 0.3f;
        public float NearMeters = 0.02f;           // fingertip this close to its spot scores 1
        public float FarMeters = 0.06f;            // … this far scores 0
        public float DepthWeight = 0.3f;           // hovering above the virtual strings is fine in the air
        public float PositionWeight = 0.6f;        // share of position vs curl per finger when tips are given
        public float LooksRightThreshold = 0.7f;
        public float HandFarMeters = 0.15f;        // whole hand this far from its spots: "bring your hand over"

        private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinky" };

        /// <param name="curl">5 values, thumb..pinky, 0 straight … 1 curled.</param>
        /// <param name="tips">Optional 5 fingertip positions (world), thumb..pinky. Null = curl only.</param>
        /// <param name="geometry">The (virtual) fretboard; needed for tips.</param>
        /// <param name="trackingConfidence">0..1 from hand tracking.</param>
        public AirShapeResult Match(ChordShape target, float[] curl, Vector3[] tips, FretboardGeometry geometry, float trackingConfidence)
        {
            var result = new AirShapeResult { Confidence = 1f, LooksRight = true, Hint = "", FocusFinger = 0, Judged = false };
            if (target == null || target.fingers == null || curl == null || curl.Length < 5) return result;
            if (trackingConfidence < MinTrackingConfidence) return result; // be generous, say nothing

            bool usePositions = tips != null && tips.Length >= 5 && geometry != null;

            // Where each finger should be (string + fret), from the chord shape. A barre uses the first string.
            var stringOf = new int[5];
            var fretOf = new int[5];
            var used = new bool[5];
            for (int s = 0; s < 6; s++)
            {
                int f = target.fingers[s];
                if (f < 1 || f > 4 || target.frets[s] <= 0 || used[f]) continue;
                used[f] = true;
                stringOf[f] = s;
                fretOf[f] = target.frets[s];
            }

            float freeSum = 0f, freeWeight = 0f;
            float usedSum = 0f, usedMin = 1f; int usedCount = 0;
            int posCount = 0;
            float distSum = 0f;
            int worstFinger = 0; float worstScore = 2f; bool worstIsCurl = false;

            for (int f = 1; f <= 4; f++)
            {
                float c = Clamp01(curl[f]);
                if (!used[f])
                {
                    // Free fingers can do almost anything; only a tight fist costs a little.
                    float free = c > UnusedCurlLimit ? 1f - 0.5f * Clamp01((c - UnusedCurlLimit) / (1f - UnusedCurlLimit + 1e-6f)) : 1f;
                    freeSum += UnusedWeight * free;
                    freeWeight += UnusedWeight;
                    continue;
                }

                float curlScore = Clamp01((c - CurlLow) / (CurlGood - CurlLow));
                float fingerScore = curlScore;   // this finger's overall score
                float issueScore = curlScore;    // its weakest part, for choosing the hint
                bool curlIsWorse = true;
                if (usePositions)
                {
                    float d = SpotDistance(geometry, tips[f], stringOf[f], fretOf[f]);
                    distSum += d;
                    float posScore = 1f - Clamp01((d - NearMeters) / (FarMeters - NearMeters));
                    posCount++;
                    fingerScore = (1f - PositionWeight) * curlScore + PositionWeight * posScore;
                    if (posScore < curlScore) { issueScore = posScore; curlIsWorse = false; }
                }

                usedSum += fingerScore;
                usedMin = Math.Min(usedMin, fingerScore);
                usedCount++;
                if (issueScore < worstScore) { worstScore = issueScore; worstFinger = f; worstIsCurl = curlIsWorse; }
            }

            // Half average, half weakest finger: one finger far off matters even if the others are perfect.
            float usedPart = usedCount > 0 ? 0.5f * (usedSum / usedCount) + 0.5f * usedMin : 1f;
            float confidence = (usedPart * usedCount + freeSum) / Math.Max(1e-6f, usedCount + freeWeight);

            result.Confidence = Clamp01(confidence);
            result.LooksRight = result.Confidence >= LooksRightThreshold;
            result.Judged = true;

            if (!result.LooksRight)
            {
                if (usePositions && posCount > 0 && distSum / posCount > HandFarMeters)
                {
                    result.Hint = "Bring your hand to the ghost fingers.";
                }
                else if (worstFinger > 0 && worstScore < 0.8f)
                {
                    result.FocusFinger = worstFinger;
                    string name = FingerNames[worstFinger];
                    result.Hint = worstIsCurl
                        ? "Curl your " + name + " finger a little more."
                        : Cap(name) + " finger: " + GuitarTuning.StringNames[stringOf[worstFinger]] + " string, fret " + fretOf[worstFinger] + ".";
                }
                else
                {
                    result.Hint = "Match the ghost fingers, then strum.";
                }
            }
            return result;
        }

        /// <summary>Distance from a fingertip to where it should press, counting height above the strings only partly.</summary>
        public float SpotDistance(FretboardGeometry g, Vector3 tip, int stringIndex, int fret)
        {
            Vector3 d = tip - g.FingerPosition(stringIndex, fret);
            float along = Vector3.Dot(d, g.NeckAxis);
            float across = Vector3.Dot(d, g.AcrossAxis);
            float up = Vector3.Dot(d, g.SurfaceNormal) * DepthWeight;
            return (float)Math.Sqrt(along * along + across * across + up * up);
        }

        private static string Cap(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
    }
}

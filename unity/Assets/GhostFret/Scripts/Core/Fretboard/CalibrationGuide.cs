using System;
using System.Numerics;

namespace GhostFret.Core.Fretboard
{
    /// <summary>
    /// Helps the three calibration touches go smoothly (DESIGN.md §10.2):
    ///  - predicts where the next touch probably is, so the glowing target can sit there;
    ///  - checks each touch is roughly where it should be, with a kind, specific tip if not.
    /// Steps: 0 = nut by the low E, 1 = nut by the high E, 2 = 12th fret on the low E.
    ///
    /// Seated playing posture assumed for predictions (they are only hints; any touch is accepted
    /// if it passes CheckTouch): the low E is the top string, so low E → high E points roughly down;
    /// right-handed players have the body to their right, so nut → body points to their right.
    /// </summary>
    public static class CalibrationGuide
    {
        public const int StepCount = 3;

        /// <summary>Typical distance between the two nut touches (metres).</summary>
        public const float TypicalNutWidth = 0.043f;
        /// <summary>Typical nut → 12th fret distance (half of a ~648 mm scale).</summary>
        public const float TypicalHalfScale = 0.324f;

        // Generous bounds: the strict check is FretboardGeometry.IsPlausible.
        public const float MinNutTouchGap = 0.02f;
        public const float MaxNutTouchGap = 0.08f;
        public const float MinTwelfthDistance = 0.20f;
        public const float MaxTwelfthDistance = 0.42f;

        public static Vector3 PredictNutHighE(Vector3 nutLowE, Vector3 worldDown)
        {
            return nutLowE + SafeNormalize(worldDown, new Vector3(0, -1, 0)) * TypicalNutWidth;
        }

        /// <summary>Best guess for the 12th fret: along the player's right (left if left-handed), across the neck at right angles.</summary>
        public static Vector3 PredictTwelfth(Vector3 nutLowE, Vector3 nutHighE, Vector3 playerRight, bool leftHanded)
        {
            Vector3 across = SafeNormalize(nutHighE - nutLowE, new Vector3(0, -1, 0));
            Vector3 neck = leftHanded ? -playerRight : playerRight;
            neck -= Vector3.Dot(neck, across) * across;
            neck = SafeNormalize(neck, leftHanded ? new Vector3(-1, 0, 0) : new Vector3(1, 0, 0));
            return nutLowE + neck * TypicalHalfScale;
        }

        /// <summary>
        /// Is this touch roughly in the right place for the step? If not, tip says what to do (kindly).
        /// Earlier touches that aren't known yet can be passed as Vector3.Zero.
        /// </summary>
        public static bool CheckTouch(int step, Vector3 touch, Vector3 nutLowE, Vector3 nutHighE, out string tip)
        {
            tip = "";
            if (step == 1)
            {
                float gap = Vector3.Distance(touch, nutLowE);
                if (gap < MinNutTouchGap)
                {
                    tip = "That's very close to the first touch. Touch the other end of the nut.";
                    return false;
                }
                if (gap > MaxNutTouchGap)
                {
                    tip = "That's a bit far. The thinnest string is just across the nut.";
                    return false;
                }
            }
            else if (step == 2)
            {
                float d = Vector3.Distance(touch, nutLowE);
                if (d < MinTwelfthDistance)
                {
                    tip = "The 12th fret is further along the neck, usually by two dots.";
                    return false;
                }
                if (d > MaxTwelfthDistance)
                {
                    tip = "That's past the 12th fret. Look for the fret with two dots.";
                    return false;
                }
                // Measured across the neck: the low E side is near 0, the high E side near the nut width.
                Vector3 acrossVec = nutHighE - nutLowE;
                float nutWidth = acrossVec.Length();
                if (nutWidth > 1e-4f && Vector3.Dot(touch - nutLowE, acrossVec / nutWidth) > 0.7f * nutWidth)
                {
                    tip = "Touch the 12th fret on the thickest string.";
                    return false;
                }
            }
            return true;
        }

        private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float len = v.Length();
            return len > 1e-6f ? v / len : fallback;
        }
    }
}

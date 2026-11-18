using System;
using System.Numerics;

namespace GhostFret.Core.Fretboard
{
    /// <summary>
    /// The 3D model of the player's real fretboard, built from three fingertip touches:
    ///   1. nut, low E side   2. nut, high E side   3. 12th fret, low E side
    /// All units are metres, in the same world space as the touches.
    /// </summary>
    public sealed class FretboardGeometry
    {
        /// <summary>Common scale lengths (metres): 24", 24.75", 25", 25.5", classical 650 mm.</summary>
        public static readonly float[] CommonScaleLengths = { 0.610f, 0.628f, 0.635f, 0.648f, 0.650f };

        public const float DefaultSnapTolerance = 0.02f;   // snap if within 2 cm of a common scale
        public const float DefaultTaper = 0.20f;           // string spread at 12th fret is ~20% wider than at nut

        public Vector3 NutLowE { get; private set; }
        public Vector3 NutHighE { get; private set; }
        public Vector3 TwelfthLowE { get; private set; }

        /// <summary>Unit vector from the nut toward the body.</summary>
        public Vector3 NeckAxis { get; private set; }
        /// <summary>Unit vector from the low E string toward the high E string, at right angles to the neck.</summary>
        public Vector3 AcrossAxis { get; private set; }
        /// <summary>Unit vector pointing out of the fretboard surface.</summary>
        public Vector3 SurfaceNormal { get; private set; }

        public float ScaleLength { get; private set; }
        public float MeasuredScaleLength { get; private set; }
        public float NutWidth { get; private set; }
        public float Taper { get; private set; }
        public bool WasSnapped { get; private set; }

        private FretboardGeometry() { }

        public static FretboardGeometry FromCalibration(
            Vector3 nutLowE, Vector3 nutHighE, Vector3 twelfthLowE,
            float snapTolerance = DefaultSnapTolerance, float taper = DefaultTaper)
        {
            var g = new FretboardGeometry();
            g.NutLowE = nutLowE;
            g.NutHighE = nutHighE;
            g.TwelfthLowE = twelfthLowE;
            g.Taper = taper;

            g.NeckAxis = Vector3.Normalize(twelfthLowE - nutLowE);

            // Remove any part of the across vector that points along the neck.
            Vector3 across = nutHighE - nutLowE;
            across -= Vector3.Dot(across, g.NeckAxis) * g.NeckAxis;
            g.NutWidth = across.Length();
            g.AcrossAxis = Vector3.Normalize(across);
            g.SurfaceNormal = Vector3.Normalize(Vector3.Cross(g.NeckAxis, g.AcrossAxis));

            g.MeasuredScaleLength = 2f * Vector3.Distance(nutLowE, twelfthLowE);
            g.ScaleLength = SnapScaleLength(g.MeasuredScaleLength, snapTolerance, out bool snapped);
            g.WasSnapped = snapped;
            return g;
        }

        /// <summary>Distance from the nut to fret n (equal temperament).</summary>
        public static float FretDistance(float scaleLength, int fret)
        {
            if (fret <= 0) return 0f;
            return (float)(scaleLength * (1.0 - Math.Pow(2.0, -fret / 12.0)));
        }

        public static float SnapScaleLength(float measured, float tolerance, out bool snapped)
        {
            float best = measured;
            float bestDiff = float.MaxValue;
            foreach (float common in CommonScaleLengths)
            {
                float diff = Math.Abs(common - measured);
                if (diff < bestDiff) { bestDiff = diff; best = common; }
            }
            snapped = bestDiff <= tolerance;
            return snapped ? best : measured;
        }

        /// <summary>Width between the low E and high E strings at a distance from the nut.</summary>
        public float WidthAt(float distanceFromNut)
        {
            float half = ScaleLength / 2f;
            return NutWidth * (1f + Taper * (distanceFromNut / half));
        }

        /// <summary>A point on a string (0 = low E … 5 = high E) at a distance from the nut.</summary>
        public Vector3 StringPoint(int stringIndex, float distanceFromNut)
        {
            float t = stringIndex / 5f;
            return NutLowE
                   + NeckAxis * distanceFromNut
                   + AcrossAxis * (t * WidthAt(distanceFromNut));
        }

        /// <summary>Where a fingertip should press for a string and fret: just behind the fret wire.</summary>
        public Vector3 FingerPosition(int stringIndex, int fret)
        {
            if (fret <= 0) return StringPoint(stringIndex, 0f);
            float before = FretDistance(ScaleLength, fret - 1);
            float at = FretDistance(ScaleLength, fret);
            float d = before + 0.7f * (at - before);   // 70% of the way toward the fret wire
            return StringPoint(stringIndex, d);
        }

        /// <summary>Both ends of fret wire n, from the low E edge to the high E edge.</summary>
        public void FretLine(int fret, out Vector3 lowEnd, out Vector3 highEnd)
        {
            float d = FretDistance(ScaleLength, fret);
            lowEnd = StringPoint(0, d);
            highEnd = StringPoint(5, d);
        }

        /// <summary>Sanity check so we can ask the player to redo calibration.</summary>
        public bool IsPlausible(out string reason)
        {
            if (MeasuredScaleLength < 0.50f || MeasuredScaleLength > 0.72f)
            {
                reason = "The 12th fret seems too near or too far from the nut. Let's try that again.";
                return false;
            }
            if (NutWidth < 0.030f || NutWidth > 0.065f)
            {
                reason = "The nut width looks unusual. Touch the very edges of the nut.";
                return false;
            }
            float alongComponent = Math.Abs(Vector3.Dot(Vector3.Normalize(NutHighE - NutLowE), NeckAxis));
            if (alongComponent > 0.5f)
            {
                reason = "The two nut touches should be across the neck, not along it.";
                return false;
            }
            reason = "";
            return true;
        }
    }
}

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

        private float _snapTolerance = DefaultSnapTolerance;

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
            g._snapTolerance = snapTolerance;

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
            if (!IsFinite(NeckAxis) || !IsFinite(AcrossAxis) || !IsFinite(SurfaceNormal))
            {
                reason = "Two of the touches landed in the same spot. Let's try that again.";
                return false;
            }
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

        // ---------- Helpers used by the overlay, ghost hand, calibration and re-fit ----------

        /// <summary>
        /// A point on the board between strings: stringPosition 0 = low E … 5 = high E (2.5 = middle).
        /// Used for fret markers and labels that sit between strings.
        /// </summary>
        public Vector3 BoardPoint(float stringPosition, float distanceFromNut)
        {
            return NutLowE
                   + NeckAxis * distanceFromNut
                   + AcrossAxis * (stringPosition / 5f * WidthAt(distanceFromNut));
        }

        /// <summary>Middle of the board around the 6th fret: a good "where is the neck" point.</summary>
        public Vector3 Center
        {
            get { return BoardPoint(2.5f, FretDistance(ScaleLength, 6)); }
        }

        /// <summary>
        /// SurfaceNormal flipped, if needed, so it points toward the viewer (the player's head).
        /// The sign of SurfaceNormal depends on handedness, so anything drawn "above the strings"
        /// should use this instead.
        /// </summary>
        public Vector3 NormalToward(Vector3 viewer)
        {
            return Vector3.Dot(SurfaceNormal, viewer - Center) >= 0f ? SurfaceNormal : -SurfaceNormal;
        }

        /// <summary>The same fretboard moved by an offset (keeps angle and size). Used by quick re-fit.</summary>
        public FretboardGeometry MovedBy(Vector3 offset)
        {
            return FromCalibration(NutLowE + offset, NutHighE + offset, TwelfthLowE + offset, _snapTolerance, Taper);
        }

        /// <summary>Fret number whose space contains a distance from the nut (0 = behind the nut).</summary>
        public static int FretAt(float scaleLength, float distanceFromNut)
        {
            if (distanceFromNut <= 0f) return 0;
            double ratio = 1.0 - distanceFromNut / scaleLength;
            if (ratio <= 0.0) return 99;
            return (int)Math.Ceiling(-12.0 * Math.Log(ratio, 2) - 1e-6);
        }

        /// <summary>Where a 3D point sits relative to the board (used for hints and re-fit).</summary>
        public FretboardPoint Locate(Vector3 point)
        {
            Vector3 d = point - NutLowE;
            var p = new FretboardPoint();
            p.Along = Vector3.Dot(d, NeckAxis);
            p.Height = Vector3.Dot(d, SurfaceNormal);
            float acrossMeters = Vector3.Dot(d, AcrossAxis);
            float width = WidthAt(Math.Max(0f, p.Along));
            p.StringPosition = width > 1e-6f ? acrossMeters / width * 5f : 0f;
            p.NearestString = Math.Max(0, Math.Min(5, (int)Math.Round(p.StringPosition)));
            p.Fret = FretAt(ScaleLength, p.Along);
            return p;
        }

        /// <summary>
        /// True if a fingertip is resting at the nut by the low E string (quick re-fit touch).
        /// maxPastNut keeps it clear of a finger pressing fret 1 (its spot is ~2.5 cm from the nut).
        /// </summary>
        public bool IsInRefitZone(Vector3 point, float radius, float maxPastNut)
        {
            if (Vector3.Distance(point, NutLowE) > radius) return false;
            return Vector3.Dot(point - NutLowE, NeckAxis) <= maxPastNut;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) ||
                     float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }
    }

    /// <summary>A point expressed in fretboard terms.</summary>
    public struct FretboardPoint
    {
        /// <summary>Metres from the nut along the neck (negative = toward the headstock).</summary>
        public float Along;
        /// <summary>Metres above the board (along SurfaceNormal; sign depends on handedness).</summary>
        public float Height;
        /// <summary>0 = low E … 5 = high E, fractional between strings.</summary>
        public float StringPosition;
        public int NearestString;
        /// <summary>Fret space the point is over (1 = between nut and fret 1). 0 = behind the nut.</summary>
        public int Fret;
    }
}

using System;
using System.Numerics;
using GhostFret.Core.Fretboard;

namespace GhostFret.Core.AirMode
{
    /// <summary>
    /// The box over the soundhole where an air strum counts.
    /// StrumAxis points from the low E string toward the high E string (roughly toward the floor),
    /// the same convention as StrumDirectionClassifier: moving along +StrumAxis = down-strum.
    /// </summary>
    public sealed class StrumZone
    {
        public Vector3 Center { get; private set; }
        /// <summary>Unit vector, low E → high E. Positive motion = down-strum.</summary>
        public Vector3 StrumAxis { get; private set; }
        /// <summary>Unit vector, nut → body.</summary>
        public Vector3 NeckAxis { get; private set; }
        /// <summary>Unit vector out of the strings, toward the player.</summary>
        public Vector3 TowardPlayer { get; private set; }

        /// <summary>Half the zone length along the neck (metres).</summary>
        public float HalfLength = 0.10f;
        /// <summary>How far above/below the middle of the strings a hand can be and still be tracked for a strum.</summary>
        public float HalfHeight = 0.20f;
        /// <summary>How far in front of the strings (toward the player) a crossing still counts. Air strums float.</summary>
        public float DepthFront = 0.22f;
        /// <summary>How far behind the strings a crossing still counts.</summary>
        public float DepthBehind = 0.08f;

        public StrumZone(Vector3 center, Vector3 strumAxis, Vector3 neckAxis, Vector3 towardPlayer)
        {
            Center = center;
            StrumAxis = Vector3.Normalize(strumAxis);
            NeckAxis = Vector3.Normalize(neckAxis);
            TowardPlayer = Vector3.Normalize(towardPlayer);
        }

        /// <summary>Position relative to the zone centre: along the neck, across the strings, and depth toward the player.</summary>
        public void Project(Vector3 p, out float along, out float across, out float depth)
        {
            Vector3 d = p - Center;
            along = Vector3.Dot(d, NeckAxis);
            across = Vector3.Dot(d, StrumAxis);
            depth = Vector3.Dot(d, TowardPlayer);
        }

        /// <summary>True if a point is close enough to the strings (ignoring the across direction) to strum them.</summary>
        public bool InReach(float along, float depth)
        {
            return Math.Abs(along) <= HalfLength && depth <= DepthFront && depth >= -DepthBehind;
        }
    }

    /// <summary>
    /// A pretend guitar resting at lap height in front of a seated player (DESIGN.md §7.6).
    /// Builds the same three calibration points a real guitar gives, so FretboardOverlay and GhostHand
    /// render on it unchanged. Right-handed: neck to the player's left. Left-handed: mirrored.
    /// Coordinates are Unity world space (y up), carried in System.Numerics vectors.
    /// </summary>
    public sealed class VirtualGuitar
    {
        // Where the soundhole sits relative to the head (metres). Seated lap height, within reach.
        public float ForwardDistance = 0.36f;
        public float BelowHead = 0.50f;
        public float SideOffset = 0.10f;          // toward the strumming hand side

        // Instrument size: an average steel-string acoustic.
        public float ScaleLength = 0.648f;
        public float NutWidth = 0.043f;
        public float StrumDistanceFromNut = 0.46f; // just past the soundhole centre

        // Pose: headstock raised a little, face tilted back so the player can see the strings.
        public float NeckRiseDegrees = 15f;
        public float FaceTiltDegrees = 25f;

        public FretboardGeometry Geometry { get; private set; }
        public StrumZone Zone { get; private set; }
        public bool LeftHanded { get; private set; }

        /// <summary>Centre of the strings at a distance from the nut (midway between low E and high E).</summary>
        public static Vector3 StringCentre(FretboardGeometry g, float distanceFromNut)
        {
            return (g.StringPoint(0, distanceFromNut) + g.StringPoint(5, distanceFromNut)) * 0.5f;
        }

        /// <summary>
        /// Places the guitar for a seated player. headForward may be tilted; only its horizontal part is used.
        /// </summary>
        public void Build(Vector3 headPosition, Vector3 headForward, bool leftHanded)
        {
            LeftHanded = leftHanded;
            Vector3 up = Vector3.UnitY;
            Vector3 fwd = new Vector3(headForward.X, 0f, headForward.Z);
            fwd = fwd.LengthSquared() < 1e-6f ? Vector3.UnitZ : Vector3.Normalize(fwd);
            // Unity convention (left-handed axes): right = up × forward.
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, fwd));
            float side = leftHanded ? -1f : 1f;

            float rise = Deg(NeckRiseDegrees);
            float tilt = Deg(FaceTiltDegrees);

            // Nut → body: toward the strumming side, dipping as the headstock rises.
            Vector3 neck = Vector3.Normalize(right * (side * (float)Math.Cos(rise)) - up * (float)Math.Sin(rise));
            // Low E → high E: down, and toward the player because the face leans back.
            Vector3 acrossWanted = -up * (float)Math.Cos(tilt) - fwd * (float)Math.Sin(tilt);
            Vector3 across = Vector3.Normalize(acrossWanted - Vector3.Dot(acrossWanted, neck) * neck);

            // Build once at the origin to learn where the string centre lands, then shift into place.
            Vector3 strumCentre = headPosition + fwd * ForwardDistance + right * (side * SideOffset) - up * BelowHead;
            FretboardGeometry probe = Make(Vector3.Zero, neck, across);
            Vector3 offset = strumCentre - StringCentre(probe, StrumDistanceFromNut);
            Geometry = Make(offset, neck, across);

            Vector3 toward = Vector3.Normalize(Vector3.Cross(neck, across));
            if (Vector3.Dot(toward, headPosition - strumCentre) < 0f) toward = -toward;
            Zone = new StrumZone(strumCentre, across, neck, toward);
        }

        private FretboardGeometry Make(Vector3 nutLowE, Vector3 neck, Vector3 across)
        {
            return FretboardGeometry.FromCalibration(
                nutLowE,
                nutLowE + across * NutWidth,
                nutLowE + neck * (ScaleLength * 0.5f));
        }

        private static float Deg(float degrees) { return degrees * (float)Math.PI / 180f; }
    }
}

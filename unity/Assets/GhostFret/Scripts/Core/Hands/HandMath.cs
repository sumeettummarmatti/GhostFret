using System;
using System.Collections.Generic;
using System.Numerics;

namespace GhostFret.Core.Hands
{
    /// <summary>
    /// Pure hand maths shared by the device and Editor hand inputs.
    /// Coordinates follow Unity's convention: x right, y up, z forward (a left-handed frame).
    /// Finger numbering: 0 thumb, 1 index, 2 middle, 3 ring, 4 pinky.
    /// </summary>
    public static class HandMath
    {
        public const int FingerCount = 5;

        /// <summary>
        /// Total bend (radians) of a finger's three joints that counts as "fully curled".
        /// A relaxed fist is roughly MCP 80° + PIP 95° + DIP 55° ≈ 230°.
        /// </summary>
        public const float FingerFullCurlRadians = 4.0f;
        /// <summary>The thumb has two bending joints (MCP + IP) and bends less, ≈ 110°.</summary>
        public const float ThumbFullCurlRadians = 1.9f;

        public static float FullCurlRadians(int finger)
        {
            return finger == 0 ? ThumbFullCurlRadians : FingerFullCurlRadians;
        }

        /// <summary>Bend at joint b: angle between segment a→b and segment b→c (0 = straight).</summary>
        public static float BendRadians(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 u = b - a;
            Vector3 v = c - b;
            float lu = u.Length();
            float lv = v.Length();
            if (lu < 1e-6f || lv < 1e-6f) return 0f;
            float cos = Vector3.Dot(u, v) / (lu * lv);
            return (float)Math.Acos(Math.Max(-1f, Math.Min(1f, cos)));
        }

        /// <summary>0 = straight … 1 = fully curled, from a sum of joint bends.</summary>
        public static float CurlFromAngles(IList<float> bendRadians, float fullCurlRadians)
        {
            if (bendRadians == null || fullCurlRadians <= 0f) return 0f;
            float sum = 0f;
            for (int i = 0; i < bendRadians.Count; i++) sum += Math.Abs(bendRadians[i]);
            return Clamp01(sum / fullCurlRadians);
        }

        /// <summary>
        /// 0 = straight … 1 = fully curled, from joint positions ordered from the hand outward
        /// (e.g. metacarpal base, knuckle, middle joint, last joint, tip).
        /// </summary>
        public static float CurlFromJoints(IList<Vector3> chain, float fullCurlRadians)
        {
            if (chain == null || chain.Count < 3 || fullCurlRadians <= 0f) return 0f;
            float sum = 0f;
            for (int i = 1; i < chain.Count - 1; i++) sum += BendRadians(chain[i - 1], chain[i], chain[i + 1]);
            return Clamp01(sum / fullCurlRadians);
        }

        /// <summary>
        /// Unit vector pointing out of the palm, from the wrist and the index and little-finger knuckles
        /// (the far ends of their metacarpals). The two hands are mirror images, so the cross product
        /// order flips between them. Returns Vector3.Zero if the points are degenerate.
        /// </summary>
        public static Vector3 PalmNormal(Vector3 wrist, Vector3 indexKnuckle, Vector3 littleKnuckle, bool isLeftHand)
        {
            Vector3 toIndex = indexKnuckle - wrist;
            Vector3 toLittle = littleKnuckle - wrist;
            Vector3 n = isLeftHand ? Vector3.Cross(toIndex, toLittle) : Vector3.Cross(toLittle, toIndex);
            float len = n.Length();
            return len > 1e-8f ? n / len : Vector3.Zero;
        }

        /// <summary>
        /// A rough fingertip position for a simple procedural hand (Editor input, previews): the tip moves
        /// from straight out toward the palm as the finger curls.
        /// </summary>
        public static Vector3 ProceduralTip(Vector3 knuckle, Vector3 fingerForward, Vector3 palmNormal, float length, float curl)
        {
            curl = Clamp01(curl);
            // At full curl the tip folds back under the knuckle, a little into the palm.
            double angle = curl * Math.PI * 0.85;
            Vector3 dir = fingerForward * (float)Math.Cos(angle) + palmNormal * (float)Math.Sin(angle);
            float reach = length * (1f - 0.45f * curl);
            return knuckle + dir * reach;
        }

        private static float Clamp01(float x) { return x < 0f ? 0f : x > 1f ? 1f : x; }
    }
}

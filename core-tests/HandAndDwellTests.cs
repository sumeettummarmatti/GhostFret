using System;
using System.Numerics;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Hands;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class DwellDetectorTests
    {
        private const float Dt = 1f / 72f;   // Quest 3 hand tracking rate

        private static DwellDetector Armed()
        {
            var d = new DwellDetector();
            d.Reset(false);
            return d;
        }

        /// <summary>Feeds frames; returns the time (s) at which the hold completed, or −1.</summary>
        private static float Run(DwellDetector d, Func<int, Vector3> point, float seconds, Func<int, bool> tracked = null)
        {
            int frames = (int)(seconds / Dt);
            for (int i = 0; i < frames; i++)
            {
                bool t = tracked == null || tracked(i);
                if (d.Update(point(i), t, Dt)) return (i + 1) * Dt;
            }
            return -1f;
        }

        [Fact]
        public void A_still_fingertip_completes_after_about_one_second()
        {
            var d = Armed();
            var p = new Vector3(0.1f, 0.9f, 0.3f);
            float t = Run(d, i => p, 2f);
            Assert.InRange(t, 0.98f, 1.05f);
            Assert.True(Vector3.Distance(d.Result, p) < 1e-5f);
        }

        [Fact]
        public void Progress_rises_while_holding()
        {
            var d = Armed();
            var p = Vector3.Zero;
            Run(d, i => p, 0.5f);
            Assert.InRange(d.Progress, 0.45f, 0.55f);
        }

        [Fact]
        public void Tracking_jitter_of_a_few_millimetres_still_completes_and_averages_out()
        {
            var d = Armed();
            var rng = new Random(11);
            var p = new Vector3(0f, 1f, 0.4f);
            Func<int, Vector3> jittery = i => p + new Vector3(
                (float)(rng.NextDouble() * 2 - 1) * 0.0025f,
                (float)(rng.NextDouble() * 2 - 1) * 0.0025f,
                (float)(rng.NextDouble() * 2 - 1) * 0.0025f);
            float t = Run(d, jittery, 3f);
            Assert.True(t > 0f && t < 1.6f, "completed at " + t);
            Assert.True(Vector3.Distance(d.Result, p) < 0.0015f);
        }

        [Fact]
        public void Moving_the_finger_restarts_the_hold()
        {
            var d = Armed();
            // Still for 0.6 s, then a 2 cm jump, then still.
            float t = Run(d, i => i * Dt < 0.6f ? Vector3.Zero : new Vector3(0.02f, 0, 0), 3f);
            Assert.True(t > 1.55f, "completed at " + t);
            Assert.True(Vector3.Distance(d.Result, new Vector3(0.02f, 0, 0)) < 0.003f);
        }

        [Fact]
        public void A_resting_hand_never_triggers_until_it_moves()
        {
            var d = new DwellDetector();   // requires travel by default
            Assert.Equal(-1f, Run(d, i => Vector3.Zero, 3f));
            Assert.False(d.IsArmed);

            // Travel 5 cm over 0.25 s, then rest: now it counts.
            float t = Run(d, i => i * Dt < 0.25f ? new Vector3(0.2f * i * Dt, 0, 0) : new Vector3(0.05f, 0, 0), 3f);
            Assert.True(t > 1.0f && t < 1.6f, "completed at " + t);
        }

        [Fact]
        public void After_a_touch_the_same_resting_finger_does_not_trigger_again()
        {
            var d = Armed();
            Assert.True(Run(d, i => Vector3.Zero, 1.5f) > 0f);
            Assert.Equal(-1f, Run(d, i => Vector3.Zero, 3f));
        }

        [Fact]
        public void A_short_tracking_dropout_pauses_but_a_long_one_resets()
        {
            var shortLoss = Armed();
            // Lost for ~0.1 s in the middle: still completes close to 1 s of tracked time.
            float t1 = Run(shortLoss, i => Vector3.Zero, 2f, i => !(i >= 30 && i < 37));
            Assert.InRange(t1, 1.0f, 1.2f);

            var longLoss = Armed();
            float t2 = Run(longLoss, i => Vector3.Zero, 3f, i => !(i >= 30 && i < 80));
            Assert.True(t2 > 1.9f, "completed at " + t2);
        }
    }

    public class HandMathTests
    {
        [Fact]
        public void A_straight_finger_has_no_curl()
        {
            var chain = new[] { Vector3.Zero, new Vector3(0, 0, 0.04f), new Vector3(0, 0, 0.08f), new Vector3(0, 0, 0.10f), new Vector3(0, 0, 0.12f) };
            Assert.Equal(0f, HandMath.CurlFromJoints(chain, HandMath.FingerFullCurlRadians), 4);
        }

        [Fact]
        public void A_fist_is_fully_curled_and_a_half_bend_is_in_between()
        {
            // Bend each of the three joints by 80° in the same plane.
            Assert.Equal(1f, HandMath.CurlFromJoints(Chain(80f), HandMath.FingerFullCurlRadians), 3);
            float half = HandMath.CurlFromJoints(Chain(38f), HandMath.FingerFullCurlRadians);
            Assert.InRange(half, 0.45f, 0.55f);
        }

        [Fact]
        public void Curl_from_angles_sums_joints_and_clamps()
        {
            float r = (float)(Math.PI / 180.0);
            Assert.Equal(0.5f, HandMath.CurlFromAngles(new[] { 30f * r, 40f * r, 40f * r }, 220f * r), 3);
            Assert.Equal(1f, HandMath.CurlFromAngles(new[] { 3f, 3f }, HandMath.ThumbFullCurlRadians), 3);
            Assert.True(HandMath.FullCurlRadians(0) < HandMath.FullCurlRadians(1));
        }

        [Fact]
        public void Bend_is_zero_for_degenerate_segments()
        {
            Assert.Equal(0f, HandMath.BendRadians(Vector3.Zero, Vector3.Zero, Vector3.One));
        }

        // Unity axes: x right, y up, z forward. Fingers point forward.
        // Right hand palm down: thumb/index side is on the left (−x). Left hand palm down: index on the right (+x).
        [Theory]
        [InlineData(false, -1f, -1f)]  // right hand, palm down
        [InlineData(false, 1f, 1f)]    // right hand flipped over (index now on +x): palm up
        [InlineData(true, 1f, -1f)]    // left hand, palm down
        [InlineData(true, -1f, 1f)]    // left hand flipped over: palm up
        public void Palm_normal_is_correct_for_each_hand(bool isLeft, float indexSideX, float expectedY)
        {
            var wrist = Vector3.Zero;
            var index = new Vector3(0.03f * indexSideX, 0, 0.085f);
            var little = new Vector3(-0.03f * indexSideX, 0, 0.075f);
            Vector3 n = HandMath.PalmNormal(wrist, index, little, isLeft);
            Assert.Equal(expectedY, n.Y, 3);
            Assert.Equal(1f, n.Length(), 3);
        }

        [Fact]
        public void Palm_normal_of_degenerate_points_is_zero()
        {
            Assert.Equal(Vector3.Zero, HandMath.PalmNormal(Vector3.Zero, Vector3.Zero, Vector3.Zero, true));
        }

        [Fact]
        public void Procedural_tip_folds_toward_the_palm_when_curled()
        {
            var knuckle = Vector3.Zero;
            var fwd = new Vector3(0, 0, 1);
            var palm = new Vector3(0, -1, 0);
            Vector3 straight = HandMath.ProceduralTip(knuckle, fwd, palm, 0.08f, 0f);
            Vector3 curled = HandMath.ProceduralTip(knuckle, fwd, palm, 0.08f, 1f);
            Assert.True(Vector3.Distance(straight, new Vector3(0, 0, 0.08f)) < 1e-5f);
            Assert.True(curled.Y < -0.01f);
            Assert.True(curled.Z < straight.Z);
        }

        private static Vector3[] Chain(float bendDegrees)
        {
            // Segments of 4, 4, 2.5, 2 cm; each joint turns the finger downward by bendDegrees.
            float[] lengths = { 0.04f, 0.04f, 0.025f, 0.02f };
            var pts = new Vector3[5];
            pts[0] = Vector3.Zero;
            double angle = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) angle += bendDegrees * Math.PI / 180.0;
                var dir = new Vector3(0, -(float)Math.Sin(angle), (float)Math.Cos(angle));
                pts[i + 1] = pts[i] + dir * lengths[i];
            }
            return pts;
        }
    }
}

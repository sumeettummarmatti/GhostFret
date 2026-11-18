using System;
using System.Numerics;
using GhostFret.Core.Fretboard;
using Xunit;

namespace GhostFret.Core.Tests
{
    /// <summary>
    /// Calibration from realistic seated touches (Unity axes: x right, y up, z forward; head at y≈1.2).
    /// </summary>
    public class FretboardCalibrationTests
    {
        private static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);

        /// <summary>A guitar on the lap: neck toward the player's right (left if left-handed), tilted back a little.</summary>
        private static void SeatedGuitar(bool leftHanded, out Vector3 nutLow, out Vector3 neck, out Vector3 across)
        {
            float side = leftHanded ? -1f : 1f;
            nutLow = new Vector3(-0.40f * side, 0.86f, 0.33f);
            neck = Vector3.Normalize(new Vector3(1f * side, -0.15f, 0.06f));
            Vector3 face = Vector3.Normalize(new Vector3(0f, 0.3f, -0.95f));   // board faces the player, a bit up
            across = Vector3.Normalize(Vector3.Cross(neck, face));
            if (across.Y > 0f) across = -across;                               // low E on top → high E is down
        }

        private static FretboardGeometry FitWithNoise(bool leftHanded, float halfScale, int seed, float noise = 0.003f)
        {
            Vector3 nutLow, neck, across;
            SeatedGuitar(leftHanded, out nutLow, out neck, out across);
            var rng = new Random(seed);
            Func<Vector3> jitter = () => new Vector3(
                (float)(rng.NextDouble() * 2 - 1) * noise,
                (float)(rng.NextDouble() * 2 - 1) * noise,
                (float)(rng.NextDouble() * 2 - 1) * noise);
            return FretboardGeometry.FromCalibration(
                nutLow + jitter(),
                nutLow + across * 0.043f + jitter(),
                nutLow + neck * halfScale + jitter());
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(false, 2)]
        [InlineData(true, 3)]
        [InlineData(true, 4)]
        public void Realistic_noisy_touches_fit_a_plausible_snapped_fretboard(bool leftHanded, int seed)
        {
            var g = FitWithNoise(leftHanded, 0.324f, seed);
            string reason;
            Assert.True(g.IsPlausible(out reason), reason);
            Assert.True(g.WasSnapped);
            // 648 mm and 650 mm are 2 mm apart, so touch noise may pick either: both are right.
            Assert.InRange(g.ScaleLength, 0.6479f, 0.6501f);
            Assert.InRange(g.NutWidth, 0.035f, 0.05f);

            Vector3 nutLow, neck, across;
            SeatedGuitar(leftHanded, out nutLow, out neck, out across);
            Assert.True(Vector3.Dot(g.NeckAxis, neck) > 0.99f);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Normal_toward_viewer_points_at_the_player_for_both_hands(bool leftHanded)
        {
            var g = FitWithNoise(leftHanded, 0.324f, 5);
            Vector3 n = g.NormalToward(Head);
            Assert.True(Vector3.Dot(n, Head - g.Center) > 0f);
            Assert.Equal(1f, n.Length(), 3);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Locate_finds_string_and_fret_of_every_finger_spot(bool leftHanded)
        {
            var g = FitWithNoise(leftHanded, 0.324f, 6);
            for (int s = 0; s < 6; s++)
            {
                for (int f = 1; f <= 12; f++)
                {
                    FretboardPoint p = g.Locate(g.FingerPosition(s, f));
                    Assert.Equal(s, p.NearestString);
                    Assert.Equal(f, p.Fret);
                    Assert.True(Math.Abs(p.Height) < 1e-4f);
                }
            }
        }

        [Fact]
        public void Fret_at_inverts_fret_distance()
        {
            Assert.Equal(0, FretboardGeometry.FretAt(0.648f, -0.01f));
            for (int f = 1; f <= 20; f++)
            {
                float mid = 0.5f * (FretboardGeometry.FretDistance(0.648f, f - 1) + FretboardGeometry.FretDistance(0.648f, f));
                Assert.Equal(f, FretboardGeometry.FretAt(0.648f, mid));
            }
        }

        [Fact]
        public void Rejects_a_twelfth_fret_touch_that_is_too_close()
        {
            var g = FitWithNoise(false, 0.20f, 7);
            string reason;
            Assert.False(g.IsPlausible(out reason));
            Assert.Contains("12th fret", reason);
        }

        [Fact]
        public void Rejects_a_narrow_nut()
        {
            var g = FretboardGeometry.FromCalibration(Vector3.Zero, new Vector3(0, -0.015f, 0), new Vector3(0.324f, 0, 0));
            string reason;
            Assert.False(g.IsPlausible(out reason));
            Assert.Contains("nut width", reason);
        }

        [Fact]
        public void Rejects_nut_touches_along_the_neck()
        {
            // Second "nut" touch is 4 cm along the neck and only 3.2 cm across.
            var g = FretboardGeometry.FromCalibration(Vector3.Zero, new Vector3(0.04f, -0.032f, 0), new Vector3(0.324f, 0, 0));
            string reason;
            Assert.False(g.IsPlausible(out reason));
            Assert.Contains("across the neck", reason);
        }

        [Fact]
        public void Rejects_two_touches_in_the_same_spot_without_crashing()
        {
            var g = FretboardGeometry.FromCalibration(Vector3.Zero, Vector3.Zero, new Vector3(0.324f, 0, 0));
            string reason;
            Assert.False(g.IsPlausible(out reason));
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Theory]
        [InlineData(0.624f, 0.628f, true)]    // 24.75" Gibson-style
        [InlineData(0.655f, 0.650f, true)]    // classical 650 mm
        [InlineData(0.700f, 0.700f, false)]   // too far from any common scale: keep measured
        public void Scale_length_snaps_only_when_close(float measured, float expected, bool snapped)
        {
            bool did;
            float result = FretboardGeometry.SnapScaleLength(measured, FretboardGeometry.DefaultSnapTolerance, out did);
            Assert.Equal(snapped, did);
            Assert.Equal(expected, result, 3);
        }

        [Fact]
        public void Moved_by_keeps_angle_and_scale()
        {
            var g = FitWithNoise(false, 0.324f, 8);
            var offset = new Vector3(0.01f, -0.02f, 0.005f);
            var m = g.MovedBy(offset);
            Assert.True(Vector3.Distance(m.NutLowE, g.NutLowE + offset) < 1e-5f);
            Assert.True(Vector3.Dot(m.NeckAxis, g.NeckAxis) > 0.99999f);
            Assert.True(Vector3.Dot(m.SurfaceNormal, g.SurfaceNormal) > 0.99999f);
            Assert.Equal(g.ScaleLength, m.ScaleLength, 5);
        }

        [Fact]
        public void Refit_zone_is_the_nut_but_not_the_first_fret_finger_spot()
        {
            var g = FitWithNoise(false, 0.324f, 9);
            Assert.True(g.IsInRefitZone(g.NutLowE + g.NeckAxis * 0.005f, 0.04f, 0.012f));
            Assert.True(g.IsInRefitZone(g.NutLowE - g.NeckAxis * 0.02f, 0.04f, 0.012f));   // guitar slid toward the headstock
            Assert.False(g.IsInRefitZone(g.FingerPosition(0, 1), 0.04f, 0.012f));
            Assert.False(g.IsInRefitZone(g.NutLowE + g.AcrossAxis * 0.06f, 0.04f, 0.012f));
        }

        // ---------- CalibrationGuide ----------

        [Fact]
        public void Predicts_the_high_E_nut_below_the_first_touch()
        {
            var p = CalibrationGuide.PredictNutHighE(new Vector3(0, 1, 0), new Vector3(0, -1, 0));
            Assert.Equal(1f - CalibrationGuide.TypicalNutWidth, p.Y, 4);
        }

        [Theory]
        [InlineData(false, 1f)]
        [InlineData(true, -1f)]
        public void Predicts_the_twelfth_fret_toward_the_body_side(bool leftHanded, float expectedSign)
        {
            Vector3 nutLow = new Vector3(0, 0.9f, 0.3f);
            Vector3 nutHigh = nutLow + new Vector3(0.01f, -0.042f, -0.005f);   // slightly skewed touch
            Vector3 p = CalibrationGuide.PredictTwelfth(nutLow, nutHigh, new Vector3(1, 0, 0), leftHanded);
            Vector3 dir = p - nutLow;
            Assert.Equal(CalibrationGuide.TypicalHalfScale, dir.Length(), 4);
            Assert.True(Math.Sign(dir.X) == (int)expectedSign);
            // At right angles to the nut.
            Assert.True(Math.Abs(Vector3.Dot(Vector3.Normalize(dir), Vector3.Normalize(nutHigh - nutLow))) < 1e-4f);
        }

        [Fact]
        public void Check_touch_gives_kind_tips_for_misplaced_touches()
        {
            Vector3 nutLow = Vector3.Zero;
            Vector3 nutHigh = new Vector3(0, -0.043f, 0);
            string tip;

            Assert.False(CalibrationGuide.CheckTouch(1, new Vector3(0, -0.01f, 0), nutLow, Vector3.Zero, out tip));
            Assert.Contains("other end of the nut", tip);
            Assert.False(CalibrationGuide.CheckTouch(1, new Vector3(0, -0.12f, 0), nutLow, Vector3.Zero, out tip));
            Assert.True(CalibrationGuide.CheckTouch(1, nutHigh, nutLow, Vector3.Zero, out tip));

            Assert.False(CalibrationGuide.CheckTouch(2, new Vector3(0.10f, 0, 0), nutLow, nutHigh, out tip));
            Assert.Contains("further along", tip);
            Assert.False(CalibrationGuide.CheckTouch(2, new Vector3(0.50f, 0, 0), nutLow, nutHigh, out tip));
            Assert.Contains("past the 12th", tip);
            Assert.False(CalibrationGuide.CheckTouch(2, new Vector3(0.324f, -0.05f, 0), nutLow, nutHigh, out tip));
            Assert.Contains("thickest string", tip);
            Assert.True(CalibrationGuide.CheckTouch(2, new Vector3(0.324f, 0.002f, 0), nutLow, nutHigh, out tip));
            Assert.Equal("", tip);
        }
    }
}

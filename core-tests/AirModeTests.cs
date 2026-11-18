using System;
using System.Collections.Generic;
using System.Numerics;
using GhostFret.Core.AirMode;
using GhostFret.Core.Audio;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Music;
using GhostFret.Core.Strum;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class AirVirtualGuitarTests
    {
        private static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);
        private static readonly Vector3 Forward = new Vector3(0f, 0f, 1f); // Unity: +z forward, +x right

        private static VirtualGuitar Build(bool leftHanded, Vector3? forward = null)
        {
            var v = new VirtualGuitar();
            v.Build(Head, forward ?? Forward, leftHanded);
            return v;
        }

        [Fact]
        public void Right_handed_guitar_is_plausible_and_at_lap_height()
        {
            var v = Build(false);
            string reason;
            Assert.True(v.Geometry.IsPlausible(out reason), reason);
            Assert.True(v.Geometry.WasSnapped);
            Assert.Equal(0.648f, v.Geometry.ScaleLength, 3);

            Vector3 c = v.Zone.Center;
            float below = Head.Y - c.Y;
            Assert.InRange(below, 0.35f, 0.65f);
            Assert.InRange(c.Z, 0.2f, 0.5f);
            Assert.InRange(Vector3.Distance(c, VirtualGuitar.StringCentre(v.Geometry, v.StrumDistanceFromNut)), 0f, 1e-4f);
        }

        [Fact]
        public void Right_handed_neck_points_left_with_low_E_on_top()
        {
            var v = Build(false);
            var g = v.Geometry;
            Assert.True(g.NutLowE.X < v.Zone.Center.X - 0.3f, "nut should be well to the player's left");
            Assert.True(g.NeckAxis.X > 0.8f, "nut → body runs to the right");
            Assert.True(g.AcrossAxis.Y < -0.7f, "low E → high E runs toward the floor");
            Assert.True(g.NutLowE.Y > v.Zone.Center.Y, "headstock is raised a little");
            Assert.True(v.Zone.TowardPlayer.Z < -0.5f, "strings face the player");
            Assert.True(v.Zone.TowardPlayer.Y > 0.1f, "face leans back so the strings are visible");
        }

        [Fact]
        public void Left_handed_guitar_is_a_mirror_image()
        {
            var r = Build(false).Geometry;
            var l = Build(true).Geometry;
            Assert.True(l.NeckAxis.X < -0.8f);
            AssertMirrored(r.NutLowE, l.NutLowE);
            AssertMirrored(r.NutHighE, l.NutHighE);
            AssertMirrored(r.TwelfthLowE, l.TwelfthLowE);
            AssertMirrored(r.FingerPosition(1, 2), l.FingerPosition(1, 2));
            string reason;
            Assert.True(l.IsPlausible(out reason), reason);
            Assert.True(Build(true).Zone.TowardPlayer.Z < -0.5f);
        }

        [Fact]
        public void Guitar_follows_head_yaw_and_stays_in_reach()
        {
            var v = Build(false, new Vector3(1f, -0.6f, 0f)); // looking right and down at the lap
            Assert.True(v.Zone.Center.X > 0.2f);
            Assert.True(v.Geometry.NeckAxis.Z < -0.8f, "right of a player facing +x is −z");
            foreach (var shape in new[] { "Em", "D", "G", "C", "Am" })
            {
                var chord = ChordLibrary.Get(shape);
                for (int s = 0; s < 6; s++)
                    if (chord.frets[s] > 0)
                        Assert.InRange(Vector3.Distance(Head, v.Geometry.FingerPosition(s, chord.frets[s])), 0.3f, 0.8f);
            }
        }

        private static void AssertMirrored(Vector3 a, Vector3 b)
        {
            Assert.Equal(a.X, -b.X, 4);
            Assert.Equal(a.Y, b.Y, 4);
            Assert.Equal(a.Z, b.Z, 4);
        }
    }

    public class AirStrumDetectorTests
    {
        private static StrumZone Zone()
        {
            // Strings face the player (−z), low E → high E points down.
            return new StrumZone(new Vector3(0, 0.7f, 0.36f), new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1));
        }

        // Position whose 'across' coordinate is a, in front of the strings by 5 cm.
        private static Vector3 At(StrumZone z, float across, float along = 0f)
        {
            return z.Center + z.StrumAxis * across + z.NeckAxis * along + z.TowardPlayer * 0.05f;
        }

        private static List<AirStrum> Run(StrumZone z, IEnumerable<KeyValuePair<double, Vector3>> path)
        {
            var d = new AirStrumDetector(z);
            var strums = new List<AirStrum>();
            foreach (var p in path)
            {
                AirStrum s;
                if (d.AddSample(p.Key, p.Value, out s)) strums.Add(s);
            }
            return strums;
        }

        // Straight move from a0 to a1 over 'seconds', sampled at 90 Hz, starting at t0.
        private static IEnumerable<KeyValuePair<double, Vector3>> Sweep(StrumZone z, double t0, float a0, float a1, double seconds, float along = 0f)
        {
            int n = (int)Math.Ceiling(seconds * 90);
            for (int i = 0; i <= n; i++)
            {
                double t = t0 + i / 90.0;
                float a = a0 + (a1 - a0) * (float)(i / (double)n);
                yield return new KeyValuePair<double, Vector3>(t, At(z, a, along));
            }
        }

        [Fact]
        public void Fast_move_toward_high_E_is_one_down_strum()
        {
            var z = Zone();
            var strums = Run(z, Sweep(z, 10.0, -0.10f, 0.10f, 0.15));
            Assert.Single(strums);
            Assert.Equal(StrumDirection.Down, strums[0].Direction);
            Assert.InRange(strums[0].Time, 10.07, 10.08);   // crossing is halfway through the sweep
            Assert.True(strums[0].Speed > 1f);
        }

        [Fact]
        public void Fast_move_toward_low_E_is_one_up_strum()
        {
            var z = Zone();
            var strums = Run(z, Sweep(z, 0.0, 0.10f, -0.10f, 0.15));
            Assert.Single(strums);
            Assert.Equal(StrumDirection.Up, strums[0].Direction);
        }

        [Fact]
        public void Slow_drift_across_the_strings_is_not_a_strum()
        {
            var z = Zone();
            Assert.Empty(Run(z, Sweep(z, 0.0, -0.10f, 0.10f, 4.0)));
        }

        [Fact]
        public void Jitter_after_a_strum_does_not_double_strum()
        {
            var z = Zone();
            var path = new List<KeyValuePair<double, Vector3>>(Sweep(z, 0.0, -0.10f, 0.0f, 0.075));
            double t = 0.075;
            for (int i = 1; i <= 60; i++)   // hover on the strings with fast ±6 mm tracking noise
            {
                float a = (i % 2 == 0 ? 1f : -1f) * 0.006f;
                path.Add(new KeyValuePair<double, Vector3>(t + i / 90.0, At(z, a)));
            }
            var strums = Run(z, path);
            Assert.Single(strums);
            Assert.Equal(StrumDirection.Down, strums[0].Direction);
        }

        [Fact]
        public void Down_up_down_gives_three_alternating_strums()
        {
            var z = Zone();
            var path = new List<KeyValuePair<double, Vector3>>();
            path.AddRange(Sweep(z, 0.0, -0.10f, 0.10f, 0.15));
            path.AddRange(Sweep(z, 0.30, 0.10f, -0.10f, 0.15));
            path.AddRange(Sweep(z, 0.60, -0.10f, 0.10f, 0.15));
            var strums = Run(z, path);
            Assert.Equal(3, strums.Count);
            Assert.Equal(StrumDirection.Down, strums[0].Direction);
            Assert.Equal(StrumDirection.Up, strums[1].Direction);
            Assert.Equal(StrumDirection.Down, strums[2].Direction);
        }

        [Fact]
        public void Strum_far_along_the_neck_does_not_count()
        {
            var z = Zone();
            Assert.Empty(Run(z, Sweep(z, 0.0, -0.10f, 0.10f, 0.15, along: -0.35f)));
        }
    }

    public class AirChordMatcherTests
    {
        private static FretboardGeometry Geometry()
        {
            var v = new VirtualGuitar();
            v.Build(new Vector3(0, 1.2f, 0), new Vector3(0, 0, 1), false);
            return v.Geometry;
        }

        // A pose that copies the chord: used fingers curled and on their spots, others relaxed.
        private static void IdealPose(ChordShape c, FretboardGeometry g, out float[] curl, out Vector3[] tips)
        {
            curl = new[] { 0.3f, 0.15f, 0.15f, 0.15f, 0.15f };
            Vector3 rest = g.FingerPosition(2, 4) + g.SurfaceNormal * 0.05f;
            tips = new[] { rest, rest, rest, rest, rest };
            for (int s = 0; s < 6; s++)
            {
                int f = c.fingers[s];
                if (f < 1 || c.frets[s] <= 0) continue;
                curl[f] = 0.7f;
                tips[f] = g.FingerPosition(s, c.frets[s]) + g.SurfaceNormal * 0.01f;
            }
        }

        [Theory]
        [InlineData("Em")]
        [InlineData("D")]
        [InlineData("G")]
        [InlineData("C")]
        [InlineData("Am")]
        public void Copying_the_ghost_hand_looks_right(string name)
        {
            var g = Geometry();
            var chord = ChordLibrary.Get(name);
            float[] curl; Vector3[] tips;
            IdealPose(chord, g, out curl, out tips);
            var m = new AirChordMatcher();
            var withTips = m.Match(chord, curl, tips, g, 1f);
            var curlOnly = m.Match(chord, curl, null, null, 1f);
            Assert.True(withTips.LooksRight, name + " " + withTips.Confidence);
            Assert.True(withTips.Confidence > 0.9f);
            Assert.True(curlOnly.LooksRight);
            Assert.Equal("", withTips.Hint);
        }

        [Fact]
        public void Flat_hand_gets_a_gentle_curl_hint_for_Em()
        {
            var m = new AirChordMatcher();
            var r = m.Match(ChordLibrary.Get("Em"), new[] { 0.2f, 0.05f, 0.05f, 0.05f, 0.05f }, null, null, 1f);
            Assert.False(r.LooksRight);
            Assert.True(r.FocusFinger == 2 || r.FocusFinger == 3);
            Assert.StartsWith("Curl your", r.Hint);
            Assert.DoesNotContain("wrong", r.Hint.ToLowerInvariant());
        }

        [Fact]
        public void G_pose_scores_lower_than_D_pose_for_a_D_target()
        {
            var g = Geometry();
            var m = new AirChordMatcher();
            float[] curl; Vector3[] tips;
            IdealPose(ChordLibrary.Get("G"), g, out curl, out tips);
            var asG = m.Match(ChordLibrary.Get("D"), curl, tips, g, 1f);
            IdealPose(ChordLibrary.Get("D"), g, out curl, out tips);
            var asD = m.Match(ChordLibrary.Get("D"), curl, tips, g, 1f);
            Assert.True(asG.Confidence < asD.Confidence - 0.3f);
            Assert.False(asG.LooksRight);
            Assert.True(asG.Hint.Length > 0);
        }

        [Fact]
        public void Misplaced_finger_hint_names_the_target_spot()
        {
            var g = Geometry();
            var am = ChordLibrary.Get("Am");
            float[] curl; Vector3[] tips;
            IdealPose(am, g, out curl, out tips);
            tips[3] = g.FingerPosition(0, 5);               // ring finger wandered to low E, fret 5
            tips[2] = g.FingerPosition(0, 4);               // middle too
            var r = new AirChordMatcher().Match(am, curl, tips, g, 1f);
            Assert.False(r.LooksRight);
            Assert.Contains("string, fret", r.Hint);
        }

        [Fact]
        public void Hand_far_from_the_neck_gets_a_general_tip()
        {
            var g = Geometry();
            var em = ChordLibrary.Get("Em");
            float[] curl; Vector3[] tips;
            IdealPose(em, g, out curl, out tips);
            for (int i = 0; i < 5; i++) tips[i] += new Vector3(0, 0.4f, 0);
            var r = new AirChordMatcher().Match(em, curl, tips, g, 1f);
            Assert.False(r.LooksRight);
            Assert.Equal("Bring your hand to the ghost fingers.", r.Hint);
        }

        [Fact]
        public void Low_tracking_confidence_is_generous_and_quiet()
        {
            var r = new AirChordMatcher().Match(ChordLibrary.Get("C"), new float[5], null, null, 0.2f);
            Assert.True(r.LooksRight);
            Assert.False(r.Judged);
            Assert.Equal("", r.Hint);
        }
    }

    public class AirPluckSynthTests
    {
        [Fact]
        public void Chord_has_the_right_length_and_is_clean()
        {
            var synth = new PluckSynth();
            foreach (var name in new[] { "Em", "D", "G", "C", "Am" })
            foreach (var dir in new[] { StrumDirection.Down, StrumDirection.Up })
            {
                float[] pcm = synth.RenderChord(ChordLibrary.Get(name), dir);
                Assert.Equal(96000, pcm.Length);
                float peak = 0f;
                foreach (float v in pcm)
                {
                    Assert.False(float.IsNaN(v) || float.IsInfinity(v));
                    peak = Math.Max(peak, Math.Abs(v));
                }
                Assert.True(peak <= 0.851f, name + " peak " + peak);
                Assert.True(peak > 0.1f, name + " is too quiet");
            }
        }

        [Fact]
        public void Down_strum_starts_on_the_lowest_sounding_string()
        {
            var synth = new PluckSynth();
            // D skips the low E and A strings: the first sounding string (D) starts right away.
            float[] d = synth.RenderChord(ChordLibrary.Get("D"), StrumDirection.Down);
            Assert.True(Rms(d, 0, 480) > 0.01f);
            // Up and down strums of the same chord sound different (string order).
            float[] up = synth.RenderChord(ChordLibrary.Get("D"), StrumDirection.Up);
            Assert.NotEqual(d[1000], up[1000]);
        }

        [Fact]
        public void Fully_muted_shape_is_silent()
        {
            var muted = new ChordShape("x", new[] { -1, -1, -1, -1, -1, -1 }, new int[6]);
            float[] pcm = new PluckSynth().RenderChord(muted, StrumDirection.Down);
            Assert.Equal(0f, Rms(pcm, 0, pcm.Length));
        }

        [Theory]
        [InlineData(82.41)]   // low E
        [InlineData(110.0)]   // A
        [InlineData(196.0)]   // G
        [InlineData(329.63)]  // high E
        public void Single_string_sounds_at_its_pitch(double hz)
        {
            float[] pcm = new PluckSynth().RenderString(hz);
            var window = new float[4096];
            Array.Copy(pcm, 4800, window, 0, window.Length);
            var p = new YinPitchDetector().Detect(window, 48000);
            Assert.True(p.HasPitch);
            Assert.InRange(NoteMath.CentsBetween(p.Frequency, hz), -10.0, 10.0);
        }

        private static float Rms(float[] x, int start, int count)
        {
            double sum = 0;
            for (int i = start; i < start + count; i++) sum += x[i] * x[i];
            return (float)Math.Sqrt(sum / Math.Max(1, count));
        }
    }
}

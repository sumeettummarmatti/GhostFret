using System;
using System.Numerics;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Music;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class MusicAndFretboardTests
    {
        [Fact]
        public void A4_is_440Hz_and_low_E_is_about_82Hz()
        {
            Assert.Equal(440.0, NoteMath.MidiToFrequency(69), 6);
            Assert.Equal(82.41, GuitarTuning.Standard.OpenStringFrequency(0), 2);
        }

        [Fact]
        public void Nearest_string_finds_a_slightly_flat_G()
        {
            int s = GuitarTuning.Standard.NearestString(194.0, out double cents);
            Assert.Equal(3, s);
            Assert.True(cents < -10 && cents > -30);
        }

        [Fact]
        public void Twelfth_fret_is_half_the_scale_length()
        {
            Assert.Equal(0.324f, FretboardGeometry.FretDistance(0.648f, 12), 4);
        }

        [Fact]
        public void Calibration_snaps_to_a_common_scale_and_is_plausible()
        {
            var nutLow = new Vector3(0, 0, 0);
            var nutHigh = new Vector3(0, 0, 0.043f);
            var twelfth = new Vector3(0.3225f, 0, 0);   // measured 0.645 m
            var g = FretboardGeometry.FromCalibration(nutLow, nutHigh, twelfth);
            Assert.True(g.WasSnapped);
            Assert.Equal(0.648f, g.ScaleLength, 3);
            Assert.True(g.IsPlausible(out _));
        }

        [Fact]
        public void Finger_position_sits_between_the_frets()
        {
            var g = FretboardGeometry.FromCalibration(Vector3.Zero, new Vector3(0, 0, 0.043f), new Vector3(0.324f, 0, 0));
            float x = g.FingerPosition(0, 2).X;
            Assert.True(x > FretboardGeometry.FretDistance(g.ScaleLength, 1));
            Assert.True(x < FretboardGeometry.FretDistance(g.ScaleLength, 2));
        }
    }
}

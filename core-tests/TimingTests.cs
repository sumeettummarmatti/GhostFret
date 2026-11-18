using System.Collections.Generic;
using GhostFret.Core.Timing;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class TimingTests
    {
        private static readonly List<double> Clicks = new List<double> { 1.0, 1.5, 2.0, 2.5 };

        [Fact]
        public void Latency_is_the_median_offset()
        {
            var strums = new List<double> { 1.12, 1.61, 2.13, 2.62 };   // 120, 110, 130, 120 ms late
            var e = LatencyCalibration.Estimate(Clicks, strums);
            Assert.True(e.Ok);
            Assert.Equal(4, e.Pairs);
            Assert.Equal(120, e.LatencyMs, 3);
        }

        [Fact]
        public void One_stray_strum_does_not_skew_it()
        {
            var strums = new List<double> { 1.10, 1.60, 2.10, 2.40 + 0.0 };  // last one 100 ms early
            var e = LatencyCalibration.Estimate(Clicks, strums);
            Assert.True(e.Ok);
            Assert.Equal(100, e.LatencyMs, 3);
        }

        [Fact]
        public void Too_few_strums_is_not_ok()
        {
            var e = LatencyCalibration.Estimate(Clicks, new List<double> { 1.1, 1.6 });
            Assert.False(e.Ok);
            Assert.Equal(2, e.Pairs);
        }

        [Fact]
        public void Double_strums_on_one_click_count_once()
        {
            var strums = new List<double> { 1.10, 1.14, 1.60, 2.10 };
            var e = LatencyCalibration.Estimate(Clicks, strums);
            Assert.Equal(3, e.Pairs);
            Assert.Equal(100, e.LatencyMs, 3);
        }

        [Fact]
        public void Early_strums_clamp_to_zero()
        {
            var strums = new List<double> { 0.97, 1.47, 1.97, 2.47 };
            var e = LatencyCalibration.Estimate(Clicks, strums);
            Assert.True(e.Ok);
            Assert.Equal(0, e.LatencyMs, 3);
        }

        [Fact]
        public void Scattered_strums_ask_to_try_again()
        {
            var strums = new List<double> { 1.00, 1.70, 1.95, 2.75 };   // 0, +200, −50, +250 ms
            Assert.False(LatencyCalibration.Estimate(Clicks, strums).Ok);
        }

        [Fact]
        public void Beat_grid_round_trip()
        {
            var g = new BeatGrid(90, 10.0);
            Assert.Equal(10.0 + 3 * (60.0 / 90), g.TimeOfBeat(3), 9);
            Assert.Equal(3, g.BeatAt(g.TimeOfBeat(3)), 9);
            Assert.Equal(-4, g.BeatAt(10.0 - 4 * g.SecondsPerBeat), 9);   // count-in beats are negative
        }

        [Fact]
        public void Dsp_smoother_fills_in_between_audio_buffers()
        {
            var s = new DspClockSmoother();
            // Audio clock moves every 20 ms, frames every ~11 ms.
            double prev = s.Update(0.000, 100.000);
            double a = s.Update(0.011, 100.000);
            double b = s.Update(0.022, 100.020);
            double c = s.Update(0.033, 100.020);
            Assert.True(a > prev && b > a && c > b);
            Assert.Equal(100.033, c, 2);
        }

        [Fact]
        public void Dsp_smoother_resyncs_after_a_jump()
        {
            var s = new DspClockSmoother();
            s.Update(0, 100);
            double t = s.Update(1, 105);   // app was paused; audio clock jumped
            Assert.Equal(105, t, 6);
        }
    }
}

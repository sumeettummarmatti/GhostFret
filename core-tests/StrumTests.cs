using GhostFret.Core.Strum;
using GhostFret.Core.Timing;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class StrumTests
    {
        // ---- Direction classifier edge cases ----

        private static StrumDirectionClassifier Swing(double from, double to, double startTime, double seconds)
        {
            var c = new StrumDirectionClassifier();
            int n = 10;
            for (int i = 0; i <= n; i++)
                c.AddSample(startTime + seconds * i / n, from + (to - from) * i / n);
            return c;
        }

        [Fact]
        public void Down_and_up_strums()
        {
            Assert.Equal(StrumDirection.Down, Swing(0, 0.2, 0, 0.1).DirectionAt(0.1));   // 2 m/s along axis
            Assert.Equal(StrumDirection.Up, Swing(0.2, 0, 0, 0.1).DirectionAt(0.1));
        }

        [Fact]
        public void No_samples_or_one_sample_is_none()
        {
            var c = new StrumDirectionClassifier();
            Assert.Equal(StrumDirection.None, c.DirectionAt(1));
            c.AddSample(1, 0);
            Assert.Equal(StrumDirection.None, c.DirectionAt(1));
        }

        [Fact]
        public void Slow_drift_is_not_a_strum()
        {
            Assert.Equal(StrumDirection.None, Swing(0, 0.01, 0, 0.1).DirectionAt(0.1));  // 0.1 m/s
        }

        [Fact]
        public void Lost_tracking_stays_quiet()
        {
            var c = Swing(0, 0.2, 0, 0.1);
            Assert.Equal(StrumDirection.None, c.DirectionAt(0.5));   // last sample 0.4 s old
        }

        [Fact]
        public void Uses_motion_just_before_the_onset_not_after()
        {
            var c = new StrumDirectionClassifier();
            // Down until t = 0.1, then the hand turns and goes up.
            for (int i = 0; i <= 10; i++) c.AddSample(i * 0.01, i * 0.02);
            for (int i = 1; i <= 10; i++) c.AddSample(0.1 + i * 0.01, 0.2 - i * 0.02);
            Assert.Equal(StrumDirection.Down, c.DirectionAt(0.1));
            Assert.Equal(StrumDirection.Up, c.DirectionAt(0.2));
        }

        [Fact]
        public void Out_of_order_samples_are_ignored()
        {
            var c = Swing(0, 0.2, 0, 0.1);
            c.AddSample(0.05, 5.0);    // stale sample arriving late
            Assert.Equal(StrumDirection.Down, c.DirectionAt(0.1));
        }

        [Fact]
        public void Sparse_samples_still_give_a_direction()
        {
            var c = new StrumDirectionClassifier();
            c.AddSample(0.0, 0.0);
            c.AddSample(0.1, -0.1);    // 100 ms gap, more than the lookback
            Assert.Equal(StrumDirection.Up, c.DirectionAt(0.1));
        }

        // ---- Pattern ----

        [Fact]
        public void Pattern_spans_one_bar()
        {
            var p = StrumPattern.Parse(new[] { "D", "-", "D", "U", "D", "U" }, 3);
            Assert.Equal(2, p.SlotsPerBeat);
            Assert.Equal(StrumDirection.Down, p.At(0));
            Assert.Equal(StrumDirection.None, p.At(1));
            Assert.Equal(StrumDirection.Up, p.At(5));
            Assert.Equal(StrumDirection.Down, p.At(6));     // next bar
            Assert.Equal(StrumDirection.Up, p.At(-1));      // count-in slot before beat 0
        }

        [Fact]
        public void Short_pattern_repeats_each_beat()
        {
            var p = StrumPattern.Parse(new[] { "D" }, 4);
            Assert.Equal(1, p.SlotsPerBeat);
            Assert.Equal(StrumDirection.Down, p.At(3));
        }

        [Fact]
        public void Bad_pattern_falls_back_to_downs()
        {
            var p = StrumPattern.Parse(null, 4);
            Assert.Equal(StrumDirection.Down, p.At(2));
        }

        [Fact]
        public void Judge_against_the_nearest_wanted_strum()
        {
            var grid = new BeatGrid(60, 0);   // 1 s per beat
            var p = StrumPattern.Parse(new[] { "D", "-", "D", "-", "D", "-", "D", "-" }, 4);

            var onBeat = p.Judge(grid, 2.05);
            Assert.Equal(TimingVerdict.OnTime, onBeat.Timing.Verdict);
            Assert.Equal(StrumDirection.Down, onBeat.Expected);
            Assert.False(onBeat.Extra);

            var late = p.Judge(grid, 2.3);
            Assert.Equal(TimingVerdict.Late, late.Timing.Verdict);
            Assert.Equal(2, late.Timing.NearestSlot);

            var extra = p.Judge(grid, 2.5);   // right on the "and", which is a rest
            Assert.True(extra.Extra);
        }

        [Fact]
        public void Judge_subtracts_latency()
        {
            var grid = new BeatGrid(60, 0) { LatencyMs = 120 };
            var p = StrumPattern.QuarterDowns(4);
            Assert.Equal(TimingVerdict.OnTime, p.Judge(grid, 3.12).Timing.Verdict);
            Assert.Equal(3, p.Judge(grid, 3.12).Timing.NearestSlot);
        }

        [Fact]
        public void Judge_up_strum_slot()
        {
            var grid = new BeatGrid(120, 1.0);   // 0.5 s per beat, starts at 1 s
            var p = StrumPattern.Parse(new[] { "D", "-", "D", "U", "-", "U", "D", "U" }, 4);
            var j = p.Judge(grid, 1.0 + 1.5 * 0.5 + 0.02);   // beat 1.5
            Assert.Equal(StrumDirection.Up, j.Expected);
            Assert.Equal(3, j.Slot);
            Assert.Equal(TimingVerdict.OnTime, j.Timing.Verdict);
        }

        // ---- Words ----

        [Fact]
        public void Feedback_words_are_gentle()
        {
            bool good;
            var on = new TimingResult { Verdict = TimingVerdict.OnTime };
            var early = new TimingResult { Verdict = TimingVerdict.Early };
            Assert.Equal("On time", StrumFeedback.ForStrum(on, StrumDirection.Down, StrumDirection.Down, out good));
            Assert.True(good);
            Assert.Equal("A little early", StrumFeedback.ForStrum(early, StrumDirection.None, StrumDirection.Down, out good));
            Assert.False(good);
            Assert.Equal("Try an up-strum here", StrumFeedback.ForStrum(on, StrumDirection.Down, StrumDirection.Up, out good));
            // Direction unknown: say nothing about direction.
            Assert.Equal("On time", StrumFeedback.ForStrum(on, StrumDirection.None, StrumDirection.Up, out good));
        }

        [Fact]
        public void Missed_up_strum_only_while_playing_and_not_too_often()
        {
            var p = StrumPattern.Parse(new[] { "D", "U" }, 4);   // up on every "and"
            var d = new MissedStrumDetector();
            long slot;

            // Not playing at all: never nag.
            Assert.False(d.CheckMissedUp(p, 0.0, 0.2, out slot));
            Assert.False(d.CheckMissedUp(p, 2.0, 0.2, out slot));

            // Playing downs only: the first missed up is mentioned, the next within the cooldown is not.
            d.MarkHit(4, 2.0);
            Assert.True(d.CheckMissedUp(p, 2.8, 0.2, out slot));    // slot 5 (beat 2.5) passed
            Assert.Equal(5, slot);
            d.MarkHit(6, 3.0);
            Assert.False(d.CheckMissedUp(p, 3.8, 0.2, out slot));   // cooldown

            // Every down and up played: nothing to say, even after the cooldown.
            for (long s = 8; s <= 13; s++) d.MarkHit(s, s / 2.0);
            Assert.False(d.CheckMissedUp(p, 6.8, 0.2, out slot));
        }
    }
}

using GhostFret.Core.Content;
using GhostFret.Core.Lesson;
using GhostFret.Core.Timing;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class SongTests
    {
        // Two sections of 2 bars each in 4/4: Verse = Em, D (beats 0..8), Chorus = G, C (beats 8..16).
        private static SongChart Chart()
        {
            return new SongChart
            {
                id = "test", title = "Test", source = "Original", difficulty = 1,
                chordsRequired = new[] { "Em", "D", "G", "C" }, timeSignature = new[] { 4, 4 },
                originalBpm = 100, strumPattern = new[] { "D", "-", "D", "U", "-", "U", "D", "U" }, backingTrack = "",
                sections = new[]
                {
                    new SongSection { name = "Verse", bars = new[] { new SongBar { chord = "Em", beats = 4 }, new SongBar { chord = "D", beats = 4 } } },
                    new SongSection { name = "Chorus", bars = new[] { new SongBar { chord = "G", beats = 4 }, new SongBar { chord = "C", beats = 4 } } }
                }
            };
        }

        [Fact]
        public void Test_chart_is_valid()
        {
            Assert.Empty(SongValidator.Validate(Chart()));
        }

        [Fact]
        public void Validator_rejects_duplicate_or_missing_section_names()
        {
            var c = Chart();
            c.sections[1].name = "verse";
            Assert.Contains(SongValidator.Validate(c), e => e.Contains("two sections"));
            c.sections[1].name = "";
            Assert.Contains(SongValidator.Validate(c), e => e.Contains("no name"));
        }

        [Fact]
        public void Timeline_at_tempo()
        {
            var t = SongTimeline.Build(Chart());
            Assert.Equal(16, t.TotalBeats);
            Assert.Equal(9.6, t.DurationSeconds(100), 6);           // 16 beats at 100 BPM
            Assert.Equal(12.8, t.DurationSeconds(75), 6);           // slowed to 75%
            Assert.Equal(2.4, t.StartSeconds(1, 100), 6);           // D starts on beat 4
            Assert.Equal(1, t.IndexAtSeconds(2.5, 100));            // D
            Assert.Equal(0, t.IndexAtSeconds(2.5, 75));             // still Em when slower (beat 3.125)
            Assert.Equal(-1, t.IndexAtSeconds(10, 100));            // after the end
            Assert.Equal(-1, t.IndexAtSeconds(-0.1, 100));          // count-in
        }

        [Fact]
        public void Timeline_sections_have_spans()
        {
            var t = SongTimeline.Build(Chart());
            SectionSpan s;
            Assert.True(t.TryGetSection("CHORUS", out s));
            Assert.Equal(8, s.StartBeat);
            Assert.Equal(16, s.EndBeat);
            Assert.False(t.TryGetSection("Bridge", out s));
        }

        [Fact]
        public void Clock_plays_straight_through_and_finishes()
        {
            var clock = new SongClock(SongTimeline.Build(Chart()), 4);
            Assert.Equal(-4, clock.Advance(-4));    // count-in
            Assert.Equal(-1, clock.CurrentIndex);
            clock.Advance(5);
            Assert.Equal(1, clock.CurrentIndex);
            Assert.False(clock.IsFinished);
            clock.Advance(16);
            Assert.True(clock.IsFinished);
        }

        [Fact]
        public void Clock_loops_a_section_and_counts_passes()
        {
            var clock = new SongClock(SongTimeline.Build(Chart()), 4);
            clock.Advance(1);
            Assert.True(clock.LoopSection("Verse"));
            Assert.Equal(7.5, clock.Advance(7.5));
            Assert.Equal(0.5, clock.Advance(8.5), 6);   // wrapped
            Assert.Equal(1, clock.LoopPasses);
            Assert.False(clock.IsFinished);
            Assert.Equal(0, clock.NextIndex(1));         // after D comes Em again
            // A strum heard just after the wrap but played just before it maps to the loop end.
            Assert.Equal(7.9, clock.SongBeatAt(7.9), 6);
        }

        [Fact]
        public void Clock_jumps_to_another_section_at_the_next_bar_line()
        {
            var clock = new SongClock(SongTimeline.Build(Chart()), 4);
            clock.Advance(1.5);
            clock.LoopSection("Chorus");
            Assert.Equal(3.5, clock.Advance(3.5), 6);    // still in the verse until the bar line
            Assert.Equal(8, clock.Advance(4), 6);        // bar line → chorus start
            Assert.Equal(2, clock.CurrentIndex);
            Assert.Equal(9, clock.Advance(5), 6);
        }

        [Fact]
        public void Clock_loop_before_start_begins_at_the_section()
        {
            var clock = new SongClock(SongTimeline.Build(Chart()), 4);
            clock.LoopSection("Chorus");
            Assert.Equal(4, clock.Advance(-4), 6);   // count-in (the player shows the chorus chord here)
            Assert.Equal(8, clock.Advance(0), 6);    // beat 0 = chorus start
        }

        [Fact]
        public void Clock_holds_still_during_resume_lead_in()
        {
            var clock = new SongClock(SongTimeline.Build(Chart()), 4);
            clock.Advance(6.25);
            // Resume shifts the grid one bar back for a lead-in: raw beats 2.25..6.25 replay.
            Assert.Equal(6.25, clock.Advance(2.25), 6);
            Assert.Equal(6.25, clock.Advance(5), 6);
            Assert.Equal(6.5, clock.Advance(6.5), 6);
        }

        [Fact]
        public void Clearing_a_loop_carries_on_to_the_end()
        {
            var clock = new SongClock(SongTimeline.Build(Chart()), 4);
            clock.LoopSection("Verse");
            clock.Advance(9);                // wrapped to 1
            clock.ClearLoop();
            Assert.Equal(-1, clock.NextIndex(clock.Timeline.Events.Count - 1));
            clock.Advance(24);               // song beat 16
            Assert.True(clock.IsFinished);
        }

        [Fact]
        public void Next_chord_one_beat_early_wraps_around_a_loop()
        {
            var clock = new SongClock(SongTimeline.Build(Chart()), 4);
            clock.LoopSection("Verse");
            clock.Advance(7.2);
            Assert.Equal(0.8, clock.BeatsUntil(0), 6);   // Em comes back in under a beat
        }

        [Fact]
        public void Score_counts_clean_timing_and_streak()
        {
            var run = new SongRun();
            for (int i = 0; i < 9; i++) run.RecordStrum("Em", true, TimingVerdict.OnTime);
            run.RecordStrum("D", false, TimingVerdict.Late);
            run.RecordStrum("D", true, TimingVerdict.Early);
            run.RecordStrum("D", true, null);              // extra strum on a rest: no timing
            Assert.Equal(12, run.Strums);
            Assert.Equal(11 / 12.0, run.CleanRate, 6);
            Assert.Equal(9 / 11.0, run.TimingRate, 6);
            Assert.Equal(9, run.LongestStreak);
            Assert.Equal(2, run.CurrentStreak);
        }

        [Fact]
        public void Silent_chords_count_against_clean_rate_and_break_the_streak()
        {
            var run = new SongRun();
            run.RecordStrum("Em", true, TimingVerdict.OnTime);
            run.RecordStrum("Em", true, TimingVerdict.OnTime);
            run.RecordSilentChord("D");
            run.RecordStrum("Em", true, TimingVerdict.OnTime);
            Assert.Equal(0.75, run.CleanRate, 6);
            Assert.Equal(1, run.CurrentStreak);
            Assert.Equal(2, run.LongestStreak);
        }

        [Fact]
        public void Summary_is_kind_and_specific()
        {
            var run = new SongRun();
            Assert.Equal("Let's play that one together again.", run.Headline());
            for (int i = 0; i < 7; i++) run.RecordStrum("G", true, TimingVerdict.OnTime);
            run.RecordStrum("C", false, TimingVerdict.OnTime);
            run.RecordStrum("C", false, TimingVerdict.OnTime);
            run.RecordStrum("C", true, TimingVerdict.OnTime);
            Assert.Equal("80% clean chords — your longest streak was 7!", run.Headline());
            Assert.Contains("C was the tricky one", run.Tip());
            Assert.DoesNotContain("Wrong", run.Headline() + run.Tip());
        }

        [Fact]
        public void Tip_mentions_rushing_when_early()
        {
            var run = new SongRun();
            for (int i = 0; i < 6; i++) run.RecordStrum("Em", true, TimingVerdict.Early);
            run.RecordStrum("Em", true, TimingVerdict.OnTime);
            Assert.Contains("rushed", run.Tip());
        }

        [Fact]
        public void Streak_milestones()
        {
            Assert.True(SongRun.IsStreakMilestone(5));
            Assert.True(SongRun.IsStreakMilestone(20));
            Assert.False(SongRun.IsStreakMilestone(7));
            Assert.False(SongRun.IsStreakMilestone(15));
        }

        [Fact]
        public void Result_carries_the_next_tempo()
        {
            var tempo = new AdaptiveTempo(0.75);
            var run = new SongRun();
            for (int i = 0; i < 10; i++) run.RecordStrum("Em", i < 5, TimingVerdict.OnTime);
            double before = tempo.Factor;
            tempo.RecordRun(run.CleanRate);   // 50% → slow down
            var result = run.Result("test", tempo.Factor, false);
            Assert.Equal(0.65, result.tempoFactor, 3);
            Assert.Equal(0.5, result.cleanRate, 6);
            Assert.Equal("Next run is a little slower, so changes feel easier.", SongRun.TempoNote(before, tempo.Factor));
            Assert.Equal("", SongRun.TempoNote(0.8, 0.8));
        }

        [Fact]
        public void Adaptive_tempo_restore_is_clamped()
        {
            var tempo = new AdaptiveTempo();
            tempo.Restore(0.2);
            Assert.Equal(0.5, tempo.Factor, 6);
            tempo.Restore(0.85);
            Assert.Equal(85, tempo.CurrentBpm(100), 6);
        }
    }
}

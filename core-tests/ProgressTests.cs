using System;
using GhostFret.Core.Progress;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class ProgressTests
    {
        [Fact]
        public void Streak_grows_on_consecutive_days_and_resets_after_a_gap()
        {
            var p = new PracticeProgress();
            p.MarkSession(new DateTime(2026, 11, 1));
            p.MarkSession(new DateTime(2026, 11, 1));
            Assert.Equal(1, p.streakDays);
            p.MarkSession(new DateTime(2026, 11, 2));
            p.MarkSession(new DateTime(2026, 11, 3));
            Assert.Equal(3, p.streakDays);
            Assert.Equal(3, p.StreakAsOf(new DateTime(2026, 11, 4)));
            Assert.Equal(0, p.StreakAsOf(new DateTime(2026, 11, 6)));
            p.MarkSession(new DateTime(2026, 11, 6));
            Assert.Equal(1, p.streakDays);
        }

        [Fact]
        public void First_time_until_a_clean_chord_or_a_finished_session()
        {
            var p = new PracticeProgress();
            Assert.True(p.IsFirstTime);
            p.RecordChordAttempt("Em", false);
            Assert.True(p.IsFirstTime);
            p.RecordChordAttempt("Em", true);
            Assert.False(p.IsFirstTime);
        }

        [Fact]
        public void Day_moves_on_once_per_date_and_stops_at_the_end()
        {
            var p = new PracticeProgress();
            var d = new DateTime(2026, 11, 1);
            Assert.True(p.CompleteDay(d, 3));
            Assert.False(p.CompleteDay(d, 3));
            Assert.Equal(2, p.currentDay);
            p.CompleteDay(d.AddDays(1), 3);
            p.CompleteDay(d.AddDays(2), 3);
            Assert.Equal(3, p.currentDay);
        }

        [Fact]
        public void Drill_best_only_goes_up()
        {
            var p = new PracticeProgress();
            Assert.True(p.RecordDrill("Em-D", 5));
            Assert.False(p.RecordDrill("Em-D", 4));
            Assert.True(p.RecordDrill("Em-D", 6));
            Assert.Equal(6, p.BestDrill("Em-D"));
            Assert.Equal(0, p.BestDrill("G-C"));
        }

        [Fact]
        public void Song_keeps_best_clean_rate_and_next_run_tempo()
        {
            var p = new PracticeProgress();
            Assert.Equal(0f, p.SongTempo("em-groove"));
            p.RecordSong("em-groove", 0.8f, 0.80f);
            p.RecordSong("em-groove", 0.5f, 0.70f);   // slowed down after a hard run
            Assert.Equal(0.70f, p.SongTempo("em-groove"), 3);
            Assert.Equal(0.8f, p.songs[0].bestCleanRate, 3);
            Assert.Equal(0.80f, p.songs[0].tempoFactorReached, 3);
        }

        [Fact]
        public void Sanitize_repairs_a_broken_load()
        {
            var p = new PracticeProgress { chords = null, drills = null, songs = null, settings = null, currentDay = 0, streakDays = -3 };
            p.Sanitize();
            Assert.NotNull(p.chords); Assert.NotNull(p.drills); Assert.NotNull(p.songs); Assert.NotNull(p.settings);
            Assert.Equal(1, p.currentDay);
            Assert.Equal(0, p.streakDays);

            p.settings.overlayBrightness = float.NaN;
            p.settings.metronomeVolume = 7f;
            p.chords.Add(null);
            p.chords.Add(new ChordStat { chord = "Em", attempts = 1, clean = 4 });
            p.Sanitize();
            Assert.Equal(1f, p.settings.metronomeVolume);
            Assert.False(float.IsNaN(p.settings.overlayBrightness));
            Assert.Single(p.chords);
            Assert.Equal(4, p.chords[0].attempts);
        }

        [Fact]
        public void Wrapup_lines_are_kind_and_specific()
        {
            Assert.Equal("Day 1 of your streak!", SessionSummary.StreakLine(1));
            Assert.Equal("3-day streak!", SessionSummary.StreakLine(3));
            var s = new SessionSummary();
            Assert.Contains("practised", s.BestLine());
            s.NewChordLearned = "Em";
            Assert.Equal("You learned Em today!", s.BestLine());
            s.DrillPair = "Em-D"; s.DrillScore = 9; s.DrillPersonalBest = true;
            Assert.Contains("personal best", s.BestLine());
        }

        [Fact]
        public void Break_tip_shows_once_after_fifteen_minutes()
        {
            var b = new BreakReminder(0);
            Assert.False(b.ShouldSuggest(14 * 60));
            Assert.True(b.ShouldSuggest(15 * 60));
            Assert.False(b.ShouldSuggest(20 * 60));
            Assert.DoesNotContain("health", BreakReminder.Message.ToLowerInvariant());
        }
    }
}

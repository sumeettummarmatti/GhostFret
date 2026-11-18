using System.Collections.Generic;
using System.IO;
using GhostFret.Core.Content;
using GhostFret.Core.Feedback;
using GhostFret.Core.Lesson;
using GhostFret.Core.Strum;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class LessonCoachTests
    {
        private static ChordFeedback Fb(FeedbackKind k, int s = -1, string msg = "msg")
        {
            return new ChordFeedback { Kind = k, StringIndex = s, Message = msg };
        }

        private static Curriculum Curriculum()
        {
            string path = Path.Combine(System.AppContext.BaseDirectory, "Content", "curriculum.json");
            return System.Text.Json.JsonSerializer.Deserialize<Curriculum>(File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        }

        [Fact]
        public void Three_clean_strums_learn_the_chord()
        {
            var l = ChordLesson.Single("Em");
            Assert.Equal(LessonReactionKind.Clean, l.OnFeedback(Fb(FeedbackKind.Clean)).Kind);
            Assert.Equal(LessonReactionKind.Clean, l.OnFeedback(Fb(FeedbackKind.Clean)).Kind);
            var last = l.OnFeedback(Fb(FeedbackKind.Clean));
            Assert.Equal(LessonReactionKind.LessonDone, last.Kind);
            Assert.True(l.IsDone);
            Assert.Null(l.OnFeedback(Fb(FeedbackKind.Clean)));
        }

        [Fact]
        public void Problems_do_not_count_and_do_not_reset()
        {
            var l = ChordLesson.Single("Em");
            l.OnFeedback(Fb(FeedbackKind.Clean));
            var r = l.OnFeedback(Fb(FeedbackKind.WeakString, 3, "The G string is muted."));
            Assert.Equal(LessonReactionKind.StringProblem, r.Kind);
            Assert.Equal(3, r.StringIndex);
            Assert.Equal(1, l.CleanCount);
        }

        [Fact]
        public void Not_sure_gives_a_general_tip_never_a_specific_claim()
        {
            var l = ChordLesson.Single("Em");
            var r = l.OnFeedback(Fb(FeedbackKind.NotSure, 2, "The A string is muted."));
            Assert.Equal(LessonReactionKind.GeneralTip, r.Kind);
            Assert.Equal(-1, r.StringIndex);
            Assert.DoesNotContain("string", r.Message);
        }

        [Fact]
        public void String_problem_without_a_valid_string_becomes_general()
        {
            var l = ChordLesson.Single("Em");
            Assert.Equal(LessonReactionKind.GeneralTip, l.OnFeedback(Fb(FeedbackKind.WeakString, -1)).Kind);
        }

        [Fact]
        public void Day_two_reviews_em_once_then_learns_d()
        {
            var l = ChordLesson.ForDay(new[] { "D" }, new[] { "Em" }, new[] { "Em" });
            Assert.Equal(2, l.Steps.Count);
            Assert.Equal("Em", l.CurrentChord);
            Assert.Equal(LessonReactionKind.ChordLearned, l.OnFeedback(Fb(FeedbackKind.Clean)).Kind);
            Assert.Equal("D", l.CurrentChord);
        }

        [Fact]
        public void Day_one_is_just_em()
        {
            var c = Curriculum();
            var l = ChordLesson.ForDay(DayPlanner.NewChords(c, 1), DayPlanner.PreviousDayChords(c, 1), DayPlanner.LearnedBefore(c, 1));
            Assert.Single(l.Steps);
            Assert.Equal("Em", l.CurrentChord);
        }

        [Fact]
        public void Planner_finds_drills_songs_and_day_lines()
        {
            var c = Curriculum();
            Assert.Null(DayPlanner.DrillPair(c, 1));
            Assert.Equal("Em-D", DayPlanner.DrillPair(c, 2));
            Assert.Equal("Em-D", DayPlanner.DrillPair(c, 2));
            Assert.False(DayPlanner.GuidedOptions(c, 1, false, true).IncludeDrill);
            Assert.True(DayPlanner.GuidedOptions(c, 2, false, true).IncludeDrill);
            Assert.StartsWith("Day 1: ", DayPlanner.DayLine(c, 1));
            Assert.Equal(DayPlanner.LastDay(c), DayPlanner.Day(c, 999).day);
            string a, b;
            Assert.True(DayPlanner.TryParsePair("Em-D", out a, out b));
            Assert.Equal("Em", a); Assert.Equal("D", b);
            Assert.False(DayPlanner.TryParsePair("Em", out a, out b));
        }

        [Fact]
        public void Song_pick_prefers_todays_unlocked_song()
        {
            var c = Curriculum();
            var groove = new SongChart { id = "em-groove", difficulty = 1, chordsRequired = new[] { "Em" } };
            var sailor = new SongChart { id = "drunken-sailor", difficulty = 1, chordsRequired = new[] { "Em", "D" } };
            var songs = new List<SongChart> { groove, sailor };
            Assert.Equal("em-groove", DayPlanner.PickSong(c, 1, songs, DayPlanner.LearnedBy(c, 1)).id);
            Assert.Equal("drunken-sailor", DayPlanner.PickSong(c, 2, songs, DayPlanner.LearnedBy(c, 2)).id);
            Assert.Null(DayPlanner.PickSong(c, 1, new List<SongChart> { sailor }, DayPlanner.LearnedBy(c, 1)));
        }

        [Fact]
        public void Change_drill_counts_switches_and_stops_at_sixty_seconds()
        {
            var d = new ChangeDrill("Em", "D");
            d.Start(0);
            Assert.False(d.OnCleanChord("Em", 1));   // starts the chain
            Assert.Equal("D", d.ExpectedChord);
            Assert.True(d.OnCleanChord("D", 3));
            Assert.False(d.OnCleanChord("D", 4));    // same chord again doesn't count
            Assert.True(d.OnCleanChord("Em", 5));
            Assert.Equal(2, d.CleanChanges);
            Assert.False(d.IsTimeUp(59));
            Assert.True(d.IsTimeUp(60));
            Assert.False(d.OnCleanChord("D", 61));
            Assert.Equal("Em-D", d.PairKey);
        }

        [Fact]
        public void Strum_practice_judges_timing_and_direction()
        {
            // 60 bpm, start at t=0 → one-bar count-in (4 beats), practice beat 0 at t=4.
            var p = new StrumPractice(new[] { "D", "-", "D", "U", "D", "U", "D", "U" }, 60, 0, bars: 1);
            Assert.True(p.InCountIn(1));
            Assert.Null(p.Judge(1.0, StrumDirection.Down));                    // count-in ignored
            var onBeat = p.Judge(4.02, StrumDirection.Down).Value;
            Assert.True(onBeat.Good);
            var early = p.Judge(5.8, StrumDirection.Down).Value;               // beat 2 early by 200 ms
            Assert.Contains("early", early.Message);
            var wrongDir = p.Judge(5.5, StrumDirection.Down).Value;            // "and" of beat 2 is U
            Assert.Equal(StrumDirection.Up, wrongDir.Expected);
            Assert.False(wrongDir.Good);
            var unknownDir = p.Judge(6.5, StrumDirection.None).Value;          // tracking unsure: no direction claim
            Assert.True(unknownDir.Good);
            Assert.False(p.IsFinished(7.5));
            Assert.True(p.IsFinished(8.6));
            Assert.Equal(4, p.Strums);
        }

        [Fact]
        public void Strum_practice_three_four_has_three_beats_per_bar()
        {
            var p = new StrumPractice(new[] { "D", "-", "D", "U", "D", "U" }, 90, 0, bars: 4);
            Assert.Equal(3, p.BeatsPerBar);
            Assert.Equal(12, p.TotalBeats);
        }
    }
}

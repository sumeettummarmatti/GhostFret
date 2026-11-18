using GhostFret.Core.Lesson;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class SessionFlowTests
    {
        private static SessionOptions All(bool fitted = false)
        {
            return new SessionOptions { FretboardFitted = fitted, IncludeStrum = true, IncludeDrill = true, IncludeSong = true };
        }

        [Fact]
        public void Guided_session_walks_every_step_to_wrapup_and_done()
        {
            var f = new SessionFlow();
            Assert.True(f.Begin(SessionMode.Guided, All()));
            Assert.Equal(SessionState.Calibrate, f.State);
            Assert.True(f.Fire(SessionTrigger.CalibrationDone)); Assert.Equal(SessionState.Tune, f.State);
            Assert.True(f.Fire(SessionTrigger.TuningDone)); Assert.Equal(SessionState.Lesson, f.State);
            Assert.True(f.Fire(SessionTrigger.SkillLearned)); Assert.Equal(SessionState.StrumCoach, f.State);
            Assert.True(f.Fire(SessionTrigger.StrumDone)); Assert.Equal(SessionState.Drill, f.State);
            Assert.True(f.Fire(SessionTrigger.DrillDone)); Assert.Equal(SessionState.Song, f.State);
            Assert.True(f.Fire(SessionTrigger.SongDone)); Assert.Equal(SessionState.WrapUp, f.State);
            Assert.True(f.Fire(SessionTrigger.Finish)); Assert.Equal(SessionState.Done, f.State);
        }

        [Fact]
        public void Day_one_skips_the_drill_and_goes_lesson_strum_song()
        {
            var f = new SessionFlow();
            f.Begin(SessionMode.Guided, new SessionOptions { IncludeDrill = false });
            f.Fire(SessionTrigger.CalibrationDone);
            f.Fire(SessionTrigger.TuningDone);
            f.Fire(SessionTrigger.SkillLearned);
            Assert.True(f.Fire(SessionTrigger.StrumDone));
            Assert.Equal(SessionState.Song, f.State);
        }

        [Fact]
        public void Already_fitted_skips_calibration()
        {
            var f = new SessionFlow();
            f.Begin(SessionMode.Guided, All(fitted: true));
            Assert.Equal(SessionState.Tune, f.State);
        }

        [Fact]
        public void Wrong_trigger_changes_nothing()
        {
            var f = new SessionFlow();
            f.Begin(SessionMode.Guided, All());
            Assert.False(f.Fire(SessionTrigger.SongDone));
            Assert.False(f.Fire(SessionTrigger.TuningDone));
            Assert.Equal(SessionState.Calibrate, f.State);
        }

        [Fact]
        public void Menu_modes_go_calibrate_then_mode_then_wrapup()
        {
            var f = new SessionFlow();
            f.Begin(SessionMode.ChangeDrill, All());
            f.Fire(SessionTrigger.CalibrationDone);
            Assert.Equal(SessionState.Drill, f.State);
            f.Fire(SessionTrigger.DrillDone);
            Assert.Equal(SessionState.WrapUp, f.State);
        }

        [Fact]
        public void Tuner_mode_needs_no_calibration()
        {
            var f = new SessionFlow();
            f.Begin(SessionMode.Tuner, All());
            Assert.Equal(SessionState.Tune, f.State);
            f.Fire(SessionTrigger.TuningDone);
            Assert.Equal(SessionState.WrapUp, f.State);
        }

        [Fact]
        public void Air_mode_from_welcome_completes_to_wrapup()
        {
            var f = new SessionFlow();
            Assert.True(f.Fire(SessionTrigger.NoGuitar));
            Assert.Equal(SessionState.AirMode, f.State);
            Assert.True(f.Fire(SessionTrigger.Finish));
            Assert.Equal(SessionState.WrapUp, f.State);
        }

        [Fact]
        public void Mic_denied_mid_lesson_can_switch_to_air()
        {
            var f = new SessionFlow();
            f.Begin(SessionMode.Guided, All(fitted: true));
            f.Fire(SessionTrigger.TuningDone);
            Assert.True(f.Fire(SessionTrigger.NoGuitar));
            Assert.Equal(SessionState.AirMode, f.State);
            Assert.Equal(SessionMode.Air, f.Mode);
        }

        [Theory]
        [InlineData(SessionState.Lesson)]
        [InlineData(SessionState.StrumCoach)]
        [InlineData(SessionState.Drill)]
        [InlineData(SessionState.Song)]
        public void Pause_resumes_exactly_where_you_were(SessionState target)
        {
            var f = new SessionFlow();
            f.Begin(SessionMode.Guided, All(fitted: true));
            while (f.State != target) Assert.True(f.Fire(SessionTrigger.Skip));
            Assert.True(f.Fire(SessionTrigger.Pause));
            Assert.Equal(SessionState.Paused, f.State);
            Assert.Equal(target, f.PausedFrom);
            Assert.False(f.Fire(SessionTrigger.Pause));
            Assert.True(f.Fire(SessionTrigger.Resume));
            Assert.Equal(target, f.State);
            // And the next step still follows on.
            Assert.True(f.Fire(SessionTrigger.Skip));
        }

        [Fact]
        public void Air_mode_can_pause_and_resume()
        {
            var f = new SessionFlow();
            f.Fire(SessionTrigger.NoGuitar);
            Assert.True(f.Fire(SessionTrigger.Pause));
            Assert.True(f.Fire(SessionTrigger.Resume));
            Assert.Equal(SessionState.AirMode, f.State);
        }

        [Fact]
        public void Cannot_pause_on_welcome_calibrate_or_wrapup()
        {
            var f = new SessionFlow();
            Assert.False(f.Fire(SessionTrigger.Pause));
            f.Begin(SessionMode.Guided, All());
            Assert.False(f.Fire(SessionTrigger.Pause));
        }

        [Fact]
        public void Finish_from_pause_goes_to_wrapup_and_restart_goes_home()
        {
            var f = new SessionFlow();
            f.Begin(SessionMode.ChordCoach, All(fitted: true));
            f.Fire(SessionTrigger.Pause);
            Assert.True(f.Fire(SessionTrigger.Finish));
            Assert.Equal(SessionState.WrapUp, f.State);
            Assert.True(f.Fire(SessionTrigger.Restart));
            Assert.Equal(SessionState.Welcome, f.State);
            Assert.True(f.Begin(SessionMode.Song, All(fitted: true)));
            Assert.Equal(SessionState.Song, f.State);
        }

        [Fact]
        public void Begin_only_from_welcome()
        {
            var f = new SessionFlow();
            f.Begin(SessionMode.Guided, All());
            Assert.False(f.Begin(SessionMode.Song, All()));
        }
    }
}

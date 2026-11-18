using GhostFret.Core.Lesson;
using GhostFret.Core.Timing;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class LessonTests
    {
        [Fact]
        public void Tempo_slows_when_hard_and_speeds_up_after_two_good_runs()
        {
            var t = new AdaptiveTempo(0.75);
            t.RecordRun(0.4);
            Assert.Equal(0.65, t.Factor, 3);
            t.RecordRun(0.95); t.RecordRun(0.95);
            Assert.Equal(0.70, t.Factor, 3);
        }

        [Fact]
        public void Pause_returns_to_the_same_state()
        {
            var f = new SessionFlow();
            f.Fire(SessionTrigger.Start);
            f.Fire(SessionTrigger.CalibrationDone);
            Assert.True(f.Fire(SessionTrigger.Pause));
            Assert.True(f.Fire(SessionTrigger.Resume));
            Assert.Equal(SessionState.Tune, f.State);
        }

        [Fact]
        public void No_guitar_goes_to_air_mode()
        {
            var f = new SessionFlow();
            f.Fire(SessionTrigger.Start);
            Assert.True(f.Fire(SessionTrigger.NoGuitar));
            Assert.Equal(SessionState.AirMode, f.State);
        }

        [Fact]
        public void Strum_slightly_late_is_still_on_time()
        {
            var grid = new BeatGrid(60, 0);
            Assert.Equal(TimingVerdict.OnTime, grid.Judge(2.05).Verdict);
            Assert.Equal(TimingVerdict.Late, grid.Judge(2.2).Verdict);
        }
    }
}

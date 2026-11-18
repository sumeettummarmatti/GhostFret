using System.Numerics;
using GhostFret.Core.Lesson;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class SessionPauseTests
    {
        private static readonly Vector3 Up = Vector3.UnitY;
        private static readonly Vector3 Down = -Vector3.UnitY;
        private static readonly Vector3 TowardHead = Vector3.UnitZ;
        private static readonly Vector3 Here = new Vector3(0, 1, 0.3f);

        // Runs 1.5 s at 60 fps with a fixed pose; returns how many times it fired.
        private static int Hold(PalmUpPauseDetector d, Vector3 normal, bool pinching, bool tracked = true, float speed = 0f, double start = 0)
        {
            int fired = 0;
            for (int i = 0; i <= 90; i++)
            {
                double t = start + i / 60.0;
                Vector3 pos = Here + new Vector3((float)(speed * t), 0, 0);
                if (d.Update(t, tracked, normal, pos, pinching)) fired++;
            }
            return fired;
        }

        [Theory]
        // palmUp, pinching, still, tracked → fires?
        [InlineData(true, false, true, true, true)]
        [InlineData(true, true, true, true, false)]    // pinching: never (system-gesture safe)
        [InlineData(true, false, false, true, false)]  // moving
        [InlineData(false, false, true, true, false)]  // palm down
        [InlineData(true, false, true, false, false)]  // not tracked
        [InlineData(false, true, true, true, false)]
        [InlineData(true, true, false, true, false)]
        [InlineData(false, false, false, true, false)]
        public void Truth_table(bool palmUp, bool pinching, bool still, bool tracked, bool expected)
        {
            var d = new PalmUpPauseDetector();
            int fired = Hold(d, palmUp ? Up : Down, pinching, tracked, still ? 0f : 0.5f);
            Assert.Equal(expected ? 1 : 0, fired);
        }

        [Fact]
        public void Palm_toward_headset_is_not_palm_up()
        {
            var d = new PalmUpPauseDetector();
            Assert.Equal(0, Hold(d, TowardHead, false));
        }

        [Fact]
        public void Never_fires_while_pinching_even_if_pinch_is_brief()
        {
            var d = new PalmUpPauseDetector();
            int fired = 0;
            for (int i = 0; i < 600; i++)
            {
                double t = i / 60.0;
                bool pinch = i % 50 == 0;   // a pinch every ~0.8 s resets the hold
                if (d.Update(t, true, Up, Here, pinch)) fired++;
            }
            Assert.Equal(0, fired);
        }

        [Fact]
        public void Fires_once_per_hold_and_rearms_after_release()
        {
            var d = new PalmUpPauseDetector();
            Assert.Equal(1, Hold(d, Up, false));
            Assert.Equal(0, Hold(d, Up, false, start: 1.6)); // still holding: no second fire
            d.Update(3.2, true, Down, Here, false);            // release
            Assert.Equal(1, Hold(d, Up, false, start: 3.3));
        }

        [Fact]
        public void Disarm_stops_an_instant_refire()
        {
            var d = new PalmUpPauseDetector();
            d.Disarm();
            Assert.Equal(0, Hold(d, Up, false));
        }

        [Fact]
        public void Needs_the_full_hold_time()
        {
            var d = new PalmUpPauseDetector { HoldSeconds = 1.0 };
            bool fired = false;
            for (int i = 0; i < 55; i++) fired |= d.Update(i / 60.0, true, Up, Here, false);
            Assert.False(fired);
            Assert.True(d.IsHolding);
        }

        [Fact]
        public void Silence_fires_once_after_ten_seconds_and_resets_on_strum()
        {
            var s = new SilenceWatcher(0) { SilenceSeconds = 10 };
            Assert.False(s.Update(9.9));
            Assert.True(s.Update(10.0));
            Assert.False(s.Update(12));
            s.OnActivity(12);
            Assert.False(s.Update(21));
            Assert.True(s.Update(22.5));
        }
    }
}

using System;
using GhostFret.Core.Audio;
using GhostFret.Core.Music;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class TunerSmootherTests
    {
        private const double Tick = 0.1;   // the tuner reads ~10 times a second

        private static double StringHz(int s, double cents)
        {
            return GuitarTuning.Standard.OpenStringFrequency(s) * Math.Pow(2.0, cents / 1200.0);
        }

        /// <summary>Runs synthetic plucks through YIN and the smoother, like TunerPanel does.</summary>
        private static TunerSmoother RunYin(int stringIndex, double cents, int ticks, double[] timbre = null)
        {
            var yin = new YinPitchDetector();
            var smoother = new TunerSmoother(GuitarTuning.Standard);
            double hz = StringHz(stringIndex, cents);
            for (int i = 0; i < ticks; i++)
            {
                var buf = new float[4096];
                // Each window is a slice of a ringing string (different start phase every tick).
                TestAudio.AddTone(buf, -(int)(i * Tick * TestAudio.SampleRate), hz, 0.2, timbre, 0.3);
                smoother.Push(yin.Detect(buf, TestAudio.SampleRate), i * Tick);
            }
            return smoother;
        }

        [Theory]
        [InlineData(0, 12.0)]
        [InlineData(1, -20.0)]
        [InlineData(2, 8.0)]
        [InlineData(3, -9.0)]
        [InlineData(4, 30.0)]
        [InlineData(5, -15.0)]
        public void Reads_known_offsets_through_yin(int s, double cents)
        {
            foreach (var timbre in new[] { null, TestAudio.BrightTimbre })
            {
                var t = RunYin(s, cents, 8, timbre);
                Assert.True(t.IsStable);
                Assert.Equal(s, t.StringIndex);
                Assert.InRange(t.Cents, cents - 2, cents + 2);
                Assert.Equal(cents < 0 ? TunerAdvice.TuneUp : TunerAdvice.TuneDown, t.Advice);
                Assert.False(t.IsDone(s));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(5)]
        public void In_tune_string_is_ticked_off_after_the_hold(int s)
        {
            var t = RunYin(s, 2.0, 12);
            Assert.Equal(TunerAdvice.InTune, t.Advice);
            Assert.True(t.IsDone(s));
            Assert.Equal("Perfect", TunerSmoother.AdviceText(t.Advice));
        }

        [Fact]
        public void Shows_listening_until_readings_agree()
        {
            var t = new TunerSmoother(GuitarTuning.Standard);
            Assert.Equal(TunerAdvice.Listening, t.Advice);
            t.Push(StringHz(1, 10), 0.95, 0.0);
            Assert.Equal(TunerAdvice.Listening, t.Advice);
            t.Push(StringHz(1, 10), 0.95, 0.1);
            t.Push(StringHz(1, 10), 0.95, 0.2);
            Assert.Equal(TunerAdvice.TuneDown, t.Advice);
            Assert.Equal("Listening…", TunerSmoother.AdviceText(TunerAdvice.Listening));
        }

        [Fact]
        public void Octave_errors_do_not_move_the_needle()
        {
            var t = new TunerSmoother(GuitarTuning.Standard);
            for (int i = 0; i < 30; i++)
            {
                double hz = StringHz(0, -10);
                if (i % 3 == 1) hz *= 2;        // tracker jumped up an octave
                if (i % 7 == 4) hz *= 0.5;      // …or down one
                t.Push(hz, 0.9, i * Tick);
                if (i >= 3)
                {
                    Assert.Equal(0, t.StringIndex);
                    Assert.InRange(t.Cents, -12, -8);
                }
            }
            Assert.Equal(TunerAdvice.TuneUp, t.Advice);
        }

        [Fact]
        public void Low_E_heard_an_octave_up_still_reads_as_low_E()
        {
            // 164.8 Hz is 200 cents sharp of D, but exactly an octave above low E: low E is far likelier.
            var t = new TunerSmoother(GuitarTuning.Standard);
            for (int i = 0; i < 4; i++) t.Push(StringHz(0, 3) * 2, 0.9, i * Tick);
            Assert.Equal(0, t.StringIndex);
            Assert.InRange(t.Cents, 1, 5);
        }

        [Fact]
        public void One_stray_reading_from_another_string_is_ignored()
        {
            var t = new TunerSmoother(GuitarTuning.Standard);
            for (int i = 0; i < 5; i++) t.Push(StringHz(2, 4), 0.9, i * Tick);
            t.Push(StringHz(3, 0), 0.9, 0.5);
            Assert.Equal(2, t.StringIndex);
            Assert.True(t.IsStable);
        }

        [Fact]
        public void Switches_when_a_new_string_is_plucked()
        {
            var t = new TunerSmoother(GuitarTuning.Standard);
            for (int i = 0; i < 5; i++) t.Push(StringHz(2, 4), 0.9, i * Tick);
            for (int i = 5; i < 11; i++) t.Push(StringHz(4, -30), 0.9, i * Tick);
            Assert.Equal(4, t.StringIndex);
            Assert.True(t.IsStable);
            Assert.InRange(t.Cents, -32, -28);
        }

        [Fact]
        public void Noise_gate_ignores_low_confidence_and_goes_back_to_listening()
        {
            var t = new TunerSmoother(GuitarTuning.Standard);
            for (int i = 0; i < 5; i++) t.Push(StringHz(1, 0), 0.9, i * Tick);
            Assert.True(t.IsStable);
            for (int i = 5; i < 15; i++) t.Push(StringHz(5, 40), 0.3, i * Tick);   // unsure readings
            Assert.False(t.IsStable);
            Assert.Equal(TunerAdvice.Listening, t.Advice);
            Assert.Equal(-1, t.StringIndex);
        }

        [Fact]
        public void One_dropped_reading_does_not_reset_the_in_tune_hold()
        {
            var t = new TunerSmoother(GuitarTuning.Standard);
            for (int i = 0; i < 4; i++) t.Push(StringHz(1, 1), 0.9, i * Tick);
            t.Push(0, 0, 0.4);
            for (int i = 5; i < 9; i++) t.Push(StringHz(1, 1), 0.9, i * Tick);
            Assert.True(t.IsDone(1));
        }

        [Fact]
        public void All_six_strings_in_tune_reports_all_done_once_each()
        {
            var t = new TunerSmoother(GuitarTuning.Standard);
            int finished = 0;
            double time = 0;
            for (int s = 0; s < 6; s++)
            {
                for (int i = 0; i < 12; i++)
                {
                    t.Push(StringHz(s, 1.5), 0.92, time);
                    time += Tick;
                    if (t.JustFinishedString >= 0) { Assert.Equal(s, t.JustFinishedString); finished++; }
                }
                time += 1.0;   // gap between strings
            }
            Assert.Equal(6, finished);
            Assert.True(t.AllDone);
            Assert.Equal(6, t.DoneCount);
        }

        [Fact]
        public void Drop_D_tuning_targets_low_D()
        {
            var t = new TunerSmoother(GuitarTuning.DropD);
            double lowD = NoteMath.MidiToFrequency(38);
            for (int i = 0; i < 5; i++) t.Push(lowD, 0.9, i * Tick);
            Assert.Equal(0, t.StringIndex);
            Assert.InRange(t.Cents, -1, 1);
        }
    }
}

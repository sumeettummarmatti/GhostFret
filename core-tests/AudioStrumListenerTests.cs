using System;
using System.Collections.Generic;
using GhostFret.Core.Audio;
using GhostFret.Core.Feedback;
using GhostFret.Core.Music;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class AudioStrumListenerTests
    {
        private const int Sr = TestAudio.SampleRate;

        private sealed class Run
        {
            public readonly List<double> Onsets = new List<double>();
            public readonly List<ChordFeedback> Results = new List<ChordFeedback>();
            public readonly List<double> ResultTimes = new List<double>();
        }

        /// <summary>Feeds audio in uneven, frame-sized chunks with sample-accurate times (sample i at i / Sr).</summary>
        private static Run Feed(float[] audio, string target, int chunk = 700)
        {
            var run = new Run();
            var listener = new StrumListener(Sr, GuitarTuning.Standard) { TargetChord = target };
            listener.StrumOnset += t => run.Onsets.Add(t);
            listener.ChordHeard += (fb, t) => { run.Results.Add(fb); run.ResultTimes.Add(t); };
            int pos = 0, n = 0;
            while (pos < audio.Length)
            {
                int count = Math.Min(audio.Length - pos, chunk + (n++ % 3) * 97);   // uneven like real frames
                pos += count;
                listener.Push(audio, pos - count, count, (pos - 1) / (double)Sr);
            }
            return run;
        }

        private static float[] Room(double seconds, double noiseRms = 0.001)
        {
            var buf = new float[(int)(seconds * Sr)];
            TestAudio.AddNoise(buf, noiseRms, 9);
            return buf;
        }

        [Fact]
        public void One_strum_gives_one_onset_and_one_clean_result_with_accurate_time()
        {
            var audio = Room(1.5);
            int at = 24123;
            TestAudio.Strum(audio, at, "Em", 0.08, 0.003, TestAudio.BrightTimbre, 0);
            var run = Feed(audio, "Em");

            Assert.Single(run.Onsets);
            Assert.InRange(run.Onsets[0], at / (double)Sr - 0.004, at / (double)Sr + 0.004);
            Assert.Single(run.Results);
            Assert.Equal(FeedbackKind.Clean, run.Results[0].Kind);
            Assert.Equal(run.Onsets[0], run.ResultTimes[0], 6);
        }

        [Fact]
        public void Two_strums_give_two_results()
        {
            var audio = Room(2.0);
            TestAudio.StrumSequence(audio, new[] { 12000, 48000 }, "G", new[] { 0.08, 0.08 }, 0.003, null);
            var run = Feed(audio, "G");

            Assert.Equal(2, run.Onsets.Count);
            Assert.InRange(run.Onsets[0], 0.25 - 0.002, 0.25 + 0.008);
            Assert.InRange(run.Onsets[1], 1.0 - 0.002, 1.0 + 0.008);
            Assert.Equal(2, run.Results.Count);
            Assert.All(run.Results, r => Assert.Equal(FeedbackKind.Clean, r.Kind));
        }

        [Fact]
        public void Fast_strums_all_time_but_merge_into_one_result()
        {
            // Four strums 150 ms apart (eighth notes at 200 BPM), each damping the last.
            var audio = Room(1.5);
            var starts = new[] { 9600, 16800, 24000, 31200 };
            TestAudio.StrumSequence(audio, starts, "D", new[] { 0.08, 0.08, 0.08, 0.08 }, 0.002, TestAudio.BrightTimbre);
            var run = Feed(audio, "D");

            Assert.Equal(4, run.Onsets.Count);
            for (int i = 0; i < 4; i++) Assert.InRange(run.Onsets[i], starts[i] / (double)Sr - 0.002, starts[i] / (double)Sr + 0.008);
            // Strums closer than MinSecondsBetweenChordResults (0.35 s) share a result:
            // 0.20 s gets one, 0.35 and 0.50 merge into it, 0.65 s is far enough for its own.
            Assert.Equal(2, run.Results.Count);
            Assert.Equal(run.Onsets[0], run.ResultTimes[0], 6);
            Assert.Equal(run.Onsets[3], run.ResultTimes[1], 6);
        }

        [Fact]
        public void A_ringing_chord_does_not_trigger_extra_strums()
        {
            var audio = Room(3.0);
            TestAudio.Strum(audio, 12000, "Em", 0.1, 0.003, TestAudio.BrightTimbre, 0);
            var run = Feed(audio, "Em");
            Assert.Single(run.Onsets);
            Assert.Single(run.Results);
        }

        [Fact]
        public void Room_noise_alone_is_not_a_strum()
        {
            var run = Feed(Room(2.0, 0.002), "Em");
            Assert.Empty(run.Onsets);
            Assert.Empty(run.Results);
        }

        [Fact]
        public void Wrong_chord_strum_is_never_clean()
        {
            var audio = Room(1.2);
            TestAudio.Strum(audio, 10000, "G", 0.08, 0.003, TestAudio.BrightTimbre, 0);
            var run = Feed(audio, "Em");
            Assert.Single(run.Results);
            Assert.NotEqual(FeedbackKind.Clean, run.Results[0].Kind);
        }

        [Fact]
        public void Muted_G_string_is_named_through_the_whole_pipeline()
        {
            var audio = Room(1.2);
            TestAudio.Strum(audio, 10000, "Em", 0.08, 0.003, TestAudio.BrightTimbre, 0, 3);
            var run = Feed(audio, "Em");
            Assert.Single(run.Results);
            Assert.Equal(FeedbackKind.WeakString, run.Results[0].Kind);
            Assert.Equal(3, run.Results[0].StringIndex);
        }

        [Fact]
        public void Without_a_target_only_strum_times_are_reported()
        {
            var audio = Room(1.0);
            TestAudio.Strum(audio, 10000, "C", 0.08, 0.003, null, 0);
            var run = Feed(audio, null);
            Assert.Single(run.Onsets);
            Assert.Empty(run.Results);
        }

        [Fact]
        public void Soft_and_loud_strums_are_both_heard()
        {
            foreach (double amp in new[] { 0.02, 0.25 })
            {
                var audio = Room(1.2);
                TestAudio.Strum(audio, 10000, "Am", amp, 0.003, null, 0);
                var run = Feed(audio, "Am", 512);
                Assert.Single(run.Onsets);
            }
        }
    }
}

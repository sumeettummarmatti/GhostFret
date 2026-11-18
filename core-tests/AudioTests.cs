using GhostFret.Core.Audio;
using GhostFret.Core.Feedback;
using GhostFret.Core.Music;
using Xunit;

namespace GhostFret.Core.Tests
{
    public class AudioTests
    {
        [Theory]
        [InlineData(82.41)]
        [InlineData(110.0)]
        [InlineData(329.63)]
        public void Yin_detects_string_pitches(double hz)
        {
            var result = new YinPitchDetector().Detect(TestAudio.Tone(hz, 4096), TestAudio.SampleRate);
            Assert.True(result.HasPitch);
            Assert.InRange(NoteMath.CentsBetween(result.Frequency, hz), -10, 10);
        }

        [Theory]
        [InlineData("Em")]
        [InlineData("G")]
        [InlineData("C")]
        [InlineData("D")]
        public void Clean_chord_is_recognised(string chord)
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            var feedback = analyzer.Analyze(TestAudio.Chord(chord, 8192), TestAudio.SampleRate, chord);
            Assert.Equal(FeedbackKind.Clean, feedback.Kind);
        }

        [Fact]
        public void Muted_G_string_in_Em_is_named()
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            var feedback = analyzer.Analyze(TestAudio.Chord("Em", 8192, 3), TestAudio.SampleRate, "Em");
            Assert.Equal(FeedbackKind.WeakString, feedback.Kind);
            Assert.Equal(3, feedback.StringIndex);
        }

        [Fact]
        public void Silence_is_too_quiet()
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            Assert.Equal(FeedbackKind.TooQuiet, analyzer.Analyze(new float[8192], TestAudio.SampleRate, "Em").Kind);
        }
    }
}

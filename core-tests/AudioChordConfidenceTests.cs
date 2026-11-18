using GhostFret.Core.Audio;
using GhostFret.Core.Feedback;
using GhostFret.Core.Music;
using Xunit;

namespace GhostFret.Core.Tests
{
    /// <summary>
    /// CLAUDE.md rule 5: when unsure, stay quiet or give a general tip; never tell the player something wrong.
    /// These tests strum synthetic chords (plain and brighter, guitar-like timbres) and check every claim.
    /// </summary>
    public class AudioChordConfidenceTests
    {
        private static float[] Strum(string chord, double[] timbre = null, double detuneCents = 0, double noiseRms = 0, params int[] mutes)
        {
            var buf = new float[8192];
            TestAudio.Strum(buf, 0, chord, 0.08, 0.004, timbre, detuneCents, mutes);
            if (noiseRms > 0) TestAudio.AddNoise(buf, noiseRms, 3);
            return buf;
        }

        public static TheoryData<string> Chords()
        {
            var d = new TheoryData<string>();
            foreach (var c in ChordLibrary.TeachingOrder) d.Add(c);
            return d;
        }

        [Theory]
        [MemberData(nameof(Chords))]
        public void Every_clean_chord_is_recognised_in_both_timbres_and_slightly_out_of_tune(string chord)
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            Assert.Equal(FeedbackKind.Clean, analyzer.Analyze(Strum(chord), TestAudio.SampleRate, chord).Kind);
            Assert.Equal(FeedbackKind.Clean, analyzer.Analyze(Strum(chord, TestAudio.BrightTimbre), TestAudio.SampleRate, chord).Kind);
            Assert.Equal(FeedbackKind.Clean, analyzer.Analyze(Strum(chord, TestAudio.BrightTimbre, 12, 0.005), TestAudio.SampleRate, chord).Kind);
        }

        [Theory]
        [MemberData(nameof(Chords))]
        public void Playing_another_chord_is_never_called_clean(string target)
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            foreach (var played in ChordLibrary.TeachingOrder)
            {
                if (played == target) continue;
                foreach (var timbre in new[] { null, TestAudio.BrightTimbre })
                {
                    var fb = analyzer.Analyze(Strum(played, timbre), TestAudio.SampleRate, target);
                    Assert.True(fb.Kind == FeedbackKind.WrongChord || fb.Kind == FeedbackKind.NotSure,
                        "target " + target + ", played " + played + " gave " + fb.Kind);
                    // If we name a chord, it must be the one actually played.
                    if (fb.Kind == FeedbackKind.WrongChord) Assert.Equal(played, fb.HeardChord);
                }
            }
        }

        [Fact]
        public void G_played_for_Em_is_named_as_G()
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            var fb = analyzer.Analyze(Strum("G", TestAudio.BrightTimbre), TestAudio.SampleRate, "Em");
            Assert.Equal(FeedbackKind.WrongChord, fb.Kind);
            Assert.Equal("G", fb.HeardChord);
            Assert.Contains("sounds like G", fb.Message);
        }

        [Fact]
        public void E_major_for_Em_is_not_called_muted_or_clean()
        {
            // One finger too many on the G string: the G string sounds G#, so "G string is muted" would be wrong.
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            foreach (var timbre in new[] { null, TestAudio.BrightTimbre })
            {
                var fb = analyzer.Analyze(Strum("E", timbre), TestAudio.SampleRate, "Em");
                Assert.NotEqual(FeedbackKind.Clean, fb.Kind);
                Assert.NotEqual(FeedbackKind.WeakString, fb.Kind);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("bright")]
        [InlineData("bright-detuned-noisy")]
        public void Em_with_muted_G_string_names_the_G_string(string variant)
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            double[] timbre = variant == null ? null : TestAudio.BrightTimbre;
            bool rough = variant == "bright-detuned-noisy";
            var fb = analyzer.Analyze(Strum("Em", timbre, rough ? 12 : 0, rough ? 0.005 : 0, 3), TestAudio.SampleRate, "Em");
            Assert.Equal(FeedbackKind.WeakString, fb.Kind);
            Assert.Equal(3, fb.StringIndex);
            Assert.Contains("G string", fb.Message);
        }

        [Theory]
        [MemberData(nameof(Chords))]
        public void A_muted_string_is_never_blamed_on_a_different_string(string chord)
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            var shape = ChordLibrary.Get(chord);
            for (int s = 0; s < 6; s++)
            {
                if (!shape.IsStringPlayed(s)) continue;
                foreach (var timbre in new[] { null, TestAudio.BrightTimbre })
                {
                    var fb = analyzer.Analyze(Strum(chord, timbre, 0, 0, s), TestAudio.SampleRate, chord);
                    if (fb.Kind == FeedbackKind.WeakString) Assert.Equal(s, fb.StringIndex);
                    Assert.NotEqual(FeedbackKind.WrongChord, fb.Kind);
                    Assert.NotEqual(FeedbackKind.UnwantedString, fb.Kind);
                }
            }
        }

        [Fact]
        public void Silence_is_too_quiet()
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            Assert.Equal(FeedbackKind.TooQuiet, analyzer.Analyze(new float[8192], TestAudio.SampleRate, "Em").Kind);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void Noise_makes_no_claim(int seed)
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            var quiet = new float[8192];
            TestAudio.AddNoise(quiet, 0.003, seed);
            Assert.Equal(FeedbackKind.TooQuiet, analyzer.Analyze(quiet, TestAudio.SampleRate, "Em").Kind);

            var loud = new float[8192];
            TestAudio.AddNoise(loud, 0.05, seed);
            var fb = analyzer.Analyze(loud, TestAudio.SampleRate, "Em");
            Assert.True(fb.Kind == FeedbackKind.NotSure || fb.Kind == FeedbackKind.TooQuiet, "noise gave " + fb.Kind);
        }

        [Fact]
        public void A_single_note_is_not_a_chord()
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            var buf = TestAudio.Tone(NoteMath.MidiToFrequency(52), 8192, 0.2);   // E3 alone
            var fb = analyzer.Analyze(buf, TestAudio.SampleRate, "Em");
            Assert.NotEqual(FeedbackKind.Clean, fb.Kind);
            Assert.NotEqual(FeedbackKind.WrongChord, fb.Kind);
        }

        [Fact]
        public void Not_sure_message_is_a_general_tip()
        {
            var fb = ChordFeedback.NotSure("G", 0.5);
            Assert.Equal(FeedbackKind.NotSure, fb.Kind);
            Assert.Equal(-1, fb.StringIndex);
            Assert.DoesNotContain("Wrong", fb.Message);
        }

        [Fact]
        public void Analyze_with_offset_matches_plain_call()
        {
            var analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            var chord = Strum("C");
            var padded = new float[8192 + 500];
            System.Array.Copy(chord, 0, padded, 500, 8192);
            Assert.Equal(analyzer.Analyze(chord, TestAudio.SampleRate, "C").Kind,
                         analyzer.Analyze(padded, 500, 8192, TestAudio.SampleRate, "C").Kind);
        }
    }
}

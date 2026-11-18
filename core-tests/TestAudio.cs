using System;
using GhostFret.Core.Music;

namespace GhostFret.Core.Tests
{
    /// <summary>Makes simple synthetic guitar-like sounds for tests.</summary>
    public static class TestAudio
    {
        public const int SampleRate = 48000;

        public static float[] Tone(double frequency, int length, double amplitude = 0.3)
        {
            var s = new float[length];
            AddTone(s, frequency, amplitude);
            return s;
        }

        /// <summary>A tone with 5 decaying harmonics.</summary>
        public static void AddTone(float[] buffer, double frequency, double amplitude)
        {
            AddTone(buffer, 0, frequency, amplitude, null);
        }

        /// <summary>
        /// A plucked-string-like tone starting at startSample (with a 2 ms attack).
        /// harmonicAmps: amplitude of harmonics 1..n (default 1/h for 5 harmonics).
        /// </summary>
        public static void AddTone(float[] buffer, int startSample, double frequency, double amplitude, double[] harmonicAmps, double decayPerSecond = 1.5)
        {
            int attack = SampleRate / 500;
            for (int i = Math.Max(0, startSample); i < buffer.Length; i++)
            {
                int n = i - startSample;
                double t = (double)n / SampleRate;
                double v = 0;
                int hCount = harmonicAmps != null ? harmonicAmps.Length : 5;
                for (int h = 1; h <= hCount; h++)
                {
                    double a = harmonicAmps != null ? harmonicAmps[h - 1] : 1.0 / h;
                    if (frequency * h > SampleRate / 2.0) break;
                    v += a * Math.Sin(2 * Math.PI * frequency * h * t);
                }
                double env = Math.Exp(-decayPerSecond * t) * Math.Min(1.0, (double)n / attack);
                buffer[i] += (float)(amplitude * v * env);
            }
        }

        /// <summary>Strums a chord shape; strings listed in muteStrings are left silent.</summary>
        public static float[] Chord(string chordName, int length, params int[] muteStrings)
        {
            var buffer = new float[length];
            Strum(buffer, 0, chordName, 0.08, 0, null, 0, muteStrings);
            return buffer;
        }

        /// <summary>
        /// Adds a down-strum (low E first) at startSample. strumSpreadSeconds = time between strings.
        /// detuneCents is applied to every string; harmonicAmps sets the timbre (null = 1/h).
        /// </summary>
        public static void Strum(float[] buffer, int startSample, string chordName, double amplitude,
                                 double strumSpreadSeconds, double[] harmonicAmps, double detuneCents, params int[] muteStrings)
        {
            var shape = ChordLibrary.Get(chordName);
            int k = 0;
            for (int s = 0; s < 6; s++)
            {
                if (Array.IndexOf(muteStrings, s) >= 0) continue;
                int midi = shape.SoundingMidi(s, GuitarTuning.Standard);
                if (midi < 0) continue;
                double f = NoteMath.MidiToFrequency(midi + detuneCents / 100.0);
                AddTone(buffer, startSample + (int)(k * strumSpreadSeconds * SampleRate), f, amplitude, harmonicAmps);
                k++;
            }
        }

        /// <summary>
        /// Several strums of one chord, like a real player: each new strum damps the strings that are
        /// still ringing (3 ms fade) and re-excites them. amplitudes[i] lets up-strums be softer.
        /// </summary>
        public static void StrumSequence(float[] buffer, int[] starts, string chordName, double[] amplitudes, double strumSpreadSeconds, double[] harmonicAmps)
        {
            int fade = SampleRate * 3 / 1000;
            for (int k = 0; k < starts.Length; k++)
            {
                var one = new float[buffer.Length];
                Strum(one, starts[k], chordName, amplitudes[k], strumSpreadSeconds, harmonicAmps, 0);
                int end = k + 1 < starts.Length ? starts[k + 1] : buffer.Length;
                for (int i = starts[k]; i < buffer.Length; i++)
                {
                    double g = i < end ? 1.0 : Math.Max(0.0, 1.0 - (double)(i - end) / fade);
                    if (g <= 0) break;
                    buffer[i] += (float)(one[i] * g);
                }
            }
        }

        /// <summary>White noise (deterministic seed).</summary>
        public static void AddNoise(float[] buffer, double rms, int seed = 1)
        {
            var rng = new Random(seed);
            double amp = rms * Math.Sqrt(3.0);   // uniform in [−a, a] has RMS a/√3
            for (int i = 0; i < buffer.Length; i++) buffer[i] += (float)(amp * (rng.NextDouble() * 2 - 1));
        }

        /// <summary>A brighter, more guitar-like timbre: strong 2nd and 3rd harmonics.</summary>
        public static readonly double[] BrightTimbre = { 1.0, 0.8, 0.6, 0.35, 0.25, 0.15, 0.1 };
    }
}

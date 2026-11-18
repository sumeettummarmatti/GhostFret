using System;
using GhostFret.Core.Music;
using GhostFret.Core.Strum;

namespace GhostFret.Core.AirMode
{
    /// <summary>
    /// Karplus-Strong plucked-string synth: Air mode's guitar sound, made in code (no audio assets).
    /// A strum plays each sounding string a few milliseconds apart in the strum direction
    /// (down = low E first, up = high E first); muted strings are skipped.
    /// A small all-pass filter fine-tunes the loop so each string lands on its real pitch.
    /// </summary>
    public sealed class PluckSynth
    {
        public int SampleRate = 48000;
        public float Seconds = 2.0f;             // clip length; strings ring out naturally inside it
        public float StaggerSeconds = 0.012f;    // gap between strings in a strum
        public float LoopGain = 0.996f;          // per-period energy kept; lower = shorter ring
        public float Brightness = 0.55f;         // 0 = dull thump, 1 = bright noise burst
        public float StringGain = 0.32f;         // level of one string before mixing
        public float PeakLimit = 0.85f;          // the mix is scaled down if it would go above this
        public float UpStrumLowStringGain = 0.6f; // up-strums catch the low strings lightly
        public int Seed = 1234;                  // deterministic: same chord = same sound

        /// <summary>Renders a strummed chord, mono, SampleRate Hz, exactly Seconds long.</summary>
        public float[] RenderChord(ChordShape shape, StrumDirection direction, GuitarTuning tuning = null, float strength = 1f)
        {
            if (tuning == null) tuning = GuitarTuning.Standard;
            var output = new float[Length()];
            if (shape == null || shape.frets == null) return output;

            bool up = direction == StrumDirection.Up;
            int order = 0;
            for (int k = 0; k < 6; k++)
            {
                int s = up ? 5 - k : k;
                int midi = shape.SoundingMidi(s, tuning);
                if (midi < 0) continue; // muted string: skipped, and it doesn't take a slot in the stagger

                float gain = StringGain * Clamp(strength, 0.2f, 1.5f);
                if (up && s < 3) gain *= UpStrumLowStringGain;
                int start = (int)Math.Round(order * StaggerSeconds * SampleRate);
                AddString(output, start, NoteMath.MidiToFrequency(midi), gain, Seed + s * 101 + (up ? 7 : 0));
                order++;
            }

            Finish(output);
            return output;
        }

        /// <summary>One plucked string at a frequency (used by tests and for single-note sounds).</summary>
        public float[] RenderString(double frequency, float gain = 0.5f)
        {
            var output = new float[Length()];
            AddString(output, 0, frequency, gain, Seed);
            Finish(output);
            return output;
        }

        private int Length() { return Math.Max(1, (int)Math.Round(Seconds * SampleRate)); }

        private void AddString(float[] output, int start, double frequency, float gain, int seed)
        {
            if (frequency <= 0 || start >= output.Length) return;

            // Loop delay = buffer (N) + averaging filter (0.5) + all-pass (delta).
            double period = SampleRate / frequency;
            int n = Math.Max(2, (int)Math.Floor(period - 0.5 - 0.1));
            double delta = period - 0.5 - n;                  // in [0.1, 1.1): keeps the all-pass well behaved
            float c = (float)((1.0 - delta) / (1.0 + delta));

            // Excitation: a soft noise burst (one-pole low-pass sets the brightness), DC removed.
            var buffer = new float[n];
            var rng = new Random(seed);
            float smooth = 0f;
            float alpha = Clamp(Brightness, 0.05f, 1f);
            double mean = 0;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                smooth += alpha * (white - smooth);
                buffer[i] = smooth;
                mean += smooth;
            }
            mean /= n;
            float peak = 1e-6f;
            for (int i = 0; i < n; i++) { buffer[i] -= (float)mean; peak = Math.Max(peak, Math.Abs(buffer[i])); }
            for (int i = 0; i < n; i++) buffer[i] *= gain / peak;

            int idx = 0;
            float prev = 0f, apIn = 0f, apOut = 0f;
            for (int t = start; t < output.Length; t++)
            {
                float x = buffer[idx];
                float lp = 0.5f * (x + prev);
                prev = x;
                float ap = c * lp + apIn - c * apOut;
                apIn = lp;
                apOut = ap;
                buffer[idx] = ap * LoopGain;
                idx++;
                if (idx == n) idx = 0;
                output[t] += x;
            }
        }

        private void Finish(float[] output)
        {
            float peak = 0f;
            for (int i = 0; i < output.Length; i++)
            {
                float v = output[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) { output[i] = 0f; continue; }
                peak = Math.Max(peak, Math.Abs(v));
            }
            if (peak > PeakLimit)
            {
                float scale = PeakLimit / peak;
                for (int i = 0; i < output.Length; i++) output[i] *= scale;
            }

            // Short fades so the clip never starts or ends with a click.
            int fadeIn = Math.Min(24, output.Length / 4);
            for (int i = 0; i < fadeIn; i++) output[i] *= i / (float)fadeIn;
            int fadeOut = Math.Min(SampleRate / 20, output.Length / 4);
            for (int i = 0; i < fadeOut; i++) output[output.Length - 1 - i] *= i / (float)fadeOut;
        }

        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}

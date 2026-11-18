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
            for (int i = 0; i < buffer.Length; i++)
            {
                double t = (double)i / SampleRate;
                double v = 0;
                for (int h = 1; h <= 5; h++) v += Math.Sin(2 * Math.PI * frequency * h * t) / h;
                buffer[i] += (float)(amplitude * v * Math.Exp(-1.5 * t));
            }
        }

        /// <summary>Strums a chord shape; strings listed in muteStrings are left silent.</summary>
        public static float[] Chord(string chordName, int length, params int[] muteStrings)
        {
            var shape = ChordLibrary.Get(chordName);
            var buffer = new float[length];
            for (int s = 0; s < 6; s++)
            {
                if (Array.IndexOf(muteStrings, s) >= 0) continue;
                int midi = shape.SoundingMidi(s, GuitarTuning.Standard);
                if (midi < 0) continue;
                AddTone(buffer, NoteMath.MidiToFrequency(midi), 0.08);
            }
            return buffer;
        }
    }
}

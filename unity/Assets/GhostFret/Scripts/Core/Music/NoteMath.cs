using System;

namespace GhostFret.Core.Music
{
    /// <summary>
    /// Small helpers for converting between notes, MIDI numbers and frequencies.
    /// Equal temperament, A4 = 440 Hz.
    /// </summary>
    public static class NoteMath
    {
        public const double A4Frequency = 440.0;
        public const int A4Midi = 69;

        public static readonly string[] PitchClassNames =
        {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
        };

        public static double MidiToFrequency(double midi)
        {
            return A4Frequency * Math.Pow(2.0, (midi - A4Midi) / 12.0);
        }

        public static double FrequencyToMidi(double frequency)
        {
            return A4Midi + 12.0 * Math.Log(frequency / A4Frequency, 2.0);
        }

        /// <summary>Pitch class 0..11 (C = 0) for a MIDI note number.</summary>
        public static int PitchClass(int midi)
        {
            return ((midi % 12) + 12) % 12;
        }

        /// <summary>How many cents frequency is above (+) or below (−) target.</summary>
        public static double CentsBetween(double frequency, double target)
        {
            return 1200.0 * Math.Log(frequency / target, 2.0);
        }

        /// <summary>Returns 0..11 for a pitch class name like "F#", or −1 if unknown.</summary>
        public static int PitchClassIndex(string name)
        {
            for (int i = 0; i < PitchClassNames.Length; i++)
            {
                if (PitchClassNames[i] == name) return i;
            }
            return -1;
        }
    }
}

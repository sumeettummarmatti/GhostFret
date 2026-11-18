using System;

namespace GhostFret.Core.Music
{
    /// <summary>
    /// A guitar tuning: the MIDI note of each open string, low E (index 0) to high E (index 5).
    /// </summary>
    public sealed class GuitarTuning
    {
        public readonly string Name;
        public readonly int[] OpenStringMidi;

        public static readonly string[] StringNames = { "low E", "A", "D", "G", "B", "high E" };

        public static readonly GuitarTuning Standard = new GuitarTuning("Standard", new[] { 40, 45, 50, 55, 59, 64 });
        public static readonly GuitarTuning DropD = new GuitarTuning("Drop D", new[] { 38, 45, 50, 55, 59, 64 });
        public static readonly GuitarTuning HalfStepDown = new GuitarTuning("Half step down", new[] { 39, 44, 49, 54, 58, 63 });

        public GuitarTuning(string name, int[] openStringMidi)
        {
            if (openStringMidi == null || openStringMidi.Length != 6)
                throw new ArgumentException("A guitar tuning needs exactly 6 strings.");
            Name = name;
            OpenStringMidi = openStringMidi;
        }

        public double OpenStringFrequency(int stringIndex)
        {
            return NoteMath.MidiToFrequency(OpenStringMidi[stringIndex]);
        }

        /// <summary>
        /// Finds the open string closest to a detected frequency.
        /// Returns the string index and how far off it is in cents (+ = sharp, − = flat).
        /// </summary>
        public int NearestString(double frequency, out double cents)
        {
            int best = 0;
            double bestAbs = double.MaxValue;
            cents = 0;
            for (int s = 0; s < 6; s++)
            {
                double c = NoteMath.CentsBetween(frequency, OpenStringFrequency(s));
                if (Math.Abs(c) < bestAbs)
                {
                    bestAbs = Math.Abs(c);
                    best = s;
                    cents = c;
                }
            }
            return best;
        }
    }
}

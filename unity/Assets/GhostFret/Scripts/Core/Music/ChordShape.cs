using System.Collections.Generic;

namespace GhostFret.Core.Music
{
    /// <summary>
    /// How to play one chord.
    /// Arrays go from low E (index 0) to high E (index 5).
    /// frets:   −1 = don't play this string, 0 = open string, 1+ = press this fret.
    /// fingers: 0 = no finger, 1 = index, 2 = middle, 3 = ring, 4 = pinky.
    /// </summary>
    [System.Serializable]
    public class ChordShape
    {
        public string name;
        public int[] frets;
        public int[] fingers;
        /// <summary>Optional: shown to the player, e.g. "simplified, 3 fingers".</summary>
        public string note;

        public ChordShape() { }

        public ChordShape(string name, int[] frets, int[] fingers, string note = "")
        {
            this.name = name;
            this.frets = frets;
            this.fingers = fingers;
            this.note = note;
        }

        public bool IsStringPlayed(int stringIndex)
        {
            return frets[stringIndex] >= 0;
        }

        /// <summary>MIDI note sounded by a string, or −1 if the string is not played.</summary>
        public int SoundingMidi(int stringIndex, GuitarTuning tuning)
        {
            if (!IsStringPlayed(stringIndex)) return -1;
            return tuning.OpenStringMidi[stringIndex] + frets[stringIndex];
        }

        /// <summary>The set of pitch classes (0..11) this chord sounds.</summary>
        public HashSet<int> PitchClasses(GuitarTuning tuning)
        {
            var set = new HashSet<int>();
            for (int s = 0; s < 6; s++)
            {
                int midi = SoundingMidi(s, tuning);
                if (midi >= 0) set.Add(NoteMath.PitchClass(midi));
            }
            return set;
        }

        public int PlayedStringCount()
        {
            int count = 0;
            for (int s = 0; s < 6; s++) if (IsStringPlayed(s)) count++;
            return count;
        }
    }
}

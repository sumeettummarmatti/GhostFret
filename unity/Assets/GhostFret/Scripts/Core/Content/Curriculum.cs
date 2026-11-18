using System.Collections.Generic;

namespace GhostFret.Core.Content
{
    [System.Serializable]
    public class CurriculumDay
    {
        public int day;
        public string title;
        public string[] newChords;
        public string[] strumPattern;
        public string[] changeDrills;   // e.g. ["Em-D"]
        public string[] songIds;
        public string goal;
    }

    [System.Serializable]
    public class Curriculum
    {
        public CurriculumDay[] days;

        /// <summary>All chords taught up to and including a day.</summary>
        public HashSet<string> ChordsLearnedBy(int day)
        {
            var set = new HashSet<string>();
            foreach (var d in days)
            {
                if (d.day > day) continue;
                if (d.newChords != null) foreach (var c in d.newChords) set.Add(c);
            }
            return set;
        }
    }
}

using System.Collections.Generic;
using GhostFret.Core.Content;

namespace GhostFret.Core.Lesson
{
    /// <summary>
    /// Reads the curriculum for "today": what to teach, which drill pair, which song,
    /// and the words for Welcome and WrapUp. Pure so it can be tested.
    /// </summary>
    public static class DayPlanner
    {
        public static CurriculumDay Day(Curriculum c, int day)
        {
            if (c == null || c.days == null || c.days.Length == 0) return null;
            CurriculumDay best = c.days[0];
            foreach (CurriculumDay d in c.days)
            {
                if (d.day == day) return d;
                if (d.day < day && d.day > best.day) best = d;   // past the end: stay on the last day
            }
            return best;
        }

        public static int LastDay(Curriculum c)
        {
            int last = 1;
            if (c != null && c.days != null) foreach (CurriculumDay d in c.days) if (d.day > last) last = d.day;
            return last;
        }

        public static string[] NewChords(Curriculum c, int day)
        {
            CurriculumDay d = Day(c, day);
            return d != null && d.newChords != null ? d.newChords : new string[0];
        }

        /// <summary>Most recent earlier day that taught new chords (for a quick review).</summary>
        public static string[] PreviousDayChords(Curriculum c, int day)
        {
            if (c == null || c.days == null) return new string[0];
            CurriculumDay best = null;
            foreach (CurriculumDay d in c.days)
                if (d.day < day && d.newChords != null && d.newChords.Length > 0 && (best == null || d.day > best.day)) best = d;
            return best != null ? best.newChords : new string[0];
        }

        public static HashSet<string> LearnedBefore(Curriculum c, int day)
        {
            return c != null && c.days != null ? c.ChordsLearnedBy(day - 1) : new HashSet<string>();
        }

        public static HashSet<string> LearnedBy(Curriculum c, int day)
        {
            var set = c != null && c.days != null ? c.ChordsLearnedBy(day) : new HashSet<string>();
            if (set.Count == 0) set.Add("Em");
            return set;
        }

        /// <summary>Today's drill pair, or the latest earlier one; null if none yet (e.g. Day 1).</summary>
        public static string DrillPair(Curriculum c, int day)
        {
            if (c == null || c.days == null) return null;
            CurriculumDay best = null;
            foreach (CurriculumDay d in c.days)
                if (d.day <= day && d.changeDrills != null && d.changeDrills.Length > 0 && (best == null || d.day > best.day)) best = d;
            return best != null ? best.changeDrills[0] : null;
        }

        /// <summary>"Em-D" → ("Em", "D"). False if the text isn't a pair.</summary>
        public static bool TryParsePair(string pair, out string a, out string b)
        {
            a = b = null;
            if (string.IsNullOrEmpty(pair)) return false;
            int dash = pair.IndexOf('-');
            if (dash <= 0 || dash >= pair.Length - 1) return false;
            a = pair.Substring(0, dash).Trim();
            b = pair.Substring(dash + 1).Trim();
            return a.Length > 0 && b.Length > 0 && a != b;
        }

        /// <summary>
        /// Today's song: the day's own song if unlocked, else the latest-day unlocked song, else the easiest unlocked.
        /// Null if nothing is unlocked.
        /// </summary>
        public static SongChart PickSong(Curriculum c, int day, IList<SongChart> songs, ICollection<string> learned)
        {
            if (songs == null || songs.Count == 0) return null;
            var unlocked = new List<SongChart>();
            foreach (SongChart s in songs) if (s != null && SongValidator.IsUnlocked(s, learned)) unlocked.Add(s);
            if (unlocked.Count == 0) return null;

            if (c != null && c.days != null)
            {
                for (int d = day; d >= 1; d--)
                {
                    CurriculumDay cd = Day(c, d);
                    if (cd == null || cd.day != d || cd.songIds == null) continue;
                    foreach (string id in cd.songIds)
                        foreach (SongChart s in unlocked) if (s.id == id) return s;
                }
            }
            SongChart easiest = unlocked[0];
            foreach (SongChart s in unlocked) if (s.difficulty < easiest.difficulty) easiest = s;
            return easiest;
        }

        /// <summary>"Day 2: Two chords, one song".</summary>
        public static string DayLine(Curriculum c, int day)
        {
            CurriculumDay d = Day(c, day);
            if (d == null) return "Day " + day;
            return "Day " + d.day + ": " + d.title;
        }

        public static string TomorrowGoal(Curriculum c, int nextDay)
        {
            CurriculumDay d = Day(c, nextDay);
            if (d == null || string.IsNullOrEmpty(d.goal)) return "Tomorrow: a little more practice.";
            return "Tomorrow: " + d.goal;
        }

        /// <summary>Should a guided session include the strum / drill / song steps today?</summary>
        public static SessionOptions GuidedOptions(Curriculum c, int day, bool fitted, bool songAvailable)
        {
            CurriculumDay d = Day(c, day);
            return new SessionOptions
            {
                FretboardFitted = fitted,
                IncludeStrum = d != null && d.strumPattern != null && d.strumPattern.Length >= 2,
                IncludeDrill = DayHasOwnDrill(d),
                IncludeSong = songAvailable
            };
        }

        private static bool DayHasOwnDrill(CurriculumDay d)
        {
            if (d == null || d.changeDrills == null || d.changeDrills.Length == 0) return false;
            string a, b;
            return TryParsePair(d.changeDrills[0], out a, out b);
        }
    }
}

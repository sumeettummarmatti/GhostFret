using System;

namespace GhostFret.Core.Progress
{
    /// <summary>
    /// What happened in this session, for the kind wrap-up card (DESIGN.md §5.2): streak, today's best,
    /// tomorrow's goal. Live counts only; nothing here is about mic or hand data.
    /// </summary>
    public sealed class SessionSummary
    {
        public int CleanChords;
        public string NewChordLearned;      // e.g. "Em" (null if none)
        public string DrillPair;
        public int DrillScore = -1;         // −1 = no drill today
        public bool DrillPersonalBest;
        public string SongTitle;
        public float SongCleanRate = -1f;   // −1 = no song today
        public int StrumOnTimePercent = -1;
        public bool AirMode;

        public void Clear()
        {
            CleanChords = 0; NewChordLearned = null; DrillPair = null; DrillScore = -1; DrillPersonalBest = false;
            SongTitle = null; SongCleanRate = -1f; StrumOnTimePercent = -1; AirMode = false;
        }

        public static string StreakLine(int streakDays)
        {
            if (streakDays <= 1) return "Day 1 of your streak!";
            return streakDays + "-day streak!";
        }

        /// <summary>One line, the best thing from today. Praise specific things.</summary>
        public string BestLine()
        {
            if (DrillPersonalBest && DrillScore > 0) return "New personal best: " + DrillScore + " clean changes " + DrillPair + "!";
            if (!string.IsNullOrEmpty(NewChordLearned)) return "You learned " + NewChordLearned + " today!";
            if (SongCleanRate >= 0 && !string.IsNullOrEmpty(SongTitle))
                return "You played " + SongTitle + ": " + (int)Math.Round(SongCleanRate * 100) + "% clean.";
            if (DrillScore > 0) return DrillScore + " clean changes " + DrillPair + ".";
            if (StrumOnTimePercent >= 0) return StrumOnTimePercent + "% of strums on time.";
            if (CleanChords > 0) return CleanChords + " clean chords today.";
            if (AirMode) return "You practised shapes and rhythm in Air mode.";
            return "You showed up and practised. That's what counts.";
        }
    }

    /// <summary>Suggests a break once after ~15 minutes. A practice tip only, never a health claim.</summary>
    public sealed class BreakReminder
    {
        public double AfterSeconds = 15 * 60;
        public const string Message = "Nice session! Practice tip: a short break, then come back anytime.";

        private double _start;
        private bool _shown;

        public BreakReminder(double now = 0) { Reset(now); }
        public void Reset(double now) { _start = now; _shown = false; }
        public double Elapsed(double now) { return now - _start; }

        /// <summary>True exactly once, when the session passes AfterSeconds.</summary>
        public bool ShouldSuggest(double now)
        {
            if (_shown || now - _start < AfterSeconds) return false;
            _shown = true;
            return true;
        }
    }
}

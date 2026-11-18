using System;
using System.Collections.Generic;
using GhostFret.Core.Timing;

namespace GhostFret.Core.Content
{
    /// <summary>What the app keeps after a song (saved on device only by ProgressStore).</summary>
    [Serializable]
    public class SongResult
    {
        public string songId;
        public double cleanRate;      // 0..1 clean chords
        public double timingRate;     // 0..1 strums on time
        public int longestStreak;     // clean chords in a row
        public double tempoFactor;    // tempo factor for the NEXT run (after AdaptiveTempo.RecordRun)
        public int strums;
        public bool looped;           // true if this was a section loop rather than the whole song
    }

    /// <summary>
    /// Scores one run of a song: clean chord %, timing %, longest streak (DESIGN.md §7.5).
    /// A chord that passes with no strum at all counts as one not-clean attempt, so stopping
    /// can't inflate the score, but it never adds a harsh message: the summary stays kind.
    /// </summary>
    public sealed class SongRun
    {
        public int Strums { get; private set; }
        public int CleanStrums { get; private set; }
        public int SilentChords { get; private set; }
        public int TimedStrums { get; private set; }
        public int OnTimeStrums { get; private set; }
        public int EarlyStrums { get; private set; }
        public int LateStrums { get; private set; }
        public int CurrentStreak { get; private set; }
        public int LongestStreak { get; private set; }

        private readonly Dictionary<string, int> _attempts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _misses = new Dictionary<string, int>();

        public int Attempts { get { return Strums + SilentChords; } }
        public double CleanRate { get { return Attempts == 0 ? 0 : CleanStrums / (double)Attempts; } }
        public double TimingRate { get { return TimedStrums == 0 ? 0 : OnTimeStrums / (double)TimedStrums; } }

        /// <summary>
        /// One strum. clean = the audio said it was the expected chord, cleanly.
        /// timing = null for an extra strum on a rest (counted for chords, not for timing).
        /// Returns true if this strum extended the streak.
        /// </summary>
        public bool RecordStrum(string expectedChord, bool clean, TimingVerdict? timing)
        {
            Strums++;
            Count(_attempts, expectedChord);
            if (clean)
            {
                CleanStrums++;
                CurrentStreak++;
                if (CurrentStreak > LongestStreak) LongestStreak = CurrentStreak;
            }
            else
            {
                Count(_misses, expectedChord);
                CurrentStreak = 0;
            }

            if (timing.HasValue)
            {
                TimedStrums++;
                if (timing.Value == TimingVerdict.OnTime) OnTimeStrums++;
                else if (timing.Value == TimingVerdict.Early) EarlyStrums++;
                else LateStrums++;
            }
            return clean;
        }

        /// <summary>A chord went by without any strum.</summary>
        public void RecordSilentChord(string chord)
        {
            SilentChords++;
            Count(_attempts, chord);
            Count(_misses, chord);
            CurrentStreak = 0;
        }

        /// <summary>Small celebrations at 5, 10, 20, 30… clean chords in a row.</summary>
        public static bool IsStreakMilestone(int streak)
        {
            return streak == 5 || (streak >= 10 && streak % 10 == 0);
        }

        /// <summary>The chord that needed the most help (≥3 tries and ≥40% not clean), or null.</summary>
        public string HardestChord()
        {
            string worst = null;
            double worstRate = 0.4 - 1e-9;
            foreach (var pair in _attempts)
            {
                if (pair.Value < 3) continue;
                int miss;
                _misses.TryGetValue(pair.Key, out miss);
                double rate = miss / (double)pair.Value;
                if (rate > worstRate) { worstRate = rate; worst = pair.Key; }
            }
            return worst;
        }

        public SongResult Result(string songId, double tempoFactor, bool looped)
        {
            return new SongResult
            {
                songId = songId,
                cleanRate = CleanRate,
                timingRate = TimingRate,
                longestStreak = LongestStreak,
                tempoFactor = tempoFactor,
                strums = Strums,
                looped = looped
            };
        }

        /// <summary>The headline: "78% clean chords — your longest streak was 9!"</summary>
        public string Headline()
        {
            if (Strums == 0) return "Let's play that one together again.";
            int pct = Percent(CleanRate);
            if (LongestStreak >= 2) return pct + "% clean chords — your longest streak was " + LongestStreak + "!";
            return pct + "% clean chords. Nice work getting through it!";
        }

        /// <summary>One specific, kind tip.</summary>
        public string Tip()
        {
            if (Strums == 0) return "Strum once on each click and the coach will listen.";

            string hardest = HardestChord();
            if (hardest != null && CleanRate < 0.9)
                return hardest + " was the tricky one. A slow change drill into " + hardest + " will help.";

            if (TimedStrums >= 4 && TimingRate < 0.75)
            {
                if (EarlyStrums > LateStrums) return "You rushed a little. Let the click lead you.";
                return "Start moving to the next chord one beat early.";
            }

            if (SilentChords > 0 && SilentChords * 4 >= Attempts)
                return "Keep your strumming hand moving, even through the changes.";

            if (CleanRate >= 0.9 && (TimedStrums == 0 || TimingRate >= 0.85))
                return "Lovely and steady. Try a run glancing less at the ghost hand.";

            return "Nice progress. Relax your fretting hand between chords.";
        }

        /// <summary>A short line about the next tempo, or "" when it didn't change.</summary>
        public static string TempoNote(double factorBefore, double factorAfter)
        {
            if (factorAfter > factorBefore + 1e-6) return "Next run is a little faster.";
            if (factorAfter < factorBefore - 1e-6) return "Next run is a little slower, so changes feel easier.";
            return "";
        }

        public static int Percent(double rate)
        {
            return (int)Math.Round(Math.Max(0, Math.Min(1, rate)) * 100.0);
        }

        private static void Count(Dictionary<string, int> map, string key)
        {
            if (string.IsNullOrEmpty(key)) key = "?";
            int n;
            map.TryGetValue(key, out n);
            map[key] = n + 1;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace GhostFret.Core.Progress
{
    [Serializable]
    public class ChordStat
    {
        public string chord;
        public int attempts;
        public int clean;
        public float CleanRate { get { return attempts == 0 ? 0f : (float)clean / attempts; } }
    }

    [Serializable]
    public class DrillStat
    {
        public string pair;      // "Em-D"
        public int best60s;
    }

    [Serializable]
    public class SongStat
    {
        public string songId;
        public float bestCleanRate;
        public float tempoFactorReached;
        public float nextTempoFactor;     // comfort tempo for the next run (0 = not played yet)
    }

    [Serializable]
    public class PracticeSettings
    {
        public bool leftHanded;
        public string tuning = "Standard";
        public float overlayFade;          // 0 = full overlay, 1 = hidden
        public float overlayBrightness = 1f;
        public float metronomeVolume = 0.6f;
        public bool captions = true;

        public void Sanitize()
        {
            if (string.IsNullOrEmpty(tuning)) tuning = "Standard";
            overlayFade = Clamp(overlayFade, 0f, 1f);
            overlayBrightness = Clamp(overlayBrightness, 0.3f, 1.5f);
            metronomeVolume = Clamp(metronomeVolume, 0f, 1f);
            captions = true;   // captions are always on (accessibility)
        }

        internal static float Clamp(float v, float lo, float hi)
        {
            if (float.IsNaN(v)) return lo;
            return v < lo ? lo : v > hi ? hi : v;
        }
    }

    /// <summary>
    /// Everything we remember between sessions. Stored on the device only.
    /// Lists (not dictionaries) so Unity's JsonUtility can save it.
    /// </summary>
    [Serializable]
    public class PracticeProgress
    {
        public int streakDays;
        public string lastSessionDate;      // "yyyy-MM-dd", local date
        public int currentDay = 1;          // curriculum day
        public string lastDayCompletedDate; // the date the curriculum day last moved on (once per date)
        public int sessionsCompleted;
        public bool airDay1Done;            // Air mode: first time runs Air Day 1, later an Air song
        public List<ChordStat> chords = new List<ChordStat>();
        public List<DrillStat> drills = new List<DrillStat>();
        public List<SongStat> songs = new List<SongStat>();
        public PracticeSettings settings = new PracticeSettings();

        private static string DateText(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        /// <summary>True for a brand-new player: go straight Fit → Tune → first chord, no menus.</summary>
        public bool IsFirstTime
        {
            get
            {
                if (sessionsCompleted > 0) return false;
                foreach (ChordStat c in chords) if (c.clean > 0) return false;
                return true;
            }
        }

        /// <summary>Call once when a session starts, with today's local date.</summary>
        public void MarkSession(DateTime today)
        {
            string todayText = DateText(today);
            if (lastSessionDate == todayText) return;

            DateTime last;
            bool hasLast = DateTime.TryParseExact(lastSessionDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out last);
            if (hasLast && (today.Date - last.Date).TotalDays == 1) streakDays++;
            else streakDays = 1;
            lastSessionDate = todayText;
        }

        /// <summary>The streak as it stands today, without changing it (0 if the chain broke).</summary>
        public int StreakAsOf(DateTime today)
        {
            DateTime last;
            if (!DateTime.TryParseExact(lastSessionDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out last)) return 0;
            double gap = (today.Date - last.Date).TotalDays;
            return gap <= 1 && gap >= 0 ? streakDays : 0;
        }

        public void RecordChordAttempt(string chord, bool wasClean)
        {
            if (string.IsNullOrEmpty(chord)) return;
            ChordStat stat = chords.Find(c => c.chord == chord);
            if (stat == null) { stat = new ChordStat { chord = chord }; chords.Add(stat); }
            stat.attempts++;
            if (wasClean) stat.clean++;
        }

        public bool RecordDrill(string pair, int score)
        {
            DrillStat stat = drills.Find(d => d.pair == pair);
            if (stat == null) { stat = new DrillStat { pair = pair }; drills.Add(stat); }
            if (score <= stat.best60s) return false;
            stat.best60s = score;
            return true;   // new personal best
        }

        public int BestDrill(string pair)
        {
            DrillStat stat = drills.Find(d => d.pair == pair);
            return stat == null ? 0 : stat.best60s;
        }

        public void RecordSong(string songId, float cleanRate, float tempoFactor)
        {
            SongStat stat = songs.Find(s => s.songId == songId);
            if (stat == null) { stat = new SongStat { songId = songId }; songs.Add(stat); }
            if (cleanRate > stat.bestCleanRate) stat.bestCleanRate = cleanRate;
            if (tempoFactor > stat.tempoFactorReached) stat.tempoFactorReached = tempoFactor;
            if (tempoFactor > 0f) stat.nextTempoFactor = tempoFactor;   // SongResult.tempoFactor is for the NEXT run
        }

        /// <summary>Saved comfort tempo for a song's next run, or 0 if never played.</summary>
        public float SongTempo(string songId)
        {
            SongStat stat = songs.Find(s => s.songId == songId);
            return stat == null ? 0f : stat.nextTempoFactor;
        }

        /// <summary>
        /// A guided lesson was finished today: move to the next curriculum day, at most once per date
        /// (so the next day's skill is tomorrow's reason to come back). Returns true if it moved.
        /// </summary>
        public bool CompleteDay(DateTime today, int lastCurriculumDay)
        {
            string t = DateText(today);
            if (lastDayCompletedDate == t) return false;
            lastDayCompletedDate = t;
            if (currentDay < lastCurriculumDay) { currentDay++; return true; }
            return false;
        }

        /// <summary>Repairs anything missing or out of range after loading (old or hand-edited files).</summary>
        public void Sanitize()
        {
            if (chords == null) chords = new List<ChordStat>();
            if (drills == null) drills = new List<DrillStat>();
            if (songs == null) songs = new List<SongStat>();
            chords.RemoveAll(c => c == null || string.IsNullOrEmpty(c.chord));
            drills.RemoveAll(d => d == null || string.IsNullOrEmpty(d.pair));
            songs.RemoveAll(s => s == null || string.IsNullOrEmpty(s.songId));
            foreach (ChordStat c in chords) { if (c.attempts < 0) c.attempts = 0; if (c.clean < 0) c.clean = 0; if (c.clean > c.attempts) c.attempts = c.clean; }
            foreach (DrillStat d in drills) if (d.best60s < 0) d.best60s = 0;
            if (settings == null) settings = new PracticeSettings();
            settings.Sanitize();
            if (currentDay < 1) currentDay = 1;
            if (streakDays < 0) streakDays = 0;
            if (sessionsCompleted < 0) sessionsCompleted = 0;
        }
    }
}

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
    }

    [Serializable]
    public class PracticeSettings
    {
        public bool leftHanded;
        public string tuning = "Standard";
        public float overlayFade;          // 0 = full overlay, 1 = hidden
        public float overlayBrightness = 1f;
        public bool captions = true;
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
        public List<ChordStat> chords = new List<ChordStat>();
        public List<DrillStat> drills = new List<DrillStat>();
        public List<SongStat> songs = new List<SongStat>();
        public PracticeSettings settings = new PracticeSettings();

        /// <summary>Call once when a session starts, with today's local date.</summary>
        public void MarkSession(DateTime today)
        {
            string todayText = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (lastSessionDate == todayText) return;

            DateTime last;
            bool hasLast = DateTime.TryParseExact(lastSessionDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out last);
            if (hasLast && (today.Date - last.Date).TotalDays == 1) streakDays++;
            else streakDays = 1;
            lastSessionDate = todayText;
        }

        public void RecordChordAttempt(string chord, bool wasClean)
        {
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

        public void RecordSong(string songId, float cleanRate, float tempoFactor)
        {
            SongStat stat = songs.Find(s => s.songId == songId);
            if (stat == null) { stat = new SongStat { songId = songId }; songs.Add(stat); }
            if (cleanRate > stat.bestCleanRate) stat.bestCleanRate = cleanRate;
            if (tempoFactor > stat.tempoFactorReached) stat.tempoFactorReached = tempoFactor;
        }
    }
}

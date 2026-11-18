using System.Collections.Generic;
using GhostFret.Core.Music;

namespace GhostFret.Core.Content
{
    // Plain serializable classes: Unity's JsonUtility (runtime) and System.Text.Json (tests)
    // both read these from StreamingAssets/GhostFret/songs/*.json. Field names match the JSON.

    [System.Serializable]
    public class SongBar
    {
        public string chord;
        public int beats;
    }

    [System.Serializable]
    public class SongSection
    {
        public string name;
        public SongBar[] bars;
    }

    [System.Serializable]
    public class SongChart
    {
        public string id;
        public string title;
        public string source;            // e.g. "Traditional (public domain), simplified arrangement"
        public int difficulty;           // 1 (easiest) .. 5
        public string[] chordsRequired;
        public int[] timeSignature;      // e.g. [3, 4]
        public int originalBpm;
        public string[] strumPattern;    // per beat subdivision, e.g. ["D","D","U"]
        public string backingTrack;      // optional path under StreamingAssets; empty = metronome only
        public SongSection[] sections;
    }

    public struct ChordEvent
    {
        public string Chord;
        public double StartBeat;
        public int Beats;
        public string Section;
    }

    /// <summary>A song chart flattened into a list of chord events on a beat timeline.</summary>
    public sealed class SongTimeline
    {
        public readonly List<ChordEvent> Events = new List<ChordEvent>();
        public double TotalBeats { get; private set; }

        public static SongTimeline Build(SongChart chart)
        {
            var timeline = new SongTimeline();
            double beat = 0;
            foreach (var section in chart.sections)
            {
                foreach (var bar in section.bars)
                {
                    timeline.Events.Add(new ChordEvent { Chord = bar.chord, StartBeat = beat, Beats = bar.beats, Section = section.name });
                    beat += bar.beats;
                }
            }
            timeline.TotalBeats = beat;
            return timeline;
        }

        /// <summary>Index of the chord event playing at a beat, or −1 if outside the song.</summary>
        public int IndexAt(double beat)
        {
            for (int i = 0; i < Events.Count; i++)
            {
                if (beat >= Events[i].StartBeat && beat < Events[i].StartBeat + Events[i].Beats) return i;
            }
            return -1;
        }
    }

    public static class SongValidator
    {
        /// <summary>Returns a list of problems; empty means the chart is valid.</summary>
        public static List<string> Validate(SongChart chart)
        {
            var errors = new List<string>();
            string who = string.IsNullOrEmpty(chart.id) ? "(no id)" : chart.id;

            if (string.IsNullOrEmpty(chart.id)) errors.Add("Missing id.");
            if (string.IsNullOrEmpty(chart.title)) errors.Add(who + ": missing title.");
            if (string.IsNullOrEmpty(chart.source) || !chart.source.ToLowerInvariant().Contains("public domain") && !chart.source.ToLowerInvariant().Contains("original"))
                errors.Add(who + ": source must say 'public domain' or 'original' (see DESIGN.md §15).");
            if (chart.timeSignature == null || chart.timeSignature.Length != 2) errors.Add(who + ": timeSignature must be [beats, noteValue].");
            if (chart.originalBpm < 40 || chart.originalBpm > 200) errors.Add(who + ": originalBpm should be 40..200.");
            if (chart.difficulty < 1 || chart.difficulty > 5) errors.Add(who + ": difficulty should be 1..5.");
            if (chart.strumPattern == null || chart.strumPattern.Length == 0) errors.Add(who + ": strumPattern is empty.");
            else
                foreach (var s in chart.strumPattern)
                    if (s != "D" && s != "U" && s != "-") errors.Add(who + ": strumPattern items must be D, U or -.");

            var used = new HashSet<string>();
            if (chart.sections == null || chart.sections.Length == 0) errors.Add(who + ": no sections.");
            else
            {
                foreach (var section in chart.sections)
                {
                    if (section.bars == null || section.bars.Length == 0) { errors.Add(who + ": section '" + section.name + "' has no bars."); continue; }
                    foreach (var bar in section.bars)
                    {
                        if (!ChordLibrary.Contains(bar.chord)) errors.Add(who + ": unknown chord '" + bar.chord + "'.");
                        if (bar.beats <= 0) errors.Add(who + ": bar with non-positive beats.");
                        used.Add(bar.chord);
                    }
                }
            }

            var required = new HashSet<string>(chart.chordsRequired ?? new string[0]);
            foreach (var c in used) if (!required.Contains(c)) errors.Add(who + ": chord '" + c + "' is used but not in chordsRequired.");
            foreach (var c in required) if (!used.Contains(c)) errors.Add(who + ": chord '" + c + "' is in chordsRequired but never used.");
            return errors;
        }

        public static bool IsUnlocked(SongChart chart, ICollection<string> learnedChords)
        {
            foreach (var c in chart.chordsRequired) if (!learnedChords.Contains(c)) return false;
            return true;
        }
    }
}

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
        public string backingTrack;      // optional, relative to StreamingAssets/GhostFret (e.g. "audio/x.ogg"); empty = metronome only
        public SongSection[] sections;

        /// <summary>Beats in one bar (the top number of the time signature); 4 if missing.</summary>
        public int GetBeatsPerBar()
        {
            return timeSignature != null && timeSignature.Length > 0 && timeSignature[0] > 0 ? timeSignature[0] : 4;
        }
    }

    public struct SectionSpan
    {
        public string Name;
        public double StartBeat;
        public double EndBeat;   // exclusive
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
        public readonly List<SectionSpan> Sections = new List<SectionSpan>();
        public double TotalBeats { get; private set; }

        public static SongTimeline Build(SongChart chart)
        {
            var timeline = new SongTimeline();
            double beat = 0;
            if (chart == null || chart.sections == null) return timeline;
            foreach (var section in chart.sections)
            {
                if (section == null || section.bars == null) continue;
                double sectionStart = beat;
                foreach (var bar in section.bars)
                {
                    if (bar == null || bar.beats <= 0) continue;
                    timeline.Events.Add(new ChordEvent { Chord = bar.chord, StartBeat = beat, Beats = bar.beats, Section = section.name });
                    beat += bar.beats;
                }
                if (beat > sectionStart)
                    timeline.Sections.Add(new SectionSpan { Name = section.name, StartBeat = sectionStart, EndBeat = beat });
            }
            timeline.TotalBeats = beat;
            return timeline;
        }

        /// <summary>Finds a section by name (case-insensitive). The first match wins.</summary>
        public bool TryGetSection(string name, out SectionSpan span)
        {
            foreach (var s in Sections)
            {
                if (string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase)) { span = s; return true; }
            }
            span = default(SectionSpan);
            return false;
        }

        /// <summary>Song length in seconds at a tempo (for "about 40 seconds" labels and tests).</summary>
        public double DurationSeconds(double bpm)
        {
            return bpm <= 0 ? 0 : TotalBeats * 60.0 / bpm;
        }

        /// <summary>Seconds from the first beat until a chord event starts, at a tempo.</summary>
        public double StartSeconds(int index, double bpm)
        {
            if (index < 0 || index >= Events.Count || bpm <= 0) return 0;
            return Events[index].StartBeat * 60.0 / bpm;
        }

        /// <summary>Chord event playing a number of seconds after the first beat, at a tempo; −1 if outside.</summary>
        public int IndexAtSeconds(double seconds, double bpm)
        {
            if (bpm <= 0) return -1;
            return IndexAt(seconds * bpm / 60.0);
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
            else if (chart.timeSignature[0] < 1 || chart.timeSignature[0] > 12) errors.Add(who + ": timeSignature beats should be 1..12.");
            if (chart.originalBpm < 40 || chart.originalBpm > 200) errors.Add(who + ": originalBpm should be 40..200.");
            if (chart.difficulty < 1 || chart.difficulty > 5) errors.Add(who + ": difficulty should be 1..5.");
            if (chart.strumPattern == null || chart.strumPattern.Length == 0) errors.Add(who + ": strumPattern is empty.");
            else
                foreach (var s in chart.strumPattern)
                    if (s != "D" && s != "U" && s != "-") errors.Add(who + ": strumPattern items must be D, U or -.");
            int beatsPerBar = chart.GetBeatsPerBar();
            // The pattern is one beat (1–2 items) or one whole bar, so the strum lane lines up with the bar.
            if (chart.strumPattern != null && chart.strumPattern.Length > 2 && chart.strumPattern.Length % beatsPerBar != 0)
                errors.Add(who + ": strumPattern should cover one beat or one bar of " + beatsPerBar + " beats.");

            var used = new HashSet<string>();
            if (chart.sections == null || chart.sections.Length == 0) errors.Add(who + ": no sections.");
            else
            {
                var names = new HashSet<string>();
                foreach (var section in chart.sections)
                {
                    if (section == null) { errors.Add(who + ": empty section."); continue; }
                    // Section names are shown on "Loop verse" buttons, so they must exist and be unique.
                    if (string.IsNullOrEmpty(section.name)) errors.Add(who + ": a section has no name.");
                    else if (!names.Add(section.name.ToLowerInvariant())) errors.Add(who + ": two sections are called '" + section.name + "'.");
                    if (section.bars == null || section.bars.Length == 0) { errors.Add(who + ": section '" + section.name + "' has no bars."); continue; }
                    int sectionBeats = 0;
                    foreach (var bar in section.bars)
                    {
                        if (bar == null) { errors.Add(who + ": empty bar."); continue; }
                        sectionBeats += System.Math.Max(0, bar.beats);
                        if (!ChordLibrary.Contains(bar.chord)) errors.Add(who + ": unknown chord '" + bar.chord + "'.");
                        if (bar.beats <= 0) errors.Add(who + ": bar with non-positive beats.");
                        used.Add(bar.chord);
                    }
                    // Whole bars, so looping a section keeps the beat-1 accent and the strum pattern in place.
                    if (sectionBeats % beatsPerBar != 0)
                        errors.Add(who + ": section '" + section.name + "' should be whole bars of " + beatsPerBar + " beats.");
                }
            }

            var required = new HashSet<string>(chart.chordsRequired ?? new string[0]);
            foreach (var c in used) if (!required.Contains(c)) errors.Add(who + ": chord '" + c + "' is used but not in chordsRequired.");
            foreach (var c in required) if (!used.Contains(c)) errors.Add(who + ": chord '" + c + "' is in chordsRequired but never used.");
            return errors;
        }

        public static bool IsUnlocked(SongChart chart, ICollection<string> learnedChords)
        {
            if (chart == null || learnedChords == null) return false;
            if (chart.chordsRequired == null) return true;
            foreach (var c in chart.chordsRequired) if (!learnedChords.Contains(c)) return false;
            return true;
        }
    }
}

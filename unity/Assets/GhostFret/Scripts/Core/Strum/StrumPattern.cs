using System;
using GhostFret.Core.Timing;

namespace GhostFret.Core.Strum
{
    /// <summary>How one strum lined up with the pattern.</summary>
    public struct StrumJudgement
    {
        public TimingResult Timing;        // offset from the nearest strum the pattern asks for
        public StrumDirection Expected;    // what the pattern wanted at that slot
        public long Slot;                  // slot index (beat * SlotsPerBeat), anchored at beat 0
        /// <summary>True when the strum landed nearer a rest than any wanted strum (an extra strum).</summary>
        public bool Extra;
    }

    /// <summary>
    /// A strum pattern from a chart or the curriculum, e.g. ["D","-","D","U","D","U"] in 3/4.
    /// The pattern covers one bar, so 6 items in 3/4 means 2 slots per beat (eighth notes).
    /// Slot 0 is beat 0 (the first beat of a bar). Count-in beats are negative and still map to the pattern.
    /// </summary>
    public sealed class StrumPattern
    {
        public readonly StrumDirection[] Slots;
        public readonly int SlotsPerBeat;
        public readonly int BeatsPerBar;

        private StrumPattern(StrumDirection[] slots, int slotsPerBeat, int beatsPerBar)
        {
            Slots = slots;
            SlotsPerBeat = slotsPerBeat;
            BeatsPerBar = beatsPerBar;
        }

        /// <summary>Plain downs on every beat: the day-1 pattern and the fallback for bad data.</summary>
        public static StrumPattern QuarterDowns(int beatsPerBar)
        {
            return Parse(new[] { "D" }, beatsPerBar);
        }

        public static StrumPattern Parse(string[] pattern, int beatsPerBar)
        {
            if (beatsPerBar < 1) beatsPerBar = 4;
            if (pattern == null || pattern.Length == 0) pattern = new[] { "D" };

            var slots = new StrumDirection[pattern.Length];
            for (int i = 0; i < pattern.Length; i++)
            {
                string s = pattern[i] == null ? "" : pattern[i].Trim().ToUpperInvariant();
                slots[i] = s == "D" ? StrumDirection.Down : s == "U" ? StrumDirection.Up : StrumDirection.None;
            }

            int len = pattern.Length;
            int perBeat;
            if (len <= 2) perBeat = len;                               // one beat: ["D"] quarters, ["D","U"] eighths
            else if (len % beatsPerBar == 0) perBeat = len / beatsPerBar; // one bar: 8 in 4/4 → eighths
            else perBeat = 2;                                          // odd lengths: assume eighths, repeating
            return new StrumPattern(slots, Math.Max(1, perBeat), beatsPerBar);
        }

        public double SlotBeat(long slot) { return slot / (double)SlotsPerBeat; }

        /// <summary>What the pattern wants at a slot (works for negative count-in slots too).</summary>
        public StrumDirection At(long slot)
        {
            long n = Slots.Length;
            long i = ((slot % n) + n) % n;
            return Slots[i];
        }

        public bool HasAnyStrum
        {
            get { foreach (var s in Slots) if (s != StrumDirection.None) return true; return false; }
        }

        /// <summary>Nearest slot that asks for a strum (D or U), searching up to one bar each way.</summary>
        public long NearestStrumSlot(double beat)
        {
            long centre = (long)Math.Round(beat * SlotsPerBeat);
            if (!HasAnyStrum) return centre;
            long best = centre;
            double bestDist = double.MaxValue;
            int reach = Math.Max(Slots.Length, SlotsPerBeat * BeatsPerBar);
            for (long s = centre - reach; s <= centre + reach; s++)
            {
                if (At(s) == StrumDirection.None) continue;
                double d = Math.Abs(SlotBeat(s) - beat);
                // On an exact tie prefer the later slot: a beginner is more often early than very late.
                if (d < bestDist - 1e-9 || (Math.Abs(d - bestDist) <= 1e-9 && s > best)) { bestDist = d; best = s; }
            }
            return best;
        }

        /// <summary>
        /// Judges a strum (time in the grid's clock, before latency correction) against the pattern.
        /// Uses the grid's tempo, latency and on-time window.
        /// </summary>
        public StrumJudgement Judge(BeatGrid grid, double strumTimeSeconds)
        {
            double corrected = strumTimeSeconds - grid.LatencyMs / 1000.0;
            double beat = grid.BeatAt(corrected);

            long strumSlot = NearestStrumSlot(beat);
            long anySlot = (long)Math.Round(beat * SlotsPerBeat);
            bool extra = At(anySlot) == StrumDirection.None &&
                         Math.Abs(SlotBeat(anySlot) - beat) < Math.Abs(SlotBeat(strumSlot) - beat);

            double slotBeat = SlotBeat(strumSlot);
            double offsetMs = (corrected - grid.TimeOfBeat(slotBeat)) * 1000.0;
            TimingVerdict verdict = TimingVerdict.OnTime;
            if (offsetMs < -grid.OnTimeWindowMs) verdict = TimingVerdict.Early;
            else if (offsetMs > grid.OnTimeWindowMs) verdict = TimingVerdict.Late;

            return new StrumJudgement
            {
                Timing = new TimingResult { NearestSlot = slotBeat, OffsetMs = offsetMs, Verdict = verdict },
                Expected = At(strumSlot),
                Slot = strumSlot,
                Extra = extra
            };
        }
    }
}

using System;
using GhostFret.Core.Strum;
using GhostFret.Core.Timing;

namespace GhostFret.Core.Lesson
{
    /// <summary>The coach's read on one strum.</summary>
    public struct StrumCoachJudgement
    {
        public TimingResult Timing;
        public StrumDirection Expected;   // None = the pattern has a rest here
        public StrumDirection Heard;      // None = hand tracking wasn't sure
        public string Message;
        public bool Good;
    }

    /// <summary>
    /// Strum Coach loop (DESIGN.md §7.3): the day's pattern for a few bars after a one-bar count-in.
    /// Pattern entries are per eighth note: "D", "U" or "-" (rest). Direction is only commented on
    /// when hand tracking reported one, so we never claim a wrong direction.
    /// </summary>
    public sealed class StrumPractice
    {
        public const int Subdivision = 2;
        public readonly string[] Pattern;
        public readonly BeatGrid Grid;
        public readonly int Bars;

        public int Strums { get; private set; }
        public int OnTime { get; private set; }
        public int RightDirection { get; private set; }

        /// <summary>Beats per bar = pattern length / 2 (8 entries = 4/4, 6 entries = 3/4).</summary>
        public int BeatsPerBar { get { return Math.Max(1, Pattern.Length / Subdivision); } }
        public double TotalBeats { get { return Bars * BeatsPerBar; } }

        /// <param name="startTime">dspTime the count-in starts; beat 0 of practice is one bar later.</param>
        public StrumPractice(string[] pattern, double bpm, double startTime, int bars = 4)
        {
            Pattern = pattern != null && pattern.Length >= 2 ? pattern : new[] { "D", "-", "D", "-", "D", "-", "D", "-" };
            Bars = bars;
            Grid = new BeatGrid(bpm, 0) { Subdivision = Subdivision };
            Grid.FirstBeatTime = startTime + BeatsPerBar * Grid.SecondsPerBeat;
        }

        public bool IsFinished(double now) { return Grid.BeatAt(now) >= TotalBeats + 0.5; }
        public bool InCountIn(double now) { return Grid.BeatAt(now) < -0.25; }

        public StrumDirection ExpectedAt(double slotBeats)
        {
            int i = (int)Math.Round(slotBeats * Subdivision);
            i = ((i % Pattern.Length) + Pattern.Length) % Pattern.Length;
            string p = Pattern[i];
            if (p == "D") return StrumDirection.Down;
            if (p == "U") return StrumDirection.Up;
            return StrumDirection.None;
        }

        /// <summary>Judge one strum. Returns null for strums in the count-in or after the end.</summary>
        public StrumCoachJudgement? Judge(double strumTime, StrumDirection heard)
        {
            TimingResult t = Grid.Judge(strumTime);
            if (t.NearestSlot < 0 || t.NearestSlot >= TotalBeats) return null;

            StrumDirection expected = ExpectedAt(t.NearestSlot);
            Strums++;
            bool onTime = t.Verdict == TimingVerdict.OnTime;
            if (onTime) OnTime++;
            bool dirKnown = heard != StrumDirection.None && expected != StrumDirection.None;
            bool dirRight = dirKnown && heard == expected;
            if (dirRight) RightDirection++;

            string msg;
            if (t.Verdict == TimingVerdict.Early) msg = "A little early. Relax and wait for the click.";
            else if (t.Verdict == TimingVerdict.Late) msg = "A little late. Move with the click.";
            else if (expected == StrumDirection.None) msg = "That one's a rest. Keep your hand moving, just miss the strings.";
            else if (dirKnown && !dirRight) msg = expected == StrumDirection.Up ? "Try an up-strum here." : "Try a down-strum here.";
            else msg = "On time!";

            return new StrumCoachJudgement
            {
                Timing = t, Expected = expected, Heard = heard, Message = msg,
                Good = onTime && expected != StrumDirection.None && (!dirKnown || dirRight)
            };
        }

        public double OnTimeRate { get { return Strums == 0 ? 0 : (double)OnTime / Strums; } }

        public string Summary()
        {
            if (Strums == 0) return "Let's try that again. Strum along with the click.";
            int pct = (int)Math.Round(OnTimeRate * 100);
            if (pct >= 80) return "Lovely rhythm! " + pct + "% on time.";
            if (pct >= 50) return "Nice work. " + pct + "% on time. It gets easier every day.";
            return "Good start. Listen for the click and keep your hand moving.";
        }
    }
}

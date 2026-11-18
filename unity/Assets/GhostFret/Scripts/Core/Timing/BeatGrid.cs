using System;

namespace GhostFret.Core.Timing
{
    public enum TimingVerdict { OnTime, Early, Late }

    public struct TimingResult
    {
        public double NearestSlot;     // in beats (e.g. 3.5 = the "and" after beat 4)
        public double OffsetMs;        // + = late, − = early
        public TimingVerdict Verdict;
    }

    /// <summary>
    /// A steady beat clock for songs and strum practice.
    /// Subdivision 1 = quarter notes, 2 = eighth notes (for up-strums).
    /// </summary>
    public sealed class BeatGrid
    {
        public double Bpm;
        public double FirstBeatTime;      // seconds
        public int Subdivision = 2;
        public double OnTimeWindowMs = 90; // generous for beginners
        public double LatencyMs;           // measured mic delay, subtracted from strum times

        public BeatGrid(double bpm, double firstBeatTime)
        {
            Bpm = bpm;
            FirstBeatTime = firstBeatTime;
        }

        public double SecondsPerBeat { get { return 60.0 / Bpm; } }

        public double BeatAt(double timeSeconds)
        {
            return (timeSeconds - FirstBeatTime) / SecondsPerBeat;
        }

        public double TimeOfBeat(double beat)
        {
            return FirstBeatTime + beat * SecondsPerBeat;
        }

        public TimingResult Judge(double strumTimeSeconds)
        {
            double corrected = strumTimeSeconds - LatencyMs / 1000.0;
            double beat = BeatAt(corrected);
            double slot = Math.Round(beat * Subdivision) / Subdivision;
            double offsetMs = (corrected - TimeOfBeat(slot)) * 1000.0;

            TimingVerdict verdict = TimingVerdict.OnTime;
            if (offsetMs < -OnTimeWindowMs) verdict = TimingVerdict.Early;
            else if (offsetMs > OnTimeWindowMs) verdict = TimingVerdict.Late;

            return new TimingResult { NearestSlot = slot, OffsetMs = offsetMs, Verdict = verdict };
        }
    }
}

using System.Collections.Generic;
using GhostFret.Core.Timing;

namespace GhostFret.Core.Strum
{
    /// <summary>
    /// Words for the strum lane. Short, kind, one tip at a time (UX_GUIDE.md "Words").
    /// Direction beats timing: if the hand went the wrong way, that is the one tip.
    /// When tracking couldn't tell the direction (None), we say nothing about direction (rule 5).
    /// </summary>
    public static class StrumFeedback
    {
        public const string OnTime = "On time";
        public const string Early = "A little early";
        public const string Late = "A little late";
        public const string MissedUp = "Missed the up-strum";
        public const string TryUp = "Try an up-strum here";
        public const string TryDown = "Try a down-strum here";

        public static string ForTiming(TimingResult result, out bool good)
        {
            good = result.Verdict == TimingVerdict.OnTime;
            if (result.Verdict == TimingVerdict.Early) return Early;
            if (result.Verdict == TimingVerdict.Late) return Late;
            return OnTime;
        }

        public static string ForStrum(TimingResult result, StrumDirection heard, StrumDirection expected, out bool good)
        {
            if (heard != StrumDirection.None && expected != StrumDirection.None && heard != expected)
            {
                good = false;
                return expected == StrumDirection.Up ? TryUp : TryDown;
            }
            return ForTiming(result, out good);
        }
    }

    /// <summary>
    /// Notices an up-strum the pattern asked for that nobody played, so the lane can say
    /// "Missed the up-strum". Gentle by design: only while the player is actively strumming,
    /// and at most once per cooldown, so a player who stopped isn't nagged.
    /// </summary>
    public sealed class MissedStrumDetector
    {
        public double CooldownBeats = 4;       // at most one "missed" note per bar of 4/4
        public double ActiveWithinBeats = 2;   // only if they strummed in the last 2 beats

        private readonly HashSet<long> _hit = new HashSet<long>();
        private long _nextToCheck = long.MinValue;
        private double _lastStrumBeat = double.NegativeInfinity;
        private double _lastSaidBeat = double.NegativeInfinity;

        public void Reset()
        {
            _hit.Clear();
            _nextToCheck = long.MinValue;
            _lastStrumBeat = double.NegativeInfinity;
            _lastSaidBeat = double.NegativeInfinity;
        }

        public void MarkHit(long slot, double beat)
        {
            _hit.Add(slot);
            _lastStrumBeat = beat;
        }

        /// <summary>
        /// Call every frame. graceBeats = how long after a slot a strum still counts (the late window).
        /// Returns true (once) when an unplayed up-strum slot has just passed and it's okay to mention it.
        /// missedSlot is the slot that was missed (to dim its arrow).
        /// </summary>
        public bool CheckMissedUp(StrumPattern pattern, double beatNow, double graceBeats, out long missedSlot)
        {
            missedSlot = 0;
            long lastPassed = (long)System.Math.Floor((beatNow - graceBeats) * pattern.SlotsPerBeat);
            if (_nextToCheck == long.MinValue) _nextToCheck = lastPassed + 1;   // start fresh, don't judge the past

            bool say = false;
            for (long s = _nextToCheck; s <= lastPassed; s++)
            {
                if (pattern.At(s) != StrumDirection.Up || _hit.Contains(s)) continue;
                bool active = beatNow - _lastStrumBeat <= ActiveWithinBeats;
                bool rested = beatNow - _lastSaidBeat >= CooldownBeats;
                if (active && rested && s >= 0) { say = true; missedSlot = s; _lastSaidBeat = beatNow; }
            }
            if (lastPassed + 1 > _nextToCheck) _nextToCheck = lastPassed + 1;
            _hit.RemoveWhere(x => x < _nextToCheck - 64);
            return say;
        }
    }
}

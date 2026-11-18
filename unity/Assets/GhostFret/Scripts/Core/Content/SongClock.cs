using System;

namespace GhostFret.Core.Content
{
    /// <summary>
    /// Maps the steady "raw" beat clock (BeatGrid beats, which only ever go forward) to a position in
    /// the song, so section loops and pauses never disturb the metronome or the strum pattern.
    ///   song beat = raw beat + offset
    /// A loop wraps by shifting the offset back by the loop length. Jumping into a different section
    /// waits for the next bar line so the strum pattern and the beat-1 accent stay in phase.
    /// Raw beats that go backwards (the lead-in bar after Resume) hold the song still.
    /// </summary>
    public sealed class SongClock
    {
        public readonly SongTimeline Timeline;
        public readonly int BeatsPerBar;

        public bool Looping { get; private set; }
        public string LoopName { get; private set; }
        public double LoopStart { get; private set; }
        public double LoopEnd { get; private set; }
        /// <summary>How many times the loop has wrapped back to its start.</summary>
        public int LoopPasses { get; private set; }

        public double SongBeat { get; private set; }

        private double _offset;
        private double _lastRaw = double.NegativeInfinity;
        private bool _jumpPending;
        private double _jumpAtRaw;
        private double _jumpTarget;

        public SongClock(SongTimeline timeline, int beatsPerBar, double startBeat = 0)
        {
            Timeline = timeline;
            BeatsPerBar = Math.Max(1, beatsPerBar);
            _offset = startBeat;
            SongBeat = startBeat;
        }

        public double LoopLength { get { return LoopEnd - LoopStart; } }
        public bool Started { get { return !double.IsNegativeInfinity(_lastRaw); } }

        /// <summary>True once a non-looping run reaches the end of the song.</summary>
        public bool IsFinished { get { return !Looping && SongBeat >= Timeline.TotalBeats; } }

        /// <summary>Call every frame with the grid's current beat. Returns the song beat.</summary>
        public double Advance(double rawBeat)
        {
            if (rawBeat < _lastRaw) return SongBeat;   // lead-in after Resume: hold position
            _lastRaw = rawBeat;

            if (_jumpPending && rawBeat >= _jumpAtRaw)
            {
                _offset = _jumpTarget - _jumpAtRaw;
                _jumpPending = false;
            }

            double s = rawBeat + _offset;
            if (Looping && LoopLength > 0)
            {
                while (s >= LoopEnd)
                {
                    _offset -= LoopLength;
                    s -= LoopLength;
                    LoopPasses++;
                }
            }
            SongBeat = s;
            return s;
        }

        /// <summary>
        /// Song beat for a past moment (e.g. a strum heard a little after it happened), without moving the clock.
        /// A strum just before a loop wrap is mapped to the end of the loop, not before its start.
        /// </summary>
        public double SongBeatAt(double rawBeat)
        {
            double s = rawBeat + _offset;
            if (Looping && LoopLength > 0)
            {
                if (s >= LoopEnd) s -= LoopLength;
                else if (s < LoopStart && s >= LoopStart - 1.0 && LoopPasses > 0) s += LoopLength;
            }
            return s;
        }

        /// <summary>Loop a section by name. If we're elsewhere, jump there at the next bar line.</summary>
        public bool LoopSection(string name)
        {
            SectionSpan span;
            if (Timeline == null || !Timeline.TryGetSection(name, out span)) return false;
            Looping = true;
            LoopName = span.Name;
            LoopStart = span.StartBeat;
            LoopEnd = span.EndBeat;
            LoopPasses = 0;
            _jumpPending = false;

            bool inside = SongBeat >= LoopStart && SongBeat < LoopEnd;
            if (!Started)
            {
                _offset = LoopStart - (SongBeat - _offset);   // keep the same raw mapping, start at the section
                SongBeat = LoopStart;
            }
            else if (!inside)
            {
                _jumpAtRaw = Math.Ceiling(_lastRaw / BeatsPerBar - 1e-9) * BeatsPerBar;
                _jumpTarget = LoopStart;
                _jumpPending = true;
            }
            return true;
        }

        /// <summary>Stop looping: the song carries on from where it is to the end.</summary>
        public void ClearLoop()
        {
            Looping = false;
            LoopName = null;
            _jumpPending = false;
        }

        public int CurrentIndex { get { return Timeline.IndexAt(SongBeat); } }

        /// <summary>The chord event after index i, following the loop if one is set; −1 at the end of the song.</summary>
        public int NextIndex(int i)
        {
            if (i < 0 || i >= Timeline.Events.Count) return -1;
            var e = Timeline.Events[i];
            if (Looping && e.StartBeat + e.Beats >= LoopEnd - 1e-9)
                return Timeline.IndexAt(LoopStart);
            return i + 1 < Timeline.Events.Count ? i + 1 : -1;
        }

        /// <summary>Beats from now until event i starts next (handles the wrap at a loop end).</summary>
        public double BeatsUntil(int i)
        {
            if (i < 0 || i >= Timeline.Events.Count) return double.PositiveInfinity;
            double d = Timeline.Events[i].StartBeat - SongBeat;
            if (d < 0 && Looping) d += LoopLength;
            return d;
        }
    }
}

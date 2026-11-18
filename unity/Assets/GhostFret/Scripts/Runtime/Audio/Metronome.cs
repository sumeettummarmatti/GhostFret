using System;
using GhostFret.Core.Timing;
using GhostFret.Runtime.UI;
using UnityEngine;

namespace GhostFret.Runtime.Audio
{
    /// <summary>
    /// One smooth clock for visuals and hand samples, on the same timeline as AudioSettings.dspTime.
    /// (dspTime itself only moves once per audio buffer; scheduling should still use dspTime directly.)
    /// </summary>
    public static class DspClock
    {
        private static readonly DspClockSmoother Smoother = new DspClockSmoother();
        private static int _frame = -1;
        private static double _now;

        public static double Now
        {
            get
            {
                if (Time.frameCount != _frame)
                {
                    _frame = Time.frameCount;
                    _now = Smoother.Update(Time.realtimeSinceStartupAsDouble, AudioSettings.dspTime);
                }
                return _now;
            }
        }
    }

    /// <summary>
    /// Soft wooden metronome (UiSounds.Tick, TickAccent on beat 1), sample-accurate:
    /// every tick is scheduled ahead with AudioSource.PlayScheduled on the dspTime of its beat.
    /// Beat(i) fires on the main thread as each beat passes, for visual pulses (i is the BeatGrid beat,
    /// negative during a count-in). CountIn(...) builds a grid whose beat 0 comes after one bar of clicks.
    /// </summary>
    public sealed class Metronome : MonoBehaviour
    {
        public float volume = 0.8f;                 // on top of UiSounds.Volume, so clicks stay under the guitar
        public double scheduleAheadSeconds = 0.25;  // how far ahead ticks are handed to the audio thread
        public double countInLeadSeconds = 0.15;    // time to schedule the very first click safely
        public bool Muted;                          // e.g. while a backing track carries the beat

        /// <summary>Fires as each beat passes (visual pulse). Negative indices are count-in beats.</summary>
        public event Action<int> Beat;
        /// <summary>Count-in number as it sounds: 1, 2, 3, 4 (for "1 2 3 4" on the strum lane).</summary>
        public event Action<int> CountInBeat;

        public bool Running { get; private set; }
        public BeatGrid Grid { get; private set; }
        public int BeatsPerBar { get; private set; }

        private const int SourceCount = 4;
        private AudioSource[] _sources;
        private int _nextSource;
        private long _nextScheduled;   // next beat index to schedule
        private long _nextAnnounced;   // next beat index to raise Beat for
        private long _stopAtBeat = long.MaxValue;

        private void Awake() { EnsureSources(); }

        // No Start(grid, bars) alias: Unity treats any method named Start as its lifecycle message
        // and logs "Start() can not take parameters". Use Begin(grid, beatsPerBar).

        /// <summary>Start ticking on a grid from the next beat. Ticks continue until Stop().</summary>
        public void Begin(BeatGrid grid, int beatsPerBar)
        {
            EnsureSources();
            StopSources();
            Grid = grid;
            BeatsPerBar = Math.Max(1, beatsPerBar);
            double now = AudioSettings.dspTime;
            long first = (long)Math.Ceiling(grid.BeatAt(now) - 1e-6);
            _nextScheduled = first;
            _nextAnnounced = first;
            _stopAtBeat = long.MaxValue;
            Running = true;
            ScheduleAhead();
        }

        /// <summary>
        /// One bar of clicks, then beat 0. Returns the grid (beat 0 = first beat after the count-in).
        /// keepTicking = false stops after the count-in (e.g. Air mode with its own sound).
        /// </summary>
        public BeatGrid CountIn(double bpm, int beatsPerBar, bool keepTicking = true)
        {
            beatsPerBar = Math.Max(1, beatsPerBar);
            double spb = 60.0 / bpm;
            var grid = new BeatGrid(bpm, AudioSettings.dspTime + countInLeadSeconds + beatsPerBar * spb);
            Begin(grid, beatsPerBar);
            if (!keepTicking) _stopAtBeat = 0;
            return grid;
        }

        public void Stop()
        {
            Running = false;
            StopSources();
        }

        /// <summary>Change tempo without a gap: re-anchors so the next beat keeps its place.</summary>
        public void SetGrid(BeatGrid grid)
        {
            if (!Running) { Grid = grid; return; }
            Begin(grid, BeatsPerBar);
        }

        private void Update()
        {
            if (!Running || Grid == null) return;
            ScheduleAhead();

            // Visual pulse: dspTime is a touch behind on screen, which reads as "on the click".
            double now = AudioSettings.dspTime;
            while (_nextAnnounced < _nextScheduled && Grid.TimeOfBeat(_nextAnnounced) <= now)
            {
                int beat = (int)_nextAnnounced;
                _nextAnnounced++;
                if (Beat != null) Beat(beat);
                if (beat < 0 && beat >= -BeatsPerBar && CountInBeat != null) CountInBeat(beat + BeatsPerBar + 1);
            }

            if (_nextAnnounced >= _stopAtBeat) Stop();
        }

        private void ScheduleAhead()
        {
            double now = AudioSettings.dspTime;
            while (_nextScheduled < _stopAtBeat)
            {
                double t = Grid.TimeOfBeat(_nextScheduled);
                if (t > now + scheduleAheadSeconds) break;
                if (t >= now && !Muted) ScheduleTick(t, IsBarStart(_nextScheduled));
                _nextScheduled++;
            }
        }

        private bool IsBarStart(long beat)
        {
            long b = BeatsPerBar;
            return ((beat % b) + b) % b == 0;
        }

        private void ScheduleTick(double dspTime, bool accent)
        {
            var source = _sources[_nextSource];
            _nextSource = (_nextSource + 1) % SourceCount;
            source.clip = accent ? UiSounds.TickAccent : UiSounds.Tick;
            source.volume = Mathf.Clamp01(UiSounds.Volume * volume * (accent ? 1f : 0.8f));
            source.PlayScheduled(dspTime);
        }

        private void EnsureSources()
        {
            if (_sources != null) return;
            _sources = new AudioSource[SourceCount];
            for (int i = 0; i < SourceCount; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;   // a metronome reads best "in your head", not from a point in the room
                _sources[i] = s;
            }
        }

        private void StopSources()
        {
            if (_sources == null) return;
            foreach (var s in _sources) if (s != null) s.Stop();
        }

        private void OnDisable() { Stop(); }
    }
}

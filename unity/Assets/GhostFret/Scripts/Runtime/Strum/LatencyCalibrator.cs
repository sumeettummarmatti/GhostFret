using System;
using System.Collections.Generic;
using GhostFret.Core.Timing;
using GhostFret.Runtime.Audio;
using GhostFret.Runtime.Overlay;
using GhostFret.Runtime.UI;
using UnityEngine;

namespace GhostFret.Runtime.Strum
{
    /// <summary>
    /// "Strum on the four clicks" (DESIGN.md §11.4): one bar of count-in, then four clicks to strum on.
    /// The app forwards AudioCoach.StrumTimed into ReportStrum. The median delay (LatencyCalibration)
    /// goes into BeatGrid.LatencyMs, e.g. SongPlayer.LatencyMs. Optional: a sensible default works without it.
    /// </summary>
    public sealed class LatencyCalibrator : MonoBehaviour
    {
        public double bpm = 80;
        public int clicks = 4;
        public double listenAfterSeconds = 0.45;   // keep listening a moment after the last click

        public bool Running { get; private set; }
        public LatencyEstimate Last { get; private set; }

        private Metronome _metronome;
        private StrumLane _lane;
        private BeatGrid _grid;
        private Action<LatencyEstimate> _done;
        private readonly List<double> _strums = new List<double>();

        /// <summary>Starts the four-click check. lane is optional (shows arrows and the count).</summary>
        public void Begin(Metronome metronome, StrumLane lane, Action<LatencyEstimate> done)
        {
            _metronome = metronome != null ? metronome : gameObject.AddComponent<Metronome>();
            _lane = lane;
            _done = done;
            _strums.Clear();

            Coach.Say("Strum on the four clicks after the count.");
            _grid = _metronome.CountIn(bpm, clicks);
            _grid.LatencyMs = 0;
            _metronome.CountInBeat -= OnCount;
            _metronome.CountInBeat += OnCount;
            if (_lane != null) _lane.Play(new[] { "D" }, _grid, clicks);
            Running = true;
        }

        public void Cancel()
        {
            if (!Running) return;
            Finish(false);
        }

        /// <summary>Strum onset time in dspTime seconds (from AudioCoach.StrumTimed).</summary>
        public void ReportStrum(double dspTime)
        {
            if (!Running || _grid == null) return;
            double beat = _grid.BeatAt(dspTime);
            if (beat > -0.5 && beat < clicks - 0.5) _strums.Add(dspTime);
        }

        private void OnCount(int n)
        {
            if (_lane != null) _lane.ShowCount(n);
        }

        private void Update()
        {
            if (!Running || _grid == null) return;
            if (AudioSettings.dspTime > _grid.TimeOfBeat(clicks - 1) + listenAfterSeconds) Finish(true);
        }

        private void Finish(bool evaluate)
        {
            Running = false;
            if (_metronome != null) { _metronome.Stop(); _metronome.CountInBeat -= OnCount; }
            if (_lane != null) _lane.Stop();
            if (!evaluate) return;

            var clickTimes = new List<double>();
            for (int i = 0; i < clicks; i++) clickTimes.Add(_grid.TimeOfBeat(i));
            Last = LatencyCalibration.Estimate(clickTimes, _strums);

            if (Last.Ok) Coach.Say("Got it. Timing is tuned to your headset.", CoachTone.Good);
            else if (Last.Pairs < LatencyCalibration.MinPairs) Coach.Say("I didn't hear four strums. Let's try once more.", CoachTone.Hint);
            else Coach.Say("Close! Strum right on each click and we'll try again.", CoachTone.Hint);

            if (_done != null) _done(Last);
        }
    }
}

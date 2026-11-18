using System;
using GhostFret.Core.Content;
using GhostFret.Core.Feedback;
using GhostFret.Core.Strum;
using GhostFret.Runtime.UI;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// Song step: SongPlayer on the fitted (or Air) fretboard. SongPlayer draws the lane, its own controls
    /// and the kind end-of-song summary; this runner feeds it strums and records progress.
    /// Real guitar: report from StrumHeard (analysis lands ~170 ms after the onset) with LastStrumDspTime.
    /// Air mode: AirGuitar.Strummed, shapeLooksRight counts as clean.
    /// </summary>
    public sealed class SongRunner : MonoBehaviour
    {
        public float stopDetectGraceSeconds = 0.5f;

        private AppServices _s;
        private Action _done;
        private bool _active, _air, _finished;
        private float _startedAt;
        private SongChart _chart;

        /// <summary>Actively playing (used for the silence auto-pause).</summary>
        public bool IsPlaying { get { return _active && !_finished && _s.Songs.IsPlaying && !_s.Songs.IsPaused; } }

        public void Init(AppServices s)
        {
            _s = s;
            // Subscribed once; each handler ignores events unless this step is running.
            s.Audio.StrumHeard += OnHeard;
            s.Air.Strummed += OnAirStrum;
            s.Songs.ChordChanged += (current, next) => { if (_active) OnChordChanged(current); };
            s.Songs.Finished += r => { if (_active && !r.looped) OnFinished(r); };
            s.Songs.SummaryClosed += () => { if (_active) Complete(); };
        }

        public void Begin(SongChart chart, bool airMode, Action done)
        {
            _chart = chart;
            _air = airMode;
            _done = done;
            _active = true;
            _finished = false;
            _startedAt = Time.unscaledTime;

            float saved = _s.Progress.SongTempo(chart.id);
            if (saved > 0f) _s.Songs.SetTempoFactor(chart.id, saved);
            _s.Songs.Geometry = _s.Geometry;   // Air: the virtual guitar; the lane places itself beside it
            _s.Songs.Load(chart);
            if (!_air) _s.Audio.Listening = true;
            _s.Songs.Play();                    // ChordChanged fires now for the first chord
            Coach.Say("Here we go: " + chart.title + ". Follow the ghost hand.", CoachTone.Neutral, false);
        }

        public void SetPaused(bool paused)
        {
            if (!_active || _finished) return;
            if (paused) { _s.Songs.Pause(); if (!_air) _s.Audio.Listening = false; }
            else { _s.Songs.Resume(); if (!_air) _s.Audio.Listening = true; }
        }

        public void End()
        {
            bool stop = _active && _s.Songs.IsPlaying;
            _active = false;   // first, so anything raised by Stop() is ignored
            if (stop) _s.Songs.Stop();
            _s.Audio.Listening = false;
            _s.Ghost.Hide();
        }

        private void Update()
        {
            // The player pressed SongPlayer's own Stop button: move on rather than leave them stranded.
            if (_active && !_finished && Time.unscaledTime - _startedAt > stopDetectGraceSeconds && !_s.Songs.IsPlaying)
                Complete();
        }

        private void OnChordChanged(string current)
        {
            if (_finished && _s.Songs.IsPlaying)
            {
                // "Play again" on SongPlayer's summary started a new run.
                _finished = false;
                _startedAt = Time.unscaledTime;
                if (!_air) _s.Audio.Listening = true;
            }
            SetTarget(current);
        }

        private void SetTarget(string chord)
        {
            if (string.IsNullOrEmpty(chord)) return;
            if (_air) _s.Air.TargetChord = chord;
            else _s.Audio.TargetChord = chord;
        }

        private void OnHeard(ChordFeedback fb)
        {
            if (!_active || _air || _finished || fb == null) return;
            _s.Activity();
            double t = _s.Audio.LastStrumDspTime;
            StrumDirection dir = _s.Strum != null ? _s.Strum.DirectionAt(t) : StrumDirection.None;
            _s.Songs.ReportStrum(t, fb.Kind == FeedbackKind.Clean, fb.HeardChord, dir);
        }

        private void OnAirStrum(double dspTime, bool shapeLooksRight, string chord, StrumDirection dir)
        {
            if (!_active || !_air || _finished) return;
            _s.Activity();
            _s.Songs.ReportStrum(dspTime, shapeLooksRight, chord, dir);
        }

        private void OnFinished(SongResult r)
        {
            _finished = true;
            _s.Audio.Listening = false;
            // Air strums don't prove the sound, so only real-guitar runs update song bests and tempo.
            if (!_air) _s.Progress.RecordSong(r.songId, (float)r.cleanRate, (float)r.tempoFactor);
            _s.Summary.SongTitle = _chart != null ? _chart.title : r.songId;
            if ((float)r.cleanRate > _s.Summary.SongCleanRate) _s.Summary.SongCleanRate = (float)r.cleanRate;
            Celebration.Burst(Celebration.NearStrings(_s.Geometry));
            // SongPlayer shows the summary: "Play again" starts another run, "Done" raises SummaryClosed.
            if (!_s.Songs.showSummary) Complete();
        }

        private void Complete()
        {
            if (!_active) return;
            _finished = true;
            Action d = _done;
            _done = null;
            if (d != null) d();
        }
    }
}

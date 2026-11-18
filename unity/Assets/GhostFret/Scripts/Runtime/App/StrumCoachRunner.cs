using System;
using GhostFret.Core.Lesson;
using GhostFret.Core.Strum;
using GhostFret.Core.Timing;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// Strum Coach (DESIGN.md §7.3): one-bar count-in, then the day's pattern for a few bars.
    /// Timing comes from the mic onset (AudioCoach.StrumTimed), direction from StrumTracker.
    /// On pause the loop stops; resume restarts this short loop from the count-in.
    /// </summary>
    public sealed class StrumCoachRunner : MonoBehaviour
    {
        public float bpm = 70f;
        public float firstDayBpm = 60f;
        public int bars = 4;
        public float tipEverySeconds = 1.5f;   // one tip at a time, not a stream

        private AppServices _s;
        private StrumPractice _practice;
        private string[] _pattern;
        private string _chord;
        private Action _done;
        private bool _active, _paused, _playing;
        private float _lastTipAt = -10f;
        private AppPanel _panel;
        private TextMeshPro _title, _body;
        private PokeButton _again, _next;

        /// <summary>Actively playing (used for the silence auto-pause).</summary>
        public bool IsPlaying { get { return _active && _playing; } }

        public void Init(AppServices s)
        {
            _s = s;
            s.Audio.StrumTimed += OnStrumTimed;
            _panel = AppPanel.Create("Strum Coach", new Vector2(0.30f, 0.15f));
            _title = _panel.AddText("", Theme.TitleHeight, 0.04f);
            _body = _panel.AddText("", Theme.SmallHeight, 0.0f, Theme.TextDim);
            _again = _panel.AddButton("Again", -0.07f, -0.045f, AppPanel.SmallButton, StartLoop);
            _next = _panel.AddButton("Next", 0.07f, -0.045f, AppPanel.SmallButton, Finish);
        }

        public void Begin(string[] pattern, string chord, Action done)
        {
            _pattern = pattern;
            _chord = chord;
            _done = done;
            _active = true;
            _paused = false;
            _panel.Show(_s.Geometry);
            StartLoop();
        }

        public void SetPaused(bool paused)
        {
            if (!_active) return;
            _paused = paused;
            if (paused) StopLoop();
            else if (_practice != null && !_again.gameObject.activeSelf) StartLoop();
        }

        public void End()
        {
            _active = false;
            StopLoop();
            _s.Ghost.Hide();
            _panel.Hide();
        }

        private void StartLoop()
        {
            if (!_active) return;
            float tempo = _s.Day <= 1 ? firstDayBpm : bpm;
            double start = AppServices.DspNow + 0.3;
            _practice = new StrumPractice(_pattern, tempo, start, bars);

            // The metronome grid starts at the count-in, so its clicks line up with the practice grid.
            var clickGrid = new BeatGrid(tempo, start) { Subdivision = StrumPractice.Subdivision };
            _s.Metronome.Begin(clickGrid, _practice.BeatsPerBar);
            _s.Lane.Play(_practice.Pattern, _practice.Grid, _practice.BeatsPerBar);
            if (_s.Geometry != null) _s.Lane.PlaceBesideGuitar(_s.Geometry); else _s.Lane.PlaceInFront();
            _s.Audio.TargetChord = _chord;
            _s.Audio.Listening = true;
            if (_s.Geometry != null && GhostFret.Core.Music.ChordLibrary.Contains(_chord))
                _s.Ghost.ShowChord(GhostFret.Core.Music.ChordLibrary.Get(_chord), _s.Geometry);

            _playing = true;
            _title.text = "Strum along: " + PatternText(_practice.Pattern);
            _body.text = "Listen for " + _practice.BeatsPerBar + " clicks, then strum with the arrows.";
            _again.gameObject.SetActive(false);
            _next.gameObject.SetActive(true);   // always a way on
            Coach.Say("Listen for " + _practice.BeatsPerBar + " clicks, then join in.", CoachTone.Neutral, false);
        }

        private void StopLoop()
        {
            if (!_playing) return;
            _playing = false;
            _s.Metronome.Stop();
            _s.Lane.Stop();
            _s.Audio.Listening = false;
        }

        private void Update()
        {
            if (!_active || !_playing || _paused || _practice == null) return;
            if (!_practice.IsFinished(AppServices.DspNow)) return;

            StopLoop();
            int pct = (int)Math.Round(_practice.OnTimeRate * 100);
            if (_practice.Strums > 0 && pct > _s.Summary.StrumOnTimePercent) _s.Summary.StrumOnTimePercent = pct;
            string summary = _practice.Summary();
            Coach.Say(summary, pct >= 50 ? CoachTone.Good : CoachTone.Neutral);
            _title.text = summary;
            _body.text = "Again, or move on?";
            _again.gameObject.SetActive(true);
        }

        private void OnStrumTimed(double dspTime)
        {
            if (!_active || !_playing || _paused || _practice == null) return;
            _s.Activity();
            StrumDirection heard = _s.Strum != null ? _s.Strum.DirectionAt(dspTime) : StrumDirection.None;
            StrumCoachJudgement? j = _practice.Judge(dspTime, heard);
            if (!j.HasValue) return;

            _s.Lane.ShowStrum(j.Value.Timing, heard, j.Value.Expected);
            if (Time.unscaledTime - _lastTipAt < tipEverySeconds) return;
            _lastTipAt = Time.unscaledTime;
            Coach.Say(j.Value.Message, j.Value.Good ? CoachTone.Good : CoachTone.Hint, false);
        }

        private void Finish()
        {
            if (!_active) return;
            StopLoop();
            Action d = _done;
            _done = null;
            if (d != null) d();
        }

        private static string PatternText(string[] p)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string x in p) sb.Append(x == "D" ? "D" : x == "U" ? "U" : "-").Append(' ');   // letters, no arrow glyphs
            return sb.ToString().Trim();
        }
    }
}

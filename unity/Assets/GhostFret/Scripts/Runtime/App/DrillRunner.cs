using System;
using GhostFret.Core.Feedback;
using GhostFret.Core.Lesson;
using GhostFret.Core.Music;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// Change Drill (DESIGN.md §7.4): "How many clean switches between Em and D in 60 seconds?"
    /// The ghost hand morphs to the next shape as soon as the current one is heard clean.
    /// Uses its own clock that stops while paused, so resume is exact.
    /// </summary>
    public sealed class DrillRunner : MonoBehaviour
    {
        public float drillSeconds = 60f;
        public float morphSeconds = 0.25f;

        private AppServices _s;
        private ChangeDrill _drill;
        private Action _done;
        private bool _active, _paused, _running;
        private double _clock;
        private AppPanel _panel;
        private TextMeshPro _title, _body, _timer, _score;
        private PokeButton _primary, _secondary;

        /// <summary>Actively playing (used for the silence auto-pause).</summary>
        public bool IsPlaying { get { return _active && _running; } }

        public void Init(AppServices s)
        {
            _s = s;
            s.Audio.StrumHeard += OnStrum;
            _panel = AppPanel.Create("Change Drill", new Vector2(0.32f, 0.20f));
            _title = _panel.AddText("", Theme.TitleHeight, 0.07f);
            _body = _panel.AddText("", Theme.SmallHeight, 0.035f, Theme.TextDim);
            _timer = _panel.AddText("", Theme.TitleHeight, 0.0f);
            _score = _panel.AddText("", Theme.BodyHeight, -0.03f, Theme.Good);
            _primary = _panel.AddButton("Start", -0.07f, -0.07f, AppPanel.BigButton * 0.85f, OnPrimary);
            _secondary = _panel.AddButton("Next", 0.08f, -0.07f, AppPanel.SmallButton, Finish);
        }

        /// <param name="pair">"Em-D"</param>
        public bool Begin(string pair, Action done)
        {
            string a, b;
            if (!DayPlanner.TryParsePair(pair, out a, out b) || !ChordLibrary.Contains(a) || !ChordLibrary.Contains(b)) return false;
            _drill = new ChangeDrill(a, b, drillSeconds);
            _done = done;
            _active = true;
            _paused = false;
            ShowIntro();
            _panel.Show(_s.Geometry);
            return true;
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_active && _running) _s.Audio.Listening = !paused;
        }

        public void End()
        {
            _active = false;
            _running = false;
            _s.Audio.Listening = false;
            _s.Ghost.Hide();
            _panel.Hide();
        }

        private void ShowIntro()
        {
            _running = false;
            _title.text = "Change Drill: " + _drill.ChordA + " and " + _drill.ChordB;
            _body.text = "How many clean switches in " + Mathf.RoundToInt(drillSeconds) + " seconds?";
            int best = _s.Progress.BestDrill(_drill.PairKey);
            _timer.text = "";
            _score.text = best > 0 ? "Your best: " + best : "";
            _primary.Label.text = "Start";
            _primary.gameObject.SetActive(true);
            _secondary.gameObject.SetActive(true);   // "Next" = skip the drill today
            ShowShape(_drill.ChordA, false);
        }

        private void OnPrimary()
        {
            if (!_active || _running) return;
            _clock = 0;
            _drill.Start(_clock);
            _running = true;
            _primary.gameObject.SetActive(false);
            _secondary.gameObject.SetActive(false);
            _body.text = "Play " + _drill.ChordA + ", then switch. Clean changes count.";
            ShowShape(_drill.ExpectedChord, false);
            _s.Audio.Listening = !_paused;
            Coach.Say("Go! Start with " + _drill.ChordA + ".", CoachTone.Neutral, false);
        }

        private void Update()
        {
            if (!_active || !_running || _paused) return;
            _clock += Time.unscaledDeltaTime;
            _timer.text = Mathf.CeilToInt((float)_drill.TimeLeft(_clock)) + " s";
            _score.text = _drill.CleanChanges + " clean changes";
            if (_drill.IsTimeUp(_clock)) ShowResult();
        }

        private void OnStrum(ChordFeedback fb)
        {
            if (!_active || !_running || _paused || fb == null) return;
            _s.Activity();
            // Only clean target chords matter; anything else is quietly ignored (no wrong claims mid-drill).
            if (fb.Kind != FeedbackKind.Clean) return;
            string played = _drill.ExpectedChord;
            bool scored = _drill.OnCleanChord(played, _clock);
            _s.Progress.RecordChordAttempt(played, true);
            _s.Summary.CleanChords++;
            if (scored) Coach.Say("Clean change!", CoachTone.Good, false);
            ShowShape(_drill.ExpectedChord, true);
            UiSounds.Play(UiSounds.Chime, 0.4f);
        }

        private void ShowShape(string chord, bool morph)
        {
            _s.Audio.TargetChord = chord;
            if (_s.Geometry == null || !ChordLibrary.Contains(chord)) return;
            if (morph) _s.Ghost.MorphTo(ChordLibrary.Get(chord), _s.Geometry, morphSeconds);
            else _s.Ghost.ShowChord(ChordLibrary.Get(chord), _s.Geometry);
        }

        private void ShowResult()
        {
            _running = false;
            _s.Audio.Listening = false;
            int score = _drill.CleanChanges;
            bool best = _s.Progress.RecordDrill(_drill.PairKey, score);
            _s.Summary.DrillPair = _drill.PairKey;
            if (score > _s.Summary.DrillScore) _s.Summary.DrillScore = score;
            if (best && score > 0)
            {
                _s.Summary.DrillPersonalBest = true;
                Coach.Say("New personal best!", CoachTone.Good);
                Celebration.Burst(Celebration.NearStrings(_s.Geometry));
            }
            else Coach.Say(score > 0 ? "Nice switching! It gets faster every day." : "Good try. Slow, clean changes win.", CoachTone.Neutral, false);

            _title.text = (best && score > 0 ? "New personal best!" : "Time!");
            _body.text = _drill.ChordA + " and " + _drill.ChordB;
            _timer.text = score + " clean changes";
            _score.text = "Your best: " + _s.Progress.BestDrill(_drill.PairKey);
            _primary.Label.text = "Again";
            _primary.gameObject.SetActive(true);
            _secondary.gameObject.SetActive(true);
        }

        private void Finish()
        {
            if (!_active) return;
            Action d = _done;
            _done = null;
            if (d != null) d();
        }
    }
}

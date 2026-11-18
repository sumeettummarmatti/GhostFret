using System;
using System.Collections;
using GhostFret.Core.Feedback;
using GhostFret.Core.Lesson;
using GhostFret.Core.Music;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// Chord Coach (DESIGN.md §7.2): ghost hand shows the chord, the coach listens, one tip at a time.
    /// Audio decides correctness; specific tips only when Core feedback is confident (ChordLesson).
    /// 3 clean strums → small celebration → next chord.
    /// </summary>
    public sealed class LessonRunner : MonoBehaviour
    {
        public float celebrateSeconds = 2.2f;   // pause on the win before the next chord
        public float cleanGlowSeconds = 0.9f;
        public float problemGlowSeconds = 2.5f;
        public int offerSkipAfterAttempts = 6;  // never trap anyone in a noisy room
        public float generalTipEverySeconds = 8f; // unsure results: a gentle tip only now and then
        private float _lastGeneralTipAt = -100f;

        private AppServices _s;
        private ChordLesson _lesson;
        private Action _done;
        private bool _active, _paused, _celebrating, _firstEver;
        private int _attemptsThisChord;
        private AppPanel _panel;
        private TextMeshPro _title, _body, _count;
        private PokeButton _skip;

        /// <summary>Waiting for strums (used for the silence auto-pause).</summary>
        public bool IsPlaying { get { return _active && !_celebrating; } }

        public void Init(AppServices s)
        {
            _s = s;
            s.Audio.StrumHeard += OnStrum;   // subscribed once; ignored unless active
            _panel = AppPanel.Create("Chord Coach", new Vector2(0.30f, 0.17f));
            _title = _panel.AddText("", Theme.TitleHeight, 0.05f);
            _body = _panel.AddText("", Theme.SmallHeight, 0.012f, Theme.TextDim);
            _count = _panel.AddText("", Theme.BodyHeight, -0.025f, Theme.Good);
            _skip = _panel.AddButton("Skip for now", 0f, -0.062f, AppPanel.SmallButton, SkipChord);
        }

        public void Begin(ChordLesson lesson, Action done)
        {
            _lesson = lesson;
            _done = done;
            _active = true;
            _paused = false;
            _celebrating = false;
            _firstEver = _s.Progress.IsFirstTime;
            _panel.Show(_s.Geometry);
            ShowCurrent();
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_active) _s.Audio.Listening = !paused;
        }

        public void End()
        {
            _active = false;
            StopAllCoroutines();
            _s.Audio.Listening = false;
            _s.Ghost.Hide();
            _panel.Hide();
        }

        private void ShowCurrent()
        {
            string chord = _lesson.CurrentChord;
            _attemptsThisChord = 0;
            _skip.gameObject.SetActive(false);
            _title.text = (_lesson.Current.IsReview ? "Quick review: " : "Copy the ghost hand: ") + chord;
            _body.text = "Place your fingers on the dots, then strum all the strings it shows.";
            UpdateCount();

            if (ChordLibrary.Contains(chord) && _s.Geometry != null) _s.Ghost.ShowChord(ChordLibrary.Get(chord), _s.Geometry);
            _s.Audio.TargetChord = chord;
            _s.Audio.Listening = !_paused;
            Coach.Say("Let's play " + chord + ". Strum when you're ready.", CoachTone.Neutral, false);
        }

        private void UpdateCount()
        {
            // Plain words, no special glyphs: "Clean: 1 of 3".
            _count.text = _lesson.IsDone ? "" : "Clean: " + _lesson.CleanCount + " of " + _lesson.Current.CleanNeeded;
        }

        private void OnStrum(ChordFeedback fb)
        {
            if (!_active || _paused || _celebrating || _lesson == null || _lesson.IsDone) return;
            _s.Activity();

            LessonReaction r = _lesson.OnFeedback(fb);
            if (r == null) return;
            _s.Progress.RecordChordAttempt(r.Chord, r.WasClean);
            if (r.WasClean) _s.Summary.CleanChords++;
            _attemptsThisChord++;

            switch (r.Kind)
            {
                case LessonReactionKind.Clean:
                    GlowPlayedStrings(r.Chord);
                    Coach.Say("Clean " + r.Chord + "!", CoachTone.Good);
                    break;
                case LessonReactionKind.StringProblem:
                    _s.Overlay.HighlightString(r.StringIndex, Theme.Hint, problemGlowSeconds);
                    Coach.Say(r.Message, CoachTone.Hint);
                    break;
                case LessonReactionKind.WrongChord:
                    _s.Ghost.Pulse();
                    Coach.Say(r.Message, CoachTone.Hint);
                    break;
                case LessonReactionKind.GeneralTip:
                    // Not a correction: just an occasional, neutral nudge.
                    if (Time.unscaledTime - _lastGeneralTipAt >= generalTipEverySeconds)
                    {
                        _lastGeneralTipAt = Time.unscaledTime;
                        Coach.Say(r.Message, CoachTone.Neutral, false);
                    }
                    break;
                case LessonReactionKind.ChordLearned:
                case LessonReactionKind.LessonDone:
                    GlowPlayedStrings(r.Chord);
                    StartCoroutine(Celebrate(r));
                    break;
            }
            UpdateCount();
            if (_attemptsThisChord >= offerSkipAfterAttempts && !_celebrating) _skip.gameObject.SetActive(true);
        }

        private IEnumerator Celebrate(LessonReaction r)
        {
            _celebrating = true;
            _s.Audio.Listening = false;
            bool isNew = true;
            foreach (LessonStep st in _lesson.Steps) if (st.Chord == r.Chord) isNew = !st.IsReview;
            if (isNew) _s.Summary.NewChordLearned = r.Chord;

            string line = _firstEver ? "That's your first chord!" : "You've got " + r.Chord + "!";
            _firstEver = false;
            Coach.Say(line, CoachTone.Good);
            _title.text = line;
            _count.text = "";
            Celebration.Burst(Celebration.NearStrings(_s.Geometry));

            // Wait on unscaled time, but hold while paused.
            float t = 0f;
            while (t < celebrateSeconds) { if (!_paused) t += Time.unscaledDeltaTime; yield return null; }
            _celebrating = false;
            Next();
        }

        private void SkipChord()
        {
            if (!_active || _lesson.IsDone) return;
            Coach.Say("No problem. We'll come back to " + _lesson.CurrentChord + ".", CoachTone.Neutral, false);
            // Skipping just moves on; it never counts as learned.
            var rest = new System.Collections.Generic.List<LessonStep>();
            for (int i = _lesson.Index + 1; i < _lesson.Steps.Count; i++) rest.Add(_lesson.Steps[i]);
            _lesson = new ChordLesson(rest);
            Next();
        }

        private void Next()
        {
            if (!_active) return;
            if (_lesson.IsDone)
            {
                Action d = _done;
                _done = null;
                if (d != null) d();
                return;
            }
            ShowCurrent();
        }

        private void GlowPlayedStrings(string chord)
        {
            if (!ChordLibrary.Contains(chord)) return;
            ChordShape shape = ChordLibrary.Get(chord);
            for (int i = 0; i < 6; i++)
                if (shape.IsStringPlayed(i)) _s.Overlay.HighlightString(i, Theme.Good, cleanGlowSeconds);
        }
    }
}

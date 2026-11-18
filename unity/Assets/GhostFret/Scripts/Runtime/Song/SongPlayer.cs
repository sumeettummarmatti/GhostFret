using System;
using System.Collections;
using System.Collections.Generic;
using GhostFret.Core.Content;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Lesson;
using GhostFret.Core.Music;
using GhostFret.Core.Strum;
using GhostFret.Core.Timing;
using GhostFret.Runtime.Audio;
using GhostFret.Runtime.Content;
using GhostFret.Runtime.Overlay;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace GhostFret.Runtime.Song
{
    /// <summary>
    /// Runs a song (DESIGN.md §7.5): one bar of count-in, then a beat clock on AudioSettings.dspTime at the
    /// player's comfort tempo (AdaptiveTempo). The ghost hand morphs to the next chord one beat early,
    /// the strum lane scrolls the chart's pattern, sections can be looped, and Pause/Resume keep the exact
    /// position (with a one-bar lead-in back in). Strums come in through ReportStrum, so real-guitar mode
    /// (AudioCoach) and Air mode (AirGuitar) both work. At the end: a kind summary, AdaptiveTempo.RecordRun,
    /// and Finished(SongResult). Audio decides chord correctness (rule 4); hand tracking only the direction.
    /// </summary>
    public sealed class SongPlayer : MonoBehaviour
    {
        [Header("Tuning")]
        public double morphLeadBeats = 1.0;     // ghost hand shows the next chord this many beats early
        public float maxMorphSeconds = 0.35f;   // morph duration cap (shorter at fast tempos)
        public int resumeLeadInBeats = -1;      // clicks before play resumes; −1 = one bar
        public double startTempoFactor = 0.75;  // first-ever run of a song: 75% of the original tempo
        public float metronomeVolume = 0.8f;

        [Header("Behaviour")]
        public bool showControls = true;        // Pause / Loop section / Stop buttons under the lane
        public bool showSummary = true;         // kind summary panel at the end
        public bool autoPlaceLane = true;       // place the lane beside the guitar from Geometry on Play

        [SerializeField] private AudioSource backing;   // optional; created if a chart has a backing track

        // ---------- Public state ----------

        public SongChart Chart { get; private set; }
        public SongTimeline Timeline { get; private set; }

        /// <summary>Real fretboard or the Air guitar's geometry. Null = no ghost hand.</summary>
        public FretboardGeometry Geometry { get; set; }

        /// <summary>Measured mic delay (LatencyCalibrator), applied to strum timing.</summary>
        public double LatencyMs { get; set; }

        /// <summary>The current song's comfort tempo (one AdaptiveTempo per song id).</summary>
        public AdaptiveTempo Tempo { get { return TempoFor(Chart != null ? Chart.id : ""); } }

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public double Bpm { get; private set; }
        public BeatGrid Grid { get { return _grid; } }
        public SongRun Run { get { return _run; } }
        public string CurrentChord { get; private set; }
        public string NextChord { get; private set; }
        public double SongBeat { get { return _clock != null ? _clock.SongBeat : 0; } }
        public bool Looping { get { return _clock != null && _clock.Looping; } }

        public event Action<SongResult> Finished;
        /// <summary>(current, next). Fired on Play for the first chord, then at every change. next = "" at the end.</summary>
        public event Action<string, string> ChordChanged;
        /// <summary>The player closed the summary with "Done".</summary>
        public event Action SummaryClosed;

        // ---------- Private ----------

        private GhostHand _ghost;
        private StrumLane _lane;
        private Metronome _metronome;
        private readonly Dictionary<string, AdaptiveTempo> _tempos = new Dictionary<string, AdaptiveTempo>();

        private BeatGrid _grid;
        private SongClock _clock;
        private StrumPattern _pattern;
        private int _beatsPerBar = 4;
        private SongRun _run = new SongRun();
        private SongRun _passRun = new SongRun();

        private int _currentIndex = -1;
        private int _morphedFor = -1;
        private string _shownChord;
        private int _strumsThisVisit;
        private int _strumsNextVisit;
        private double _pausedRaw;
        private double _holdUntilRaw = double.NegativeInfinity;

        private AudioClip _backingClip;
        private bool _backingActive;

        private Transform _controls;
        private PokeButton _pauseButton;
        private readonly List<KeyValuePair<string, PokeButton>> _sectionButtons = new List<KeyValuePair<string, PokeButton>>();
        private Transform _summary;

        // ---------- Setup ----------

        /// <summary>Wire the pieces. Any argument may be null: a Metronome is created if missing.</summary>
        public void Init(GhostHand ghostHand, StrumLane lane, Metronome metronome)
        {
            _ghost = ghostHand;
            _lane = lane;
            if (_metronome != null) _metronome.CountInBeat -= OnCountIn;
            _metronome = metronome != null ? metronome : GetComponent<Metronome>();
            if (_metronome == null) _metronome = gameObject.AddComponent<Metronome>();
            _metronome.CountInBeat += OnCountIn;
        }

        private void Awake()
        {
            if (_metronome == null) Init(_ghost, _lane, null);
        }

        /// <summary>Restore a saved comfort tempo for a song (from on-device progress).</summary>
        public void SetTempoFactor(string songId, double factor)
        {
            TempoFor(songId).Restore(factor);
        }

        public AdaptiveTempo TempoFor(string songId)
        {
            if (songId == null) songId = "";
            AdaptiveTempo t;
            if (!_tempos.TryGetValue(songId, out t)) { t = new AdaptiveTempo(startTempoFactor); _tempos[songId] = t; }
            return t;
        }

        public void Load(SongChart chart)
        {
            Stop();
            CloseSummary();
            Chart = chart;
            if (chart == null) { Timeline = null; _clock = null; return; }

            Timeline = SongTimeline.Build(chart);
            _beatsPerBar = chart.GetBeatsPerBar();
            _pattern = StrumPattern.Parse(chart.strumPattern, _beatsPerBar);
            _clock = new SongClock(Timeline, _beatsPerBar);
            BuildControls();

            _backingClip = null;
            if (!string.IsNullOrEmpty(chart.backingTrack)) StartCoroutine(LoadBacking(chart.backingTrack));

            if (_lane != null && Timeline.Events.Count > 0)
                _lane.SetChords(Timeline.Events[0].Chord, Timeline.Events.Count > 1 ? Timeline.Events[1].Chord : "");
        }

        // ---------- Transport ----------

        public void Play()
        {
            if (Chart == null || Timeline == null || Timeline.Events.Count == 0)
            {
                Debug.LogWarning("GhostFret: SongPlayer.Play() with no song loaded.");
                return;
            }
            if (IsPlaying) StopInternal(false);
            CloseSummary();

            string loop = _clock != null && _clock.Looping ? _clock.LoopName : null;
            _clock = new SongClock(Timeline, _beatsPerBar);
            if (loop != null) _clock.LoopSection(loop);

            _run = new SongRun();
            _passRun = new SongRun();
            _strumsThisVisit = 0;
            _strumsNextVisit = 0;
            _holdUntilRaw = double.NegativeInfinity;
            _morphedFor = -1;
            _shownChord = null;

            Bpm = Tempo.CurrentBpm(Chart.originalBpm);
            _metronome.volume = metronomeVolume;
            _grid = _metronome.CountIn(Bpm, _beatsPerBar);
            _grid.Subdivision = _pattern.SlotsPerBeat;
            _grid.LatencyMs = LatencyMs;

            if (_lane != null)
            {
                if (autoPlaceLane && Geometry != null) _lane.PlaceBesideGuitar(Geometry);
                _lane.Play(Chart.strumPattern, _grid, _beatsPerBar);
            }

            IsPlaying = true;
            IsPaused = false;
            EnterEvent(Timeline.IndexAt(_clock.SongBeat), false);
            ShowControls(true);
            UpdateControlLabels();

            _backingActive = _backingClip != null && !_clock.Looping && Math.Abs(Tempo.Factor - 1.0) < 0.01;
            if (_backingActive) StartBacking(0, _clock.SongBeat);

            Coach.Say(Chart.title + " · " + Mathf.RoundToInt((float)Bpm) + " BPM. Count of " + _beatsPerBar + ", then strum.");
        }

        /// <summary>Freezes everything at the exact beat.</summary>
        public void Pause()
        {
            if (!IsPlaying || IsPaused || _grid == null) return;
            _pausedRaw = Math.Max(0, _grid.BeatAt(AudioSettings.dspTime));
            _metronome.Stop();
            if (_lane != null) _lane.Pause();
            if (backing != null) backing.Stop();
            IsPaused = true;
            UpdateControlLabels();
            Coach.Say("Paused. Resume whenever you're ready.");
        }

        /// <summary>One bar of clicks, then the song carries on from exactly where it paused.</summary>
        public void Resume()
        {
            if (!IsPlaying || !IsPaused || _grid == null) return;
            int lead = resumeLeadInBeats < 0 ? _beatsPerBar : resumeLeadInBeats;
            double startAt = AudioSettings.dspTime + _metronome.countInLeadSeconds;
            // Re-anchor the grid so raw beat (pausedRaw − lead) lands at startAt: the metronome, the lane
            // and strum timing all follow this one grid. The song clock holds still until pausedRaw.
            _grid.FirstBeatTime = startAt - (_pausedRaw - lead) * _grid.SecondsPerBeat;
            _holdUntilRaw = _pausedRaw;
            _metronome.Begin(_grid, _beatsPerBar);
            if (_lane != null) _lane.Resume();
            if (_backingActive) StartBacking(_pausedRaw, _clock.SongBeat);
            IsPaused = false;
            UpdateControlLabels();
        }

        /// <summary>Stops without a summary (the player left the song).</summary>
        public void Stop()
        {
            StopInternal(true);
        }

        /// <summary>Loop a section by name ("Verse"). Takes effect at the next bar line.</summary>
        public bool LoopSection(string name)
        {
            if (_clock == null) return false;
            if (!_clock.LoopSection(name)) return false;
            _passRun = new SongRun();
            _backingActive = false;
            if (backing != null) backing.Stop();
            UpdateControlLabels();
            if (IsPlaying) Coach.Say("Looping the " + _clock.LoopName.ToLowerInvariant() + ".");
            return true;
        }

        /// <summary>Leave the loop; the song carries on to the end.</summary>
        public void PlayWholeSong()
        {
            if (_clock == null) return;
            _clock.ClearLoop();
            UpdateControlLabels();
            if (IsPlaying) Coach.Say("Playing on to the end.");
        }

        // ---------- Strum input (real or Air) ----------

        /// <summary>
        /// A strum happened at dspTime. clean = audio heard the target chord cleanly (or Air mode's shape check);
        /// heardChord = what was heard ("" if unknown); direction from StrumTracker (None if unsure).
        /// </summary>
        public void ReportStrum(double dspTime, bool clean, string heardChord, StrumDirection direction)
        {
            if (!IsPlaying || IsPaused || _grid == null || _clock == null) return;

            StrumJudgement j = _pattern.Judge(_grid, dspTime);
            double rawBeat = j.Timing.NearestSlot;
            if (rawBeat < 0) return;                    // strumming along with the count-in: fine, not scored
            if (rawBeat < _holdUntilRaw) return;        // resume lead-in

            double songBeat = _clock.SongBeatAt(rawBeat);
            int ei = Timeline.IndexAt(songBeat);
            if (ei < 0) return;                         // past the end
            string expected = Timeline.Events[ei].Chord;

            // Audio is the judge. If it named a chord, it must be the expected one.
            bool ok = clean && (string.IsNullOrEmpty(heardChord) || heardChord == expected);
            TimingVerdict? timing = j.Extra ? (TimingVerdict?)null : j.Timing.Verdict;
            _run.RecordStrum(expected, ok, timing);
            _passRun.RecordStrum(expected, ok, timing);

            if (ei == _currentIndex) _strumsThisVisit++;
            else if (ei == _clock.NextIndex(_currentIndex)) _strumsNextVisit++;   // a hair early for the change

            if (_lane != null && !j.Extra) _lane.ShowStrum(j.Timing, direction, j.Expected);

            if (ok && SongRun.IsStreakMilestone(_run.CurrentStreak))
                Coach.Say(_run.CurrentStreak + " clean chords in a row!", CoachTone.Good);
        }

        // ---------- Frame update ----------

        private void Update()
        {
            FollowLane();
            if (!IsPlaying || IsPaused || _grid == null) return;

            double raw = _grid.BeatAt(DspClock.Now);
            if (raw < 0) return;   // count-in: the first chord is already showing

            int passes = _clock.LoopPasses;
            _clock.Advance(raw);
            if (_clock.LoopPasses != passes) OnLoopPass();

            if (_clock.IsFinished) { Finish(); return; }

            int idx = _clock.CurrentIndex;
            if (idx >= 0 && idx != _currentIndex) EnterEvent(idx, true);

            // Ghost hand moves to the next shape one beat before the change.
            int next = _clock.NextIndex(_currentIndex);
            if (next >= 0 && next != _morphedFor && _clock.BeatsUntil(next) <= morphLeadBeats)
            {
                _morphedFor = next;
                MorphGhost(Timeline.Events[next].Chord);
            }
        }

        private void EnterEvent(int idx, bool countSilence)
        {
            if (idx < 0 || idx >= Timeline.Events.Count) return;

            if (countSilence && _currentIndex >= 0 && _strumsThisVisit == 0)
                RecordSilence(Timeline.Events[_currentIndex].Chord);
            _strumsThisVisit = countSilence ? _strumsNextVisit : 0;
            _strumsNextVisit = 0;

            _currentIndex = idx;
            CurrentChord = Timeline.Events[idx].Chord;
            int next = _clock.NextIndex(idx);
            NextChord = next >= 0 ? Timeline.Events[next].Chord : "";

            if (_lane != null) _lane.SetChords(CurrentChord, NextChord);
            if (_morphedFor != idx) ShowGhost(CurrentChord);
            if (ChordChanged != null) ChordChanged(CurrentChord, NextChord);
        }

        private void RecordSilence(string chord)
        {
            _run.RecordSilentChord(chord);
            _passRun.RecordSilentChord(chord);
        }

        private void OnLoopPass()
        {
            if (_passRun.Strums > 0)
                Coach.Say("Loop " + _clock.LoopPasses + ": " + SongRun.Percent(_passRun.CleanRate) + "% clean. Again!");
            _passRun = new SongRun();
        }

        private void OnCountIn(int number)
        {
            if (IsPlaying && _lane != null) _lane.ShowCount(number);
        }

        // ---------- Ending ----------

        private void Finish()
        {
            if (_currentIndex >= 0 && _strumsThisVisit == 0) RecordSilence(Timeline.Events[_currentIndex].Chord);

            AdaptiveTempo tempo = Tempo;
            double before = tempo.Factor;
            if (_run.Strums > 0) tempo.RecordRun(_run.CleanRate);   // no strums at all: don't change the tempo
            SongResult result = _run.Result(Chart.id, tempo.Factor, false);

            StopInternal(true);
            if (_run.Strums > 0) Coach.Say(_run.Headline(), CoachTone.Good);
            else Coach.Say(_run.Headline());
            if (showSummary) ShowSummary(result, before);
            if (Finished != null) Finished(result);
        }

        private void StopInternal(bool hideGhost)
        {
            bool was = IsPlaying;
            IsPlaying = false;
            IsPaused = false;
            if (_metronome != null) _metronome.Stop();
            if (_lane != null && was) _lane.Stop();
            if (backing != null) backing.Stop();
            if (hideGhost && _ghost != null && was) _ghost.Hide();
            _shownChord = null;
            _currentIndex = -1;
            ShowControls(false);
        }

        private void OnDisable()
        {
            if (IsPlaying) StopInternal(true);
        }

        // ---------- Ghost hand ----------

        private void ShowGhost(string chord)
        {
            if (_ghost == null || Geometry == null || chord == _shownChord || !ChordLibrary.Contains(chord)) return;
            _ghost.ShowChord(ChordLibrary.Get(chord), Geometry);
            _shownChord = chord;
        }

        private void MorphGhost(string chord)
        {
            if (_ghost == null || Geometry == null || chord == _shownChord || !ChordLibrary.Contains(chord)) return;
            float seconds = Mathf.Min(maxMorphSeconds, (float)(_grid.SecondsPerBeat * 0.8));
            _ghost.MorphTo(ChordLibrary.Get(chord), Geometry, seconds);
            _shownChord = chord;
        }

        // ---------- Backing track (optional; metronome only is the normal case) ----------

        private IEnumerator LoadBacking(string path)
        {
            string lower = path.ToLowerInvariant();
            AudioType type = lower.EndsWith(".wav") ? AudioType.WAV : lower.EndsWith(".mp3") ? AudioType.MPEG : AudioType.OGGVORBIS;
            string url = ContentLoader.StreamingUrl(ContentLoader.Folder + "/" + path);
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) _backingClip = DownloadHandlerAudioClip.GetContent(request);
                else Debug.LogWarning("GhostFret: backing track not found, using the metronome only (" + path + ").");
            }
        }

        // The backing track is only used at the original tempo (no pitch-shifting), and never in loops.
        private void StartBacking(double rawBeat, double songBeat)
        {
            if (_backingClip == null) return;
            if (backing == null)
            {
                backing = gameObject.AddComponent<AudioSource>();
                backing.playOnAwake = false;
                backing.spatialBlend = 0f;
                backing.volume = 0.5f;   // moderate, so the mic still hears the guitar (DESIGN.md §11.5)
            }
            backing.Stop();
            backing.clip = _backingClip;
            backing.time = Mathf.Clamp((float)(Math.Max(0, songBeat) * _grid.SecondsPerBeat), 0f, Mathf.Max(0f, _backingClip.length - 0.01f));
            backing.PlayScheduled(_grid.TimeOfBeat(Math.Max(0, rawBeat)));
        }

        // ---------- Controls (hands only: poke or pinch) ----------

        private void BuildControls()
        {
            if (_controls != null) Destroy(_controls.gameObject);
            _sectionButtons.Clear();
            if (!showControls || Chart == null) { _controls = null; return; }

            var sections = Timeline.Sections;
            int buttons = 2 + sections.Count;               // Pause, Stop, one per section
            int rows = (buttons + 1) / 2;
            var buttonSize = new Vector2(0.105f, 0.04f);
            float rowH = 0.05f;
            var panelSize = new Vector2(0.24f, rows * rowH + 0.02f);

            _controls = UIKit.CreatePanel("Song controls", panelSize);
            int i = 0;
            Func<Vector3> nextPos = () =>
            {
                int col = i % 2, row = i / 2;
                i++;
                return new Vector3(col == 0 ? -0.058f : 0.058f, panelSize.y * 0.5f - 0.01f - rowH * 0.5f - row * rowH, -0.001f);
            };

            _pauseButton = UIKit.CreateButton(_controls, "Pause", nextPos(), buttonSize, () => { if (IsPaused) Resume(); else Pause(); });
            foreach (var s in sections)
            {
                string name = s.Name;
                var b = UIKit.CreateButton(_controls, LoopLabel(name), nextPos(), buttonSize, () => ToggleLoop(name));
                _sectionButtons.Add(new KeyValuePair<string, PokeButton>(name, b));
            }
            UIKit.CreateButton(_controls, "Stop", nextPos(), buttonSize, Stop);
            _controls.gameObject.SetActive(false);
        }

        private void ToggleLoop(string name)
        {
            if (_clock != null && _clock.Looping && string.Equals(_clock.LoopName, name, StringComparison.OrdinalIgnoreCase)) PlayWholeSong();
            else LoopSection(name);
        }

        private static string LoopLabel(string section)
        {
            return "Loop " + (section ?? "").ToLowerInvariant();
        }

        private void UpdateControlLabels()
        {
            if (_pauseButton != null && _pauseButton.Label != null) _pauseButton.Label.text = IsPaused ? "Resume" : "Pause";
            foreach (var pair in _sectionButtons)
            {
                if (pair.Value == null || pair.Value.Label == null) continue;
                bool looping = _clock != null && _clock.Looping && string.Equals(_clock.LoopName, pair.Key, StringComparison.OrdinalIgnoreCase);
                pair.Value.Label.text = looping ? "Whole song" : LoopLabel(pair.Key);
            }
        }

        private void ShowControls(bool show)
        {
            if (_controls == null) return;
            _controls.gameObject.SetActive(show && showControls);
            if (show) FollowLane();
        }

        // Controls sit just under the lane (or in front, lower right, if there is no lane).
        private void FollowLane()
        {
            if (_controls == null || !_controls.gameObject.activeSelf) return;
            if (_lane != null)
            {
                Transform lt = _lane.transform;
                _controls.position = lt.position - lt.up * (_lane.size.y * 0.5f + 0.045f) - lt.forward * 0.005f;
                _controls.rotation = lt.rotation;
            }
            else UIKit.PlaceInFront(_controls, 0.45f, 0.28f, 0.2f);
        }

        // ---------- Summary ----------

        private void ShowSummary(SongResult result, double factorBefore)
        {
            CloseSummary();
            var size = new Vector2(0.36f, 0.22f);
            _summary = UIKit.CreatePanel("Song summary", size);

            string title = Chart != null ? Chart.title : "Nice playing";
            UIKit.CreateText(_summary, title, Theme.TitleHeight, new Vector3(0, 0.08f, -0.001f), size.x * 0.92f);
            var headline = UIKit.CreateText(_summary, _run.Headline(), Theme.BodyHeight, new Vector3(0, 0.045f, -0.001f), size.x * 0.92f);
            headline.color = _run.Strums > 0 ? Theme.Good : Theme.Text;

            string detail = _run.TimedStrums > 0 ? SongRun.Percent(_run.TimingRate) + "% of strums on time." : "";
            string tempoNote = SongRun.TempoNote(factorBefore, result.tempoFactor);
            if (tempoNote.Length > 0) detail = detail.Length > 0 ? detail + " " + tempoNote : tempoNote;
            var d = UIKit.CreateText(_summary, detail, Theme.SmallHeight, new Vector3(0, 0.012f, -0.001f), size.x * 0.92f);
            d.color = Theme.TextDim;

            UIKit.CreateText(_summary, _run.Tip(), Theme.SmallHeight, new Vector3(0, -0.022f, -0.001f), size.x * 0.92f);

            var bs = new Vector2(0.14f, 0.045f);
            UIKit.CreateButton(_summary, "Play again", new Vector3(-0.08f, -0.072f, -0.001f), bs, Play);
            UIKit.CreateButton(_summary, "Done", new Vector3(0.08f, -0.072f, -0.001f), bs, () =>
            {
                CloseSummary();
                if (SummaryClosed != null) SummaryClosed();
            });

            if (_lane != null)
            {
                _summary.position = _lane.transform.position;
                UIKit.FacePlayer(_summary);
            }
            else UIKit.PlaceInFront(_summary, 0.45f, 0.15f);
            StartCoroutine(EaseIn(_summary));
        }

        private void CloseSummary()
        {
            if (_summary != null) Destroy(_summary.gameObject);
            _summary = null;
        }

        // A gentle grow-in (no flash): 92% → 100% over the theme's ease time.
        private static IEnumerator EaseIn(Transform t)
        {
            float start = Time.unscaledTime;
            while (t != null)
            {
                float k = Theme.Ease((Time.unscaledTime - start) / Theme.EaseSeconds);
                t.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, k);
                if (k >= 1f) yield break;
                yield return null;
            }
        }
    }
}

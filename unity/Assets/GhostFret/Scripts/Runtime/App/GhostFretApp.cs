using System;
using System.Collections;
using System.Collections.Generic;
using GhostFret.Core.Content;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Lesson;
using GhostFret.Core.Progress;
using GhostFret.Runtime.AirMode;
using GhostFret.Runtime.Audio;
using GhostFret.Runtime.Calibration;
using GhostFret.Runtime.Content;
using GhostFret.Runtime.Gestures;
using GhostFret.Runtime.Hands;
using GhostFret.Runtime.Overlay;
using GhostFret.Runtime.Progress;
using GhostFret.Runtime.Song;
using GhostFret.Runtime.Strum;
using GhostFret.Runtime.Tuner;
using GhostFret.Runtime.UI;
using GhostFret.Runtime.Util;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// The conductor, and the only component the scene needs (plus camera rig + passthrough).
    /// Creates any missing module on a child object, wires their events, and drives SessionFlow
    /// (DESIGN.md §8.2): Welcome → Calibrate → Tune → Lesson → Strum → Drill → Song → WrapUp,
    /// Air mode → WrapUp, and Paused from any playing state with an exact resume.
    /// First-time players get no menus: Start → Fit → Tune → first chord.
    /// </summary>
    public sealed class GhostFretApp : MonoBehaviour
    {
        [Header("Optional: leave empty and they are created automatically")]
        [SerializeField] private MonoBehaviour handInputBehaviour;   // MetaHandInput / EditorMouseHandInput
        [SerializeField] private MicrophoneInput microphone;
        [SerializeField] private FretboardCalibrator calibrator;
        [SerializeField] private FretboardOverlay overlay;
        [SerializeField] private GhostHand ghostHand;
        [SerializeField] private TunerPanel tuner;
        [SerializeField] private AudioCoach coach;
        [SerializeField] private StrumLane strumLane;
        [SerializeField] private Metronome metronome;
        [SerializeField] private StrumTracker strumTracker;
        [SerializeField] private SongPlayer songPlayer;
        [SerializeField] private PauseGesture pauseGesture;
        [SerializeField] private AirGuitar airGuitar;
        [SerializeField] private ContentLoader content;

        [Header("Tuning values")]
        public float lessonSilenceSeconds = 30f;  // beginners need time to place fingers
        public float playSilenceSeconds = 10f;    // strum coach, drill, song
        public float startDelaySeconds = 0.5f;    // let the head pose settle before placing the first card

        public SessionFlow Flow { get; private set; }
        public PracticeProgress Progress { get { return _s != null ? _s.Progress : null; } }

        private AppServices _s;
        private LessonRunner _lesson;
        private StrumCoachRunner _strum;
        private DrillRunner _drill;
        private SongRunner _song;
        private AirRunner _air;
        private WelcomePanel _welcome;
        private PausePanel _pause;
        private WrapUpPanel _wrapUp;
        private SettingsPanel _settings;
        private HelperPanel _helper;
        private Transform _captionAnchor;

        private BreakReminder _break;
        private bool _breakTipSaid;
        private bool _ready;
        private bool _lessonCompleted;
        private SongChart _chosenSong;
        private SessionState _pausedFrom;

        // ---------- Startup ----------

        private void Awake()
        {
            Flow = new SessionFlow();
            _s = new AppServices();
            _s.Progress = ProgressStore.Load();
            BuildModules();
            BuildRunnersAndPanels();
            WireEvents();
            ApplySettings();
#if UNITY_EDITOR
            // Editor-only preview (Runtime/EditorPreview): with no headset hands (mouse hands picked, so no
            // OVRHand in the scene) add a stand-in guitar, a dim room and keyboard shortcuts for every
            // feedback state. Never compiled into a device build.
            if (Application.isEditor && _s.Hands is EditorMouseHandInput)
                gameObject.AddComponent<GhostFret.Runtime.Preview.EditorPreview>().Init(this, _s);
#endif
        }

#if UNITY_EDITOR
        /// <summary>Editor preview only: the same as pressing a mode button on the Welcome card.</summary>
        public void ChooseModeForPreview(SessionMode mode)
        {
            OnModeChosen(mode);
        }
#endif

        private IEnumerator Start()
        {
            _break = new BreakReminder(Time.unscaledTime);
            yield return StartCoroutine(_s.Content.LoadCurriculum());
            yield return StartCoroutine(_s.Content.LoadAllSongs(_ => { }));
            yield return new WaitForSecondsRealtime(startDelaySeconds);
            _ready = true;
            ShowWelcome();
        }

        private T Ensure<T>(T existing, string name) where T : Component
        {
            if (existing != null) return existing;
            T found = GetComponentInChildren<T>(true);
            if (found != null) return found;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        private void BuildModules()
        {
            _s.Hands = PickHandInput();
            HandsHub.Input = _s.Hands;
            EnsureAudioListener();

            _s.Mic = microphone = Ensure(microphone, "Microphone");
            _s.Audio = coach = Ensure(coach, "Audio Coach");
            _s.Calibrator = calibrator = Ensure(calibrator, "Fretboard Calibrator");
            _s.Overlay = overlay = Ensure(overlay, "Fretboard Overlay");
            _s.Ghost = ghostHand = Ensure(ghostHand, "Ghost Hand");
            _s.Tuner = tuner = Ensure(tuner, "Tuner");
            _s.Lane = strumLane = Ensure(strumLane, "Strum Lane");
            _s.Metronome = metronome = Ensure(metronome, "Metronome");
            _s.Strum = strumTracker = Ensure(strumTracker, "Strum Tracker");
            _s.Songs = songPlayer = Ensure(songPlayer, "Song Player");
            _s.Pause = pauseGesture = Ensure(pauseGesture, "Pause Gesture");
            _s.Air = airGuitar = Ensure(airGuitar, "Air Guitar");
            _s.Content = content = Ensure(content, "Content");

            _s.Calibrator.Init(_s.Overlay);   // calibration previews on the shared overlay
            _s.Audio.Init(_s.Mic);
            _s.Tuner.Init(_s.Mic);
            _s.Songs.Init(_s.Ghost, _s.Lane, _s.Metronome);
            _s.Air.Init(_s.Overlay, _s.Ghost);
            _s.Pause.Init(_s.Hands);
        }

        /// <summary>Meta hands on device (or whenever OVRHand is in the scene); mouse hands in the Editor.</summary>
        private IHandInput PickHandInput()
        {
            var assigned = handInputBehaviour as IHandInput;
            if (assigned != null) return assigned;
#if GHOSTFRET_META_SDK
            if (!Application.isEditor || FindFirstObjectByType<OVRHand>() != null)
                return Ensure<MetaHandInput>(null, "Hands (Meta)");
#endif
            EditorMouseHandInput mouse = Ensure<EditorMouseHandInput>(null, "Hands (Editor mouse)");
            SetEditorCamera(mouse);
            return mouse;
        }

        private static void SetEditorCamera(EditorMouseHandInput mouse)
        {
            if (mouse.viewCamera == null) mouse.viewCamera = Camera.main;
        }

        private T AddRunner<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        private void BuildRunnersAndPanels()
        {
            _lesson = AddRunner<LessonRunner>("Chord Coach"); _lesson.Init(_s);
            _strum = AddRunner<StrumCoachRunner>("Strum Coach"); _strum.Init(_s);
            _drill = AddRunner<DrillRunner>("Change Drill"); _drill.Init(_s);
            _song = AddRunner<SongRunner>("Song Step"); _song.Init(_s);
            _air = AddRunner<AirRunner>("Air Step"); _air.Init(_s, _song);
            _welcome = AddRunner<WelcomePanel>("Welcome"); _welcome.Init();
            _pause = AddRunner<PausePanel>("Pause"); _pause.Init();
            _wrapUp = AddRunner<WrapUpPanel>("Wrap-up"); _wrapUp.Init();
            _settings = AddRunner<SettingsPanel>("Settings"); _settings.Init();
            _helper = AddRunner<HelperPanel>("Helper"); _helper.Init("Helper card");
            _captionAnchor = new GameObject("Caption Anchor").transform;
            _captionAnchor.SetParent(transform, false);
        }

        private void WireEvents()
        {
            _s.Calibrator.Fitted += OnFitted;
            _s.Calibrator.Refitted += OnRefitted;
            _s.Tuner.AllInTune += OnAllInTune;
            _s.Mic.PermissionDenied += OnMicDenied;

            _s.Pause.Paused += RequestPause;
            _s.Pause.SilenceNoticed += RequestPause;
            _s.Pause.Resumed += RequestResume;

            _welcome.ModeChosen += OnModeChosen;
            _welcome.SongChosen += song => StartSession(SessionMode.Song, song);
            _welcome.SettingsChosen += () => _settings.Show(_s.Progress.settings, _s.Geometry);

            _pause.ResumePressed += RequestResume;
            _pause.SettingsPressed += () => _settings.Show(_s.Progress.settings, _s.Geometry);
            _pause.FinishPressed += () => Fire(SessionTrigger.Finish);

            _wrapUp.PractiseAgain += () => Fire(SessionTrigger.Restart);
            _wrapUp.Done += () => Fire(SessionTrigger.Finish);

            _settings.Changed += _ => { ApplySettings(); Save(); };
            _settings.DeleteConfirmed += DeleteProgress;
        }

        // ---------- State machine ----------

        public bool Fire(SessionTrigger trigger)
        {
            SessionState before = Flow.State;
            if (!Flow.Fire(trigger)) return false;
            OnStateChanged(before, Flow.State);
            return true;
        }

        private void OnStateChanged(SessionState from, SessionState to)
        {
            UpdatePauseGesture(to);
            MaybeSayBreakTip(to);

            if (to == SessionState.Paused) { EnterPause(from); return; }
            if (from == SessionState.Paused)
            {
                _pause.Hide();
                _settings.Hide();
                _s.Pause.Disarm();
                if (to == _pausedFrom) { SetRunnerPaused(_pausedFrom, false); return; }   // exact resume
                ExitState(_pausedFrom);                                                  // "Finish for today"
                EnterState(to);
                return;
            }
            ExitState(from);
            EnterState(to);
        }

        private void EnterState(SessionState state)
        {
            _helper.Hide();
            switch (state)
            {
                case SessionState.Welcome: ShowWelcome(); break;
                case SessionState.Calibrate: EnterCalibrate(); break;
                case SessionState.Tune: EnterTune(); break;
                case SessionState.Lesson:
                    StartMic();
                    _lesson.Begin(TodaysLesson(), () => { _lessonCompleted = true; Fire(SessionTrigger.SkillLearned); });
                    break;
                case SessionState.StrumCoach:
                    StartMic();
                    _strum.Begin(TodaysPattern(), TodaysChord(), () => Fire(SessionTrigger.StrumDone));
                    break;
                case SessionState.Drill:
                    StartMic();
                    if (!_drill.Begin(DayPlanner.DrillPair(_s.Curriculum, _s.Day), () => Fire(SessionTrigger.DrillDone)))
                        Fire(SessionTrigger.Skip);
                    break;
                case SessionState.Song:
                    SongChart song = _chosenSong ?? PickTodaysSong();
                    if (song == null) { Fire(SessionTrigger.Skip); break; }
                    StartMic();
                    _song.Begin(song, false, () => Fire(SessionTrigger.SongDone));
                    break;
                case SessionState.AirMode: EnterAir(); break;
                case SessionState.WrapUp: EnterWrapUp(); break;
                case SessionState.Done: _wrapUp.ShowGoodbye(_s.Geometry); break;
            }
        }

        private void ExitState(SessionState state)
        {
            switch (state)
            {
                case SessionState.Welcome: _welcome.Hide(); _settings.Hide(); break;
                case SessionState.Calibrate: if (_s.Fitted == null) _s.Calibrator.Cancel(); break;
                case SessionState.Tune: _s.Tuner.End(); break;
                case SessionState.Lesson: _lesson.End(); break;
                case SessionState.StrumCoach: _strum.End(); break;
                case SessionState.Drill: _drill.End(); break;
                case SessionState.Song: _song.End(); break;
                case SessionState.AirMode:
                    _air.End();
                    PlaceCaptionAnchor(_s.Fitted);
                    if (_s.Fitted != null && _s.Strum != null) _s.Strum.Init(_s.Fitted.AcrossAxis.ToUnity());
                    break;
                case SessionState.WrapUp:
                case SessionState.Done: _wrapUp.Hide(); break;
            }
            _helper.Hide();
        }

        // ---------- Session start / steps ----------

        private void ShowWelcome()
        {
            if (!_ready) return;
            _s.Ghost.Hide();
            if (_s.Progress.IsFirstTime) _welcome.ShowFirstTime(_s.Fitted);
            else _welcome.ShowReturning(_s.Progress.StreakAsOf(DateTime.Now), DayPlanner.DayLine(_s.Curriculum, _s.Day),
                DayPlanner.DrillPair(_s.Curriculum, _s.Day), _s.Fitted);
        }

        private void OnModeChosen(SessionMode mode)
        {
            if (Flow.State != SessionState.Welcome) return;
            if (mode == SessionMode.Song) { _welcome.ShowSongs(UnlockedSongs(), ShowWelcome, _s.Fitted); return; }
            StartSession(mode, null);
        }

        private void StartSession(SessionMode mode, SongChart song)
        {
            if (Flow.State != SessionState.Welcome) return;
            _s.Progress.MarkSession(DateTime.Now);
            _s.Summary.Clear();
            _lessonCompleted = false;
            _chosenSong = song;

            SessionOptions options = mode == SessionMode.Guided
                ? DayPlanner.GuidedOptions(_s.Curriculum, _s.Day, _s.Fitted != null, PickTodaysSong() != null)
                : new SessionOptions { FretboardFitted = _s.Fitted != null };

            // Ask for the microphone now, right after the Welcome card said why we listen,
            // instead of surprising the player with the system dialog mid-calibration.
            if (mode != SessionMode.Air) StartMic();

            SessionState before = Flow.State;
            if (Flow.Begin(mode, options)) OnStateChanged(before, Flow.State);
        }

        private void EnterCalibrate()
        {
            _s.Calibrator.Begin(_s.Hands);
            Coach.Say("Let's fit the fretboard to your guitar.", CoachTone.Neutral, false);
            _helper.Show("Touch the glowing spots on your guitar.", "No guitar? Air mode", () => Fire(SessionTrigger.NoGuitar),
                null, null, null);
        }

        private void EnterTune()
        {
            StartMic();
            _s.Tuner.Begin(_s.Fitted);
            Coach.Say("Pluck each string. I'll help you tune.", CoachTone.Neutral, false);
        }

        private void EnterAir()
        {
            _s.Mic.StopListening();   // Air mode needs no microphone
            SongChart song = _s.Progress.airDay1Done ? (_chosenSong ?? PickTodaysSong()) : null;
            _air.Begin(song, () => Fire(SessionTrigger.Finish));
            if (_s.Strum != null) _s.Strum.Init(_s.Air.StrumAxis);
            PlaceCaptionAnchor(_s.Geometry);
        }

        private void EnterWrapUp()
        {
            _s.Mic.StopListening();
            _s.Ghost.Hide();
            if (Flow.Mode == SessionMode.Guided && _lessonCompleted)
                _s.Progress.CompleteDay(DateTime.Now, DayPlanner.LastDay(_s.Curriculum));
            _s.Progress.sessionsCompleted++;
            Save();

            string streak = SessionSummary.StreakLine(_s.Progress.streakDays);
            string tip = _break != null && _break.Elapsed(Time.unscaledTime) >= _break.AfterSeconds ? BreakReminder.Message : null;
            _wrapUp.Show(streak, _s.Summary.BestLine(), DayPlanner.TomorrowGoal(_s.Curriculum, _s.Day), tip, _s.Geometry);
            Coach.Say(streak, CoachTone.Good);
        }

        private ChordLesson TodaysLesson()
        {
            Curriculum c = _s.Curriculum;
            int day = _s.Day;
            return ChordLesson.ForDay(DayPlanner.NewChords(c, day), DayPlanner.PreviousDayChords(c, day), DayPlanner.LearnedBefore(c, day));
        }

        private string[] TodaysPattern()
        {
            CurriculumDay d = DayPlanner.Day(_s.Curriculum, _s.Day);
            return d != null ? d.strumPattern : null;   // StrumPractice falls back to quarter downs
        }

        private string TodaysChord()
        {
            string[] fresh = DayPlanner.NewChords(_s.Curriculum, _s.Day);
            return fresh.Length > 0 ? fresh[0] : "Em";
        }

        private List<SongChart> AllSongs()
        {
            var list = new List<SongChart>();
            if (_s.Content.Songs != null) foreach (SongChart s in _s.Content.Songs) list.Add(s);
            return list;
        }

        private List<SongChart> UnlockedSongs()
        {
            var list = new List<SongChart>();
            var unlocked = _s.Content.UnlockedSongs(DayPlanner.LearnedBy(_s.Curriculum, _s.Day));
            if (unlocked != null) foreach (SongChart s in unlocked) list.Add(s);
            return list;
        }

        private SongChart PickTodaysSong()
        {
            return DayPlanner.PickSong(_s.Curriculum, _s.Day, AllSongs(), DayPlanner.LearnedBy(_s.Curriculum, _s.Day));
        }

        // ---------- Module events ----------

        private void OnFitted(FretboardGeometry g)
        {
            UseFittedGeometry(g);
            Coach.Say("Fretboard fitted!", CoachTone.Good);
            if (Flow.State == SessionState.Calibrate) Fire(SessionTrigger.CalibrationDone);
        }

        private void OnRefitted(FretboardGeometry g)
        {
            UseFittedGeometry(g);
        }

        private void UseFittedGeometry(FretboardGeometry g)
        {
            _s.Fitted = g;
            if (Flow.State == SessionState.AirMode || (Flow.State == SessionState.Paused && _pausedFrom == SessionState.AirMode)) return;
            _s.Geometry = g;
            _s.Overlay.Show(g);   // glides into place on a re-fit
            _s.Songs.Geometry = g;
            if (_s.Ghost.IsVisible && _s.Ghost.Chord != null) _s.Ghost.ShowChord(_s.Ghost.Chord, g);
            if (_s.Strum != null)
            {
                _s.Strum.Init(g.AcrossAxis.ToUnity());   // low E → high E; positive = down-strum
                _s.Strum.LeftHanded = _s.Progress.settings.leftHanded;
            }
            PlaceCaptionAnchor(g);
            AppPanel.PlaceAllVisible(g);
        }

        private void OnAllInTune()
        {
            if (Flow.State != SessionState.Tune) return;
            // TunerPanel has its own "Skip tuning" button; AllInTune fires for that too.
            if (_s.Tuner.WasSkipped) Coach.Say("No problem. We can tune another time.", CoachTone.Neutral, false);
            else Coach.Say("All in tune!", CoachTone.Good);
            Fire(SessionTrigger.TuningDone);
        }

        private void OnMicDenied()
        {
            Coach.Say("I need the microphone to hear your guitar. Air mode works without it.", CoachTone.Neutral, false);
            _helper.Show("No microphone? You can still practise in Air mode.",
                "Try Air mode", () => { _helper.Hide(); Fire(SessionTrigger.NoGuitar); },
                "Not now", () => _helper.Hide(), _s.Geometry);
        }

        private void StartMic()
        {
            if (!_s.Mic.IsRunning) _s.Mic.StartListening();
        }

        // ---------- Pause ----------

        private void RequestPause()
        {
            if (SessionFlow.IsPausable(Flow.State)) Fire(SessionTrigger.Pause);
        }

        private void RequestResume()
        {
            _pausedBySystem = false;   // any resume (button, palm, system) clears the system-pause flag
            if (Flow.State == SessionState.Paused) Fire(SessionTrigger.Resume);
        }

        private void EnterPause(SessionState from)
        {
            _pausedFrom = from;
            SetRunnerPaused(from, true);
            _pause.Show(_s.Geometry);
            _s.Pause.Disarm();
            Coach.Say("Paused. Take your time.", CoachTone.Neutral, false);
            Save();
        }

        private void SetRunnerPaused(SessionState state, bool paused)
        {
            switch (state)
            {
                case SessionState.Lesson: _lesson.SetPaused(paused); break;
                case SessionState.StrumCoach: _strum.SetPaused(paused); break;
                case SessionState.Drill: _drill.SetPaused(paused); break;
                case SessionState.Song: _song.SetPaused(paused); break;
                case SessionState.AirMode: _air.SetPaused(paused); break;
            }
        }

        private void UpdatePauseGesture(SessionState state)
        {
            _s.Pause.IsPaused = state == SessionState.Paused;
            _s.Pause.GestureEnabled = state == SessionState.Paused || SessionFlow.IsPausable(state);
        }

        private void Update()
        {
            if (!_ready) return;
            SessionState st = Flow.State;

            // Silence auto-pause only while actually playing (not on intro or result cards).
            bool playing = (st == SessionState.Lesson && _lesson.IsPlaying) || (st == SessionState.StrumCoach && _strum.IsPlaying)
                || (st == SessionState.Drill && _drill.IsPlaying) || (st == SessionState.Song && _song.IsPlaying);
            _s.Pause.silenceSeconds = st == SessionState.Lesson ? lessonSilenceSeconds : playSilenceSeconds;
            _s.Pause.WatchSilence = playing;
            // No accidental re-fits while a song or drill is running.
            _s.Calibrator.refitEnabled = st != SessionState.Song && st != SessionState.Drill;
        }

        private void MaybeSayBreakTip(SessionState to)
        {
            if (_breakTipSaid || _break == null || to == SessionState.Song || to == SessionState.Paused) return;
            if (!_break.ShouldSuggest(Time.unscaledTime)) return;
            _breakTipSaid = true;
            Coach.Say(BreakReminder.Message, CoachTone.Neutral, false);
        }

        // ---------- Settings + saving ----------

        private void ApplySettings()
        {
            PracticeSettings set = _s.Progress.settings;
            Theme.OverlayBrightness = set.overlayBrightness;
            _s.Metronome.volume = set.metronomeVolume;
            _s.Songs.metronomeVolume = set.metronomeVolume;
            // Left-handed goes to every mirrored piece together.
            _s.Calibrator.LeftHanded = set.leftHanded;
            _s.Overlay.LeftHanded = set.leftHanded;
            _s.Ghost.LeftHanded = set.leftHanded;
            if (_s.Strum != null) _s.Strum.LeftHanded = set.leftHanded;
            if (_s.Fitted != null && _s.Geometry == _s.Fitted) _s.Overlay.Show(_s.Fitted);   // redraw at the new brightness
            // Air mode reads left-handedness when it begins (AirRunner passes it to AirGuitar.Begin).
        }

        private void DeleteProgress()
        {
            PracticeSettings keep = _s.Progress.settings;   // accessibility choices stay; history goes
            ProgressStore.DeleteAll();
            _s.Progress = new PracticeProgress { settings = keep };
            _s.Summary.Clear();
            if (Flow.State == SessionState.Welcome) { _settings.Hide(); ShowWelcome(); }
        }

        private void Save()
        {
            ProgressStore.Save(_s.Progress);
        }

        // True when the headset (system menu, a permission dialog) paused us, not the player.
        private bool _pausedBySystem;

        private void PauseForSystem()
        {
            if (!_ready) return;
            Save();
            // Android's microphone dialog takes focus: don't pause for that, or the player
            // comes back from "Allow" to a pause card and the mic seems broken.
            if (_s.Mic != null && _s.Mic.IsAskingPermission) return;
            if (Flow.State == SessionState.Paused) return;
            if (!SessionFlow.IsPausable(Flow.State)) return;
            _pausedBySystem = true;
            RequestPause();
        }

        private void ResumeFromSystem()
        {
            // Coming back from the Meta menu: pick up exactly where we were, no extra tap needed.
            if (!_pausedBySystem) return;
            _pausedBySystem = false;
            RequestResume();
        }

        private void OnApplicationPause(bool paused) { if (paused) PauseForSystem(); else ResumeFromSystem(); }
        private void OnApplicationFocus(bool focused) { if (!focused) PauseForSystem(); else ResumeFromSystem(); }

        /// <summary>
        /// Without an AudioListener Unity plays nothing at all. Meta's camera rig doesn't always have one,
        /// so put one on the player's eye camera if the scene has none.
        /// </summary>
        private static void EnsureAudioListener()
        {
            if (FindFirstObjectByType<AudioListener>() != null) return;
            Camera cam = Camera.main;
            if (cam == null)
            {
                foreach (Camera c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    if (c.enabled && c.stereoTargetEye != StereoTargetEyeMask.None) { cam = c; break; }
            }
            GameObject host = cam != null ? cam.gameObject : new GameObject("GhostFret Audio Listener");
            host.AddComponent<AudioListener>();
            Debug.Log("[GhostFret] Added an AudioListener to " + host.name + " (the scene had none).");
        }
        private void OnApplicationQuit() { if (_s != null) Save(); }

        // ---------- Placement ----------

        /// <summary>Captions sit just above the panels beside the guitar (or in front when there's no guitar).</summary>
        private void PlaceCaptionAnchor(FretboardGeometry g)
        {
            if (g == null) { Coach.Anchor = null; return; }
            // Same spot as the cards, then straight up past the tallest card (0.30 m), so a caption
            // never sits on top of a card (seen overlapping on device).
            UIKit.PlaceBesideGuitar(_captionAnchor, g);
            _captionAnchor.position += Vector3.up * 0.19f;
            UIKit.FacePlayer(_captionAnchor);
            Coach.Anchor = _captionAnchor;
        }
    }
}

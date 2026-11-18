using System.Collections.Generic;

namespace GhostFret.Core.Lesson
{
    public enum SessionState { Welcome, Calibrate, Tune, Lesson, Drill, Song, WrapUp, Paused, AirMode, Done, StrumCoach }

    public enum SessionTrigger
    {
        Start, CalibrationDone, NoGuitar, TuningDone, SkillLearned,
        DrillDone, SongDone, Finish, Pause, Resume,
        StrumDone,  // Strum Coach loop finished
        Skip,       // "Skip" / "Next" button: leave the current step without finishing it
        Restart     // "Practise again" from WrapUp or Done: back to Welcome
    }

    /// <summary>What the player picked on the Welcome card.</summary>
    public enum SessionMode { Guided, ChordCoach, StrumCoach, ChangeDrill, Song, Tuner, Air }

    /// <summary>Which optional steps today's guided session has (e.g. Day 1 has no change drill).</summary>
    public sealed class SessionOptions
    {
        public bool FretboardFitted;      // already fitted this run → skip Calibrate
        public bool IncludeStrum = true;
        public bool IncludeDrill = true;
        public bool IncludeSong = true;
    }

    /// <summary>
    /// The session state machine from DESIGN.md §8.2.
    /// A session is a list of steps built from the chosen mode; each "done" trigger moves to the next
    /// step, and the last step leads to WrapUp. Guided = Calibrate → Tune → Lesson → Strum → Drill → Song.
    /// Pause remembers where you were and Resume goes back there exactly.
    /// </summary>
    public sealed class SessionFlow
    {
        public SessionState State { get; private set; }
        public SessionMode Mode { get; private set; }
        /// <summary>The state we paused from (valid while State == Paused).</summary>
        public SessionState PausedFrom { get { return _beforePause; } }

        private SessionState _beforePause;
        private readonly List<SessionState> _steps = new List<SessionState>();
        private int _stepIndex;

        // Which trigger finishes which step.
        private static readonly Dictionary<SessionState, SessionTrigger> DoneTrigger = new Dictionary<SessionState, SessionTrigger>
        {
            { SessionState.Calibrate, SessionTrigger.CalibrationDone },
            { SessionState.Tune, SessionTrigger.TuningDone },
            { SessionState.Lesson, SessionTrigger.SkillLearned },
            { SessionState.StrumCoach, SessionTrigger.StrumDone },
            { SessionState.Drill, SessionTrigger.DrillDone },
            { SessionState.Song, SessionTrigger.SongDone },
            { SessionState.AirMode, SessionTrigger.Finish },
        };

        private static readonly HashSet<SessionState> Pausable = new HashSet<SessionState>
        {
            SessionState.Tune, SessionState.Lesson, SessionState.StrumCoach,
            SessionState.Drill, SessionState.Song, SessionState.AirMode
        };

        public SessionFlow()
        {
            State = SessionState.Welcome;
        }

        public static bool IsPausable(SessionState s) { return Pausable.Contains(s); }

        /// <summary>The steps of the current session, in order (for "Step 2 of 5").</summary>
        public IList<SessionState> Steps { get { return _steps.AsReadOnly(); } }
        public int StepIndex { get { return _stepIndex; } }

        /// <summary>Starts a session from Welcome. Returns false if not on Welcome.</summary>
        public bool Begin(SessionMode mode, SessionOptions options)
        {
            if (State != SessionState.Welcome) return false;
            if (options == null) options = new SessionOptions();
            Mode = mode;
            _steps.Clear();
            BuildSteps(mode, options);
            _stepIndex = 0;
            State = _steps[0];
            return true;
        }

        private void BuildSteps(SessionMode mode, SessionOptions o)
        {
            if (mode == SessionMode.Air) { _steps.Add(SessionState.AirMode); return; }
            if (mode == SessionMode.Tuner) { _steps.Add(SessionState.Tune); return; }

            if (!o.FretboardFitted) _steps.Add(SessionState.Calibrate);
            switch (mode)
            {
                case SessionMode.Guided:
                    _steps.Add(SessionState.Tune);
                    _steps.Add(SessionState.Lesson);
                    if (o.IncludeStrum) _steps.Add(SessionState.StrumCoach);
                    if (o.IncludeDrill) _steps.Add(SessionState.Drill);
                    if (o.IncludeSong) _steps.Add(SessionState.Song);
                    break;
                case SessionMode.ChordCoach: _steps.Add(SessionState.Lesson); break;
                case SessionMode.StrumCoach: _steps.Add(SessionState.StrumCoach); break;
                case SessionMode.ChangeDrill: _steps.Add(SessionState.Drill); break;
                case SessionMode.Song: _steps.Add(SessionState.Song); break;
            }
        }

        /// <summary>Applies a trigger. Returns false (and changes nothing) if it isn't allowed now.</summary>
        public bool Fire(SessionTrigger trigger)
        {
            switch (trigger)
            {
                case SessionTrigger.Start:
                    return Begin(SessionMode.Guided, new SessionOptions());

                case SessionTrigger.Pause:
                    if (!Pausable.Contains(State)) return false;
                    _beforePause = State;
                    State = SessionState.Paused;
                    return true;

                case SessionTrigger.Resume:
                    if (State != SessionState.Paused) return false;
                    State = _beforePause;
                    return true;

                case SessionTrigger.NoGuitar:
                    // From Welcome, or any guitar step (e.g. mic permission denied): switch to Air mode.
                    if (State == SessionState.Welcome) return Begin(SessionMode.Air, null);
                    if (State == SessionState.WrapUp || State == SessionState.Done ||
                        State == SessionState.AirMode || State == SessionState.Paused) return false;
                    Mode = SessionMode.Air;
                    _steps.Clear();
                    _steps.Add(SessionState.AirMode);
                    _stepIndex = 0;
                    State = SessionState.AirMode;
                    return true;

                case SessionTrigger.Finish:
                    if (State == SessionState.WrapUp) { State = SessionState.Done; return true; }
                    if (State == SessionState.Paused) { State = SessionState.WrapUp; return true; } // "Finish for today"
                    break;

                case SessionTrigger.Restart:
                    if (State != SessionState.WrapUp && State != SessionState.Done) return false;
                    State = SessionState.Welcome;
                    _steps.Clear();
                    _stepIndex = 0;
                    return true;

                case SessionTrigger.Skip:
                    if (_stepIndex < _steps.Count && _steps[_stepIndex] == State) { Advance(); return true; }
                    return false;
            }

            SessionTrigger done;
            if (_stepIndex < _steps.Count && _steps[_stepIndex] == State &&
                DoneTrigger.TryGetValue(State, out done) && done == trigger)
            {
                Advance();
                return true;
            }
            return false;
        }

        private void Advance()
        {
            _stepIndex++;
            State = _stepIndex < _steps.Count ? _steps[_stepIndex] : SessionState.WrapUp;
        }
    }
}

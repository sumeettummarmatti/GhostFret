using System.Collections.Generic;

namespace GhostFret.Core.Lesson
{
    public enum SessionState { Welcome, Calibrate, Tune, Lesson, Drill, Song, WrapUp, Paused, AirMode, Done }

    public enum SessionTrigger
    {
        Start, CalibrationDone, NoGuitar, TuningDone, SkillLearned,
        DrillDone, SongDone, Finish, Pause, Resume
    }

    /// <summary>
    /// The session state machine from DESIGN.md §8.2.
    /// Pause remembers where you were and Resume goes back there.
    /// </summary>
    public sealed class SessionFlow
    {
        public SessionState State { get; private set; }
        private SessionState _beforePause;

        private static readonly Dictionary<SessionState, Dictionary<SessionTrigger, SessionState>> Transitions =
            new Dictionary<SessionState, Dictionary<SessionTrigger, SessionState>>
            {
                { SessionState.Welcome,   new Dictionary<SessionTrigger, SessionState> { { SessionTrigger.Start, SessionState.Calibrate } } },
                { SessionState.Calibrate, new Dictionary<SessionTrigger, SessionState> {
                    { SessionTrigger.CalibrationDone, SessionState.Tune },
                    { SessionTrigger.NoGuitar, SessionState.AirMode } } },
                { SessionState.Tune,      new Dictionary<SessionTrigger, SessionState> { { SessionTrigger.TuningDone, SessionState.Lesson } } },
                { SessionState.Lesson,    new Dictionary<SessionTrigger, SessionState> { { SessionTrigger.SkillLearned, SessionState.Drill } } },
                { SessionState.Drill,     new Dictionary<SessionTrigger, SessionState> { { SessionTrigger.DrillDone, SessionState.Song } } },
                { SessionState.Song,      new Dictionary<SessionTrigger, SessionState> { { SessionTrigger.SongDone, SessionState.WrapUp } } },
                { SessionState.AirMode,   new Dictionary<SessionTrigger, SessionState> { { SessionTrigger.Finish, SessionState.WrapUp } } },
                { SessionState.WrapUp,    new Dictionary<SessionTrigger, SessionState> { { SessionTrigger.Finish, SessionState.Done } } },
            };

        private static readonly HashSet<SessionState> Pausable = new HashSet<SessionState>
        {
            SessionState.Tune, SessionState.Lesson, SessionState.Drill, SessionState.Song, SessionState.AirMode
        };

        public SessionFlow()
        {
            State = SessionState.Welcome;
        }

        /// <summary>Applies a trigger. Returns false (and changes nothing) if it isn't allowed now.</summary>
        public bool Fire(SessionTrigger trigger)
        {
            if (trigger == SessionTrigger.Pause)
            {
                if (!Pausable.Contains(State)) return false;
                _beforePause = State;
                State = SessionState.Paused;
                return true;
            }
            if (trigger == SessionTrigger.Resume)
            {
                if (State != SessionState.Paused) return false;
                State = _beforePause;
                return true;
            }

            Dictionary<SessionTrigger, SessionState> options;
            SessionState next;
            if (Transitions.TryGetValue(State, out options) && options.TryGetValue(trigger, out next))
            {
                State = next;
                return true;
            }
            return false;
        }
    }
}

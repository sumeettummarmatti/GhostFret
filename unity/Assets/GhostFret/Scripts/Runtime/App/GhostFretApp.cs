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
using GhostFret.Runtime.Tuner;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// The conductor: owns SessionFlow and switches components on and off per state (DESIGN.md §8.2).
    /// Welcome → Calibrate (or AirMode) → Tune → Lesson → Drill → Song → WrapUp.
    /// TODO (Phase 6): wire each state's enter/exit; Pause/Resume from PauseGesture;
    ///                 MarkSession + save progress at WrapUp.
    /// </summary>
    public sealed class GhostFretApp : MonoBehaviour
    {
        [Header("Hands (MetaHandInput on device, EditorMouseHandInput in Editor)")]
        [SerializeField] private MonoBehaviour handInputBehaviour;

        [Header("Components")]
        [SerializeField] private MicrophoneInput microphone;
        [SerializeField] private FretboardCalibrator calibrator;
        [SerializeField] private FretboardOverlay overlay;
        [SerializeField] private GhostHand ghostHand;
        [SerializeField] private TunerPanel tuner;
        [SerializeField] private AudioCoach coach;
        [SerializeField] private StrumLane strumLane;
        [SerializeField] private SongPlayer songPlayer;
        [SerializeField] private PauseGesture pauseGesture;
        [SerializeField] private AirGuitar airGuitar;
        [SerializeField] private ContentLoader content;

        public SessionFlow Flow { get; private set; }
        public PracticeProgress Progress { get; private set; }
        private IHandInput Hands { get { return handInputBehaviour as IHandInput; } }

        private void Start()
        {
            Flow = new SessionFlow();
            Progress = ProgressStore.Load();
            Progress.MarkSession(System.DateTime.Now);
            StartCoroutine(content.LoadCurriculum());
            // TODO: show Welcome panel ("Grab your guitar and sit comfortably. Pinch to start.")
            //       with a second button: "No guitar? Try Air mode".
        }

        public void Fire(SessionTrigger trigger)
        {
            SessionState before = Flow.State;
            if (!Flow.Fire(trigger)) return;
            OnStateChanged(before, Flow.State);
        }

        private void OnStateChanged(SessionState from, SessionState to)
        {
            // TODO: enable/disable components for each state.
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) { Fire(SessionTrigger.Pause); ProgressStore.Save(Progress); }
        }
    }
}

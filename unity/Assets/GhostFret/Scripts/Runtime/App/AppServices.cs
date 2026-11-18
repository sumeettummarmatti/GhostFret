using GhostFret.Core.Content;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Progress;
using GhostFret.Runtime.AirMode;
using GhostFret.Runtime.Audio;
using GhostFret.Runtime.Calibration;
using GhostFret.Runtime.Content;
using GhostFret.Runtime.Gestures;
using GhostFret.Runtime.Hands;
using GhostFret.Runtime.Overlay;
using GhostFret.Runtime.Song;
using GhostFret.Runtime.Strum;
using GhostFret.Runtime.Tuner;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>Shared references every step runner needs. Built once by GhostFretApp.</summary>
    public sealed class AppServices
    {
        public IHandInput Hands;
        public MicrophoneInput Mic;
        public AudioCoach Audio;
        public FretboardCalibrator Calibrator;
        public FretboardOverlay Overlay;
        public GhostHand Ghost;
        public TunerPanel Tuner;
        public StrumLane Lane;
        public Metronome Metronome;
        public StrumTracker Strum;
        public SongPlayer Songs;
        public AirGuitar Air;
        public ContentLoader Content;
        public PauseGesture Pause;

        public PracticeProgress Progress;
        public readonly SessionSummary Summary = new SessionSummary();

        /// <summary>The real fitted fretboard (null until fitted).</summary>
        public FretboardGeometry Fitted;
        /// <summary>What panels and the ghost hand use right now: fitted guitar, or the Air guitar.</summary>
        public FretboardGeometry Geometry;

        public Curriculum Curriculum { get { return Content != null ? Content.Curriculum : null; } }
        public int Day { get { return Progress.currentDay; } }
        public static double DspNow { get { return AudioSettings.dspTime; } }

        /// <summary>Any strum counts as activity, so the silence auto-pause waits again.</summary>
        public void Activity()
        {
            if (Pause != null) Pause.NotifyActivity();
        }
    }
}

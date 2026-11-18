using System;
using GhostFret.Core.Audio;
using GhostFret.Core.Feedback;
using GhostFret.Core.Music;
using UnityEngine;

namespace GhostFret.Runtime.Audio
{
    /// <summary>
    /// Listens for strums and turns them into ChordFeedback (DESIGN.md §11).
    /// TODO (Phase 3): run OnsetDetector on 1024-sample hops; on onset, wait ~170 ms,
    ///                 grab 8192 samples and call ChordAnalyzer.Analyze; raise StrumHeard.
    /// TODO (Phase 4): also raise StrumTimed(onsetTime) for the Strum Coach.
    /// </summary>
    public sealed class AudioCoach : MonoBehaviour
    {
        [SerializeField] private MicrophoneInput microphone;

        public string TargetChord = "Em";
        public event Action<ChordFeedback> StrumHeard;
        public event Action<double> StrumTimed;

        private ChordAnalyzer _analyzer;
        private OnsetDetector _onsets;

        private void Awake()
        {
            _analyzer = new ChordAnalyzer(GuitarTuning.Standard);
            _onsets = new OnsetDetector();
        }

        private void Update()
        {
            // TODO (Phase 3): onset detection + delayed chord analysis.
        }

        private void Raise(ChordFeedback feedback) { if (StrumHeard != null) StrumHeard(feedback); }
        private void RaiseTimed(double t) { if (StrumTimed != null) StrumTimed(t); }
    }
}

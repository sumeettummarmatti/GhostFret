using GhostFret.Core.Audio;
using GhostFret.Core.Music;
using GhostFret.Runtime.Audio;
using UnityEngine;

namespace GhostFret.Runtime.Tuner
{
    /// <summary>
    /// Guitar-tuner panel floating at the headstock (DESIGN.md §7.1).
    /// TODO (Phase 1): every ~100 ms grab 4096 samples → YinPitchDetector → GuitarTuning.NearestString;
    ///                 smooth; show dial + "Tune up a little" / "Tune down" / "Perfect" (±5 cents);
    ///                 mark each string done; raise AllInTune when six strings are green.
    /// </summary>
    public sealed class TunerPanel : MonoBehaviour
    {
        [SerializeField] private MicrophoneInput microphone;
        public double inTuneCents = 5;

        public event System.Action AllInTune;

        private readonly YinPitchDetector _yin = new YinPitchDetector();
        private GuitarTuning _tuning = GuitarTuning.Standard;

        private void Update() { /* TODO */ }
    }
}

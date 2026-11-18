using System;
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.Hands;
using UnityEngine;

namespace GhostFret.Runtime.Calibration
{
    /// <summary>
    /// "Fit the fretboard": three 1-second fingertip touches → FretboardGeometry (DESIGN.md §10.2).
    /// TODO (Phase 2):
    ///  1. Show one glowing target hint at a time: nut (low E), nut (high E), 12th fret (low E).
    ///  2. While the index tip is still (< 5 mm movement), fill a ring over 1 s; chime on success.
    ///  3. Build FretboardGeometry.FromCalibration; if !IsPlausible, show the reason and restart.
    ///  4. Quick re-fit: touching the nut for 1 s later shifts the model (keep angle).
    /// </summary>
    public sealed class FretboardCalibrator : MonoBehaviour
    {
        public float holdSeconds = 1f;
        public float stillnessMeters = 0.005f;

        public event Action<FretboardGeometry> Fitted;
        public FretboardGeometry Current { get; private set; }

        private IHandInput _hands;

        public void Begin(IHandInput hands)
        {
            _hands = hands;
            // TODO: reset step, show first target.
        }

        private void Update()
        {
            // TODO: dwell detection per step.
        }

        private void Complete(FretboardGeometry geometry)
        {
            Current = geometry;
            if (Fitted != null) Fitted(geometry);
        }
    }
}

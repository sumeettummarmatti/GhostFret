using System;
using GhostFret.Runtime.Hands;
using UnityEngine;

namespace GhostFret.Runtime.Gestures
{
    /// <summary>
    /// Pause = palm facing up, held still for 1 s, NOT pinching.
    /// Never uses the reserved system gesture (palm toward headset + pinch).
    /// Also pauses after silenceSeconds without a strum (wired from AudioCoach).
    /// TODO (Phase 6): implement both triggers; raise Paused / Resumed.
    /// </summary>
    public sealed class PauseGesture : MonoBehaviour
    {
        public float holdSeconds = 1f;
        public float silenceSeconds = 10f;
        public float palmUpDot = 0.8f;   // dot(palmNormal, world up)

        public event Action Paused;
        public event Action Resumed;

        private IHandInput _hands;
        public void Init(IHandInput hands) { _hands = hands; }

        private void Update() { /* TODO */ }
    }
}

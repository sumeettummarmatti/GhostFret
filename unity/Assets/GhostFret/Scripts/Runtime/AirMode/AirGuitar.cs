using GhostFret.Runtime.Hands;
using UnityEngine;

namespace GhostFret.Runtime.AirMode
{
    /// <summary>
    /// Air Guitar mode (DESIGN.md §7.6): a virtual guitar at lap height for players without one,
    /// and for judges. Must let a full session be completed with hands only.
    /// TODO (Phase 7): chord shape from finger-joint pose (approximate); strum when the wrist
    ///                 crosses the virtual strings; play sampled chord audio; reuse StrumLane + SongPlayer.
    /// UI text: "Air mode practises shapes and rhythm. Real-guitar mode checks your actual sound."
    /// </summary>
    public sealed class AirGuitar : MonoBehaviour
    {
        private IHandInput _hands;
        public void Begin(IHandInput hands) { _hands = hands; }
        public void End() { }
    }
}

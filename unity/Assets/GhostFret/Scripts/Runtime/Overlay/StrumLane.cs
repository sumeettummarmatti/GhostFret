using GhostFret.Core.Timing;
using UnityEngine;

namespace GhostFret.Runtime.Overlay
{
    /// <summary>
    /// Vertical lane beside the guitar body; ↓/↑ arrows scroll to a hit line in time (DESIGN.md §7.3).
    /// TODO (Phase 4): spawn arrows from the strum pattern on the BeatGrid; show
    ///                 "On time" / "A little early" / "A little late" / "Missed the up-strum".
    /// </summary>
    public sealed class StrumLane : MonoBehaviour
    {
        public void Play(string[] pattern, BeatGrid grid) { /* TODO */ }
        public void ShowJudgement(TimingResult result) { /* TODO */ }
        public void Stop() { /* TODO */ }
    }
}

using GhostFret.Core.Fretboard;
using GhostFret.Core.Music;
using UnityEngine;

namespace GhostFret.Runtime.Overlay
{
    /// <summary>
    /// Shows where fingers go: numbered dots (1–4) at FretboardGeometry.FingerPosition,
    /// × over skipped strings, ○ over open strings (DESIGN.md §7.2).
    /// v1: dots. v1.5: translucent hand mesh posed to the chord.
    /// TODO (Phase 3): build dots; morph to the next chord one beat early in Song Mode.
    /// TODO (Phase 8): mirror for left-handed mode.
    /// </summary>
    public sealed class GhostHand : MonoBehaviour
    {
        public void ShowChord(ChordShape chord, FretboardGeometry geometry) { /* TODO */ }
        public void Hide() { /* TODO */ }
    }
}

using GhostFret.Core.Fretboard;
using UnityEngine;

namespace GhostFret.Runtime.Overlay
{
    /// <summary>
    /// Draws frets (0–12) and six strings on the real neck (DESIGN.md §10.5, docs/UX_GUIDE.md).
    /// TODO (Phase 2): LineRenderers from FretboardGeometry.FretLine / StringPoint, lifted ~3 mm
    ///                 along SurfaceNormal; soft white, 40% opacity.
    /// TODO (Phase 3): HighlightString(index, colour) for clean / problem feedback.
    /// </summary>
    public sealed class FretboardOverlay : MonoBehaviour
    {
        public int fretCount = 12;
        public float liftMeters = 0.003f;

        public void Show(FretboardGeometry geometry) { /* TODO */ }
        public void HighlightString(int stringIndex, Color colour, float seconds) { /* TODO */ }
        public void Hide() { gameObject.SetActive(false); }
    }
}

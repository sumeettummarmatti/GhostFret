using UnityEngine;

namespace GhostFret.Runtime.Hands
{
    public enum Handedness { Left, Right }

    /// <summary>
    /// Everything GhostFret needs from hand tracking. Implemented by MetaHandInput on device
    /// and EditorMouseHandInput in the Editor. Keeps Meta SDK types out of the rest of the code.
    /// </summary>
    public interface IHandInput
    {
        bool IsTracked(Handedness hand);
        Vector3 IndexTip(Handedness hand);
        Vector3 Wrist(Handedness hand);
        /// <summary>Direction the palm faces (out of the palm).</summary>
        Vector3 PalmNormal(Handedness hand);
        bool IsPinching(Handedness hand);
        /// <summary>0..1 confidence; use for "hints only when confident".</summary>
        float Confidence(Handedness hand);
    }
}

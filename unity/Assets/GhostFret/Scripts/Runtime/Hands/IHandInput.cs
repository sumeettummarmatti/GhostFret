using UnityEngine;

namespace GhostFret.Runtime.Hands
{
    public enum Handedness { Left, Right }

    /// <summary>Finger numbers used by IHandInput.FingerTip / FingerCurl (same as chord finger numbers, plus the thumb).</summary>
    public static class Finger
    {
        public const int Thumb = 0;
        public const int Index = 1;
        public const int Middle = 2;
        public const int Ring = 3;
        public const int Pinky = 4;
    }

    /// <summary>
    /// Everything GhostFret needs from hand tracking. Implemented by MetaHandInput on device
    /// and EditorMouseHandInput in the Editor. Keeps Meta SDK types out of the rest of the code.
    /// All positions are world space. Fretting-finger data is for hints only: audio decides correctness.
    /// </summary>
    public interface IHandInput
    {
        bool IsTracked(Handedness hand);
        Vector3 IndexTip(Handedness hand);
        Vector3 Wrist(Handedness hand);
        /// <summary>Direction the palm faces (out of the palm).</summary>
        Vector3 PalmNormal(Handedness hand);
        /// <summary>Index–thumb pinch. Never true during the system gesture (palm toward headset + pinch).</summary>
        bool IsPinching(Handedness hand);
        /// <summary>0..1 confidence; use for "hints only when confident".</summary>
        float Confidence(Handedness hand);

        /// <summary>Fingertip position. finger: 0 thumb, 1 index, 2 middle, 3 ring, 4 pinky.</summary>
        Vector3 FingerTip(Handedness hand, int finger);
        /// <summary>0 = straight, 1 = fully curled. finger: 0 thumb, 1 index, 2 middle, 3 ring, 4 pinky.</summary>
        float FingerCurl(Handedness hand, int finger);
    }
}

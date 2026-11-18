#if GHOSTFRET_META_SDK
using UnityEngine;

namespace GhostFret.Runtime.Hands
{
    /// <summary>
    /// IHandInput backed by the Meta XR SDK (OVRHand / OVRSkeleton).
    /// Enabled by the GHOSTFRET_META_SDK scripting define (see docs/SETUP_UNITY.md).
    /// TODO (Phase 2): assign OVRHand + OVRSkeleton for each hand and read bone transforms.
    /// TODO: add the Meta SDK assembly to GhostFret.Runtime.asmdef references.
    /// </summary>
    public sealed class MetaHandInput : MonoBehaviour, IHandInput
    {
        [SerializeField] private OVRHand leftHand;
        [SerializeField] private OVRHand rightHand;
        [SerializeField] private OVRSkeleton leftSkeleton;
        [SerializeField] private OVRSkeleton rightSkeleton;

        private OVRHand Hand(Handedness h) { return h == Handedness.Left ? leftHand : rightHand; }

        public bool IsTracked(Handedness h) { return Hand(h) != null && Hand(h).IsTracked; }
        public bool IsPinching(Handedness h) { return Hand(h) != null && Hand(h).GetFingerIsPinching(OVRHand.HandFinger.Index); }
        public float Confidence(Handedness h) { return Hand(h) != null && Hand(h).HandConfidence == OVRHand.TrackingConfidence.High ? 1f : 0.3f; }

        public Vector3 IndexTip(Handedness h) { return Vector3.zero; /* TODO: OVRSkeleton.BoneId.Hand_IndexTip */ }
        public Vector3 Wrist(Handedness h) { return Vector3.zero; /* TODO: OVRSkeleton.BoneId.Hand_WristRoot */ }
        public Vector3 PalmNormal(Handedness h) { return Vector3.up; /* TODO: from wrist + metacarpal bones */ }
    }
}
#endif

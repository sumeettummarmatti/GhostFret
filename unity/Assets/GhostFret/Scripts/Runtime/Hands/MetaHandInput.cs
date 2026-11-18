#if GHOSTFRET_META_SDK
using System.Collections.Generic;
using GhostFret.Core.Hands;
using GhostFret.Runtime.Util;
using UnityEngine;

namespace GhostFret.Runtime.Hands
{
    /// <summary>
    /// IHandInput backed by the Meta XR SDK (OVRHand + OVRSkeleton, OpenXR hand skeleton).
    /// Enabled by the GHOSTFRET_META_SDK scripting define (see docs/SETUP_UNITY.md).
    ///
    /// Works when added by code: if the hands/skeletons aren't assigned, it finds them in the scene
    /// (OVRHand.GetHand() / OVRSkeleton.GetSkeletonType()) and keeps retrying until they appear.
    ///
    /// Safety (DESIGN.md §13.3): pinch is reported false while the system gesture is in progress
    /// or while the palm faces the headset, so app actions can never ride on the system menu gesture.
    /// Nothing here is stored or logged: every value is read live from the current frame.
    /// </summary>
    public sealed class MetaHandInput : MonoBehaviour, IHandInput
    {
        public OVRHand leftHand;
        public OVRHand rightHand;
        public OVRSkeleton leftSkeleton;
        public OVRSkeleton rightSkeleton;

        /// <summary>How often to search the scene for missing hands (seconds).</summary>
        public float findRetrySeconds = 1f;
        /// <summary>dot(palm normal, direction to head) above this = palm toward the headset → no app pinch.</summary>
        public float palmTowardHeadDot = 0.6f;
        /// <summary>
        /// Hide the SDK's hand meshes so the player sees their real hands in passthrough. On device the
        /// default hand mesh drew as an opaque blob over the real hand and the ghost dots.
        /// Tracking keeps working; only the renderers are switched off.
        /// </summary>
        public bool hideHandMeshes = true;
        private float _nextHide;

        // Joint slots we care about, filled from OVRSkeleton bones by id.
        private const int Wrist_ = 0;
        private const int SlotCount = 1 + 5 * 5;   // wrist + 5 fingers × (metacarpal, proximal, intermediate, distal, tip)

        private readonly Transform[][] _joints = { new Transform[SlotCount], new Transform[SlotCount] };
        private readonly int[] _boneCount = { -1, -1 };
        private readonly List<Vector3> _chain = new List<Vector3>(5);
        private readonly List<System.Numerics.Vector3> _chainN = new List<System.Numerics.Vector3>(5);
        private float _nextFind;
        private bool _warnedLegacy;

        private void Awake() { FindMissing(); }

        private void Update()
        {
            if ((leftHand == null || rightHand == null || leftSkeleton == null || rightSkeleton == null) && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + findRetrySeconds;
                FindMissing();
            }
            // The SDK creates hand mesh renderers lazily, so keep checking once a second.
            if (hideHandMeshes && Time.unscaledTime >= _nextHide)
            {
                _nextHide = Time.unscaledTime + 1f;
                HideHandVisuals();
            }
        }

        private void HideHandVisuals()
        {
            HideRenderersUnder(leftHand);
            HideRenderersUnder(rightHand);
            HideRenderersUnder(leftSkeleton);
            HideRenderersUnder(rightSkeleton);
            // Hand meshes that live on other objects (building-block hand visuals).
            foreach (OVRMeshRenderer m in FindObjectsByType<OVRMeshRenderer>(FindObjectsSortMode.None))
                HideRenderersUnder(m);
            foreach (OVRSkeletonRenderer s in FindObjectsByType<OVRSkeletonRenderer>(FindObjectsSortMode.None))
                if (s.enabled) s.enabled = false;
        }

        private static void HideRenderersUnder(Component c)
        {
            if (c == null) return;
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) r.enabled = false;
        }

        // ---------- IHandInput ----------

        public bool IsTracked(Handedness h)
        {
            OVRHand hand = Hand(h);
            if (hand == null || !hand.IsTracked) return false;
            OVRSkeleton s = Skeleton(h);
            return s != null && s.IsInitialized && s.IsDataValid && Joint(h, Wrist_) != null;
        }

        public bool IsPinching(Handedness h)
        {
            OVRHand hand = Hand(h);
            if (hand == null || !hand.IsTracked) return false;
            if (hand.IsSystemGestureInProgress) return false;
            if (PalmFacesHead(h)) return false;
            return hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        }

        public float Confidence(Handedness h)
        {
            if (!IsTracked(h)) return 0f;
            OVRHand hand = Hand(h);
            OVRSkeleton s = Skeleton(h);
            bool high = hand.HandConfidence == OVRHand.TrackingConfidence.High && s.IsDataHighConfidence;
            return high ? 1f : 0.3f;
        }

        public Vector3 IndexTip(Handedness h) { return FingerTip(h, Finger.Index); }

        public Vector3 Wrist(Handedness h)
        {
            Transform t = Joint(h, Wrist_);
            return t != null ? t.position : Vector3.zero;
        }

        public Vector3 FingerTip(Handedness h, int finger)
        {
            Transform t = Joint(h, Slot(finger, 4));
            return t != null ? t.position : Vector3.zero;
        }

        public Vector3 PalmNormal(Handedness h)
        {
            // The far ends of the index and little metacarpals are the proximal (knuckle) joints.
            Transform w = Joint(h, Wrist_);
            Transform i = Joint(h, Slot(Finger.Index, 1));
            Transform l = Joint(h, Slot(Finger.Pinky, 1));
            if (w == null || i == null || l == null) return Vector3.down;
            System.Numerics.Vector3 n = HandMath.PalmNormal(
                w.position.ToNumerics(), i.position.ToNumerics(), l.position.ToNumerics(), h == Handedness.Left);
            return n == System.Numerics.Vector3.Zero ? Vector3.down : n.ToUnity();
        }

        public float FingerCurl(Handedness h, int finger)
        {
            if (finger < 0 || finger > 4) return 0f;
            _chain.Clear();
            // Thumb: metacarpal, proximal, distal, tip (no intermediate). Others: all five.
            for (int k = 0; k < 5; k++)
            {
                if (finger == Finger.Thumb && k == 2) continue;
                Transform t = Joint(h, Slot(finger, k));
                if (t == null) return 0f;
                _chain.Add(t.position);
            }
            _chainN.Clear();
            for (int k = 0; k < _chain.Count; k++) _chainN.Add(_chain[k].ToNumerics());
            return HandMath.CurlFromJoints(_chainN, HandMath.FullCurlRadians(finger));
        }

        // ---------- Internals ----------

        private OVRHand Hand(Handedness h) { return h == Handedness.Left ? leftHand : rightHand; }
        private OVRSkeleton Skeleton(Handedness h) { return h == Handedness.Left ? leftSkeleton : rightSkeleton; }

        /// <summary>Slot for finger (0..4) and joint k (0 metacarpal, 1 proximal, 2 intermediate, 3 distal, 4 tip).</summary>
        private static int Slot(int finger, int k) { return 1 + Mathf.Clamp(finger, 0, 4) * 5 + k; }

        private bool PalmFacesHead(Handedness h)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 toHead = cam.transform.position - Wrist(h);
            if (toHead.sqrMagnitude < 1e-6f) return false;
            return Vector3.Dot(PalmNormal(h), toHead.normalized) > palmTowardHeadDot;
        }

        private Transform Joint(Handedness h, int slot)
        {
            int side = (int)h;
            OVRSkeleton s = Skeleton(h);
            if (s == null || !s.IsInitialized || s.Bones == null) return null;
            if (_boneCount[side] != s.Bones.Count) BuildJointMap(side, s);
            return _joints[side][slot];
        }

        private void BuildJointMap(int side, OVRSkeleton s)
        {
            Transform[] map = _joints[side];
            for (int i = 0; i < map.Length; i++) map[i] = null;
            _boneCount[side] = s.Bones.Count;

            string type = s.GetSkeletonType().ToString();
            if (!type.StartsWith("XRHand") && !_warnedLegacy)
            {
                _warnedLegacy = true;
                Debug.LogWarning("GhostFret: OVRSkeleton uses the legacy hand skeleton. Set Skeleton Type to XR Hand (OpenXR) for correct joints.");
            }

            for (int i = 0; i < s.Bones.Count; i++)
            {
                OVRBone bone = s.Bones[i];
                if (bone == null || bone.Transform == null) continue;
                int slot = SlotFor(bone.Id);
                if (slot >= 0) map[slot] = bone.Transform;
            }
        }

        /// <summary>OpenXR hand skeleton bone id → our slot (−1 = not needed).</summary>
        private static int SlotFor(OVRSkeleton.BoneId id)
        {
            switch (id)
            {
                case OVRSkeleton.BoneId.XRHand_Wrist: return Wrist_;

                case OVRSkeleton.BoneId.XRHand_ThumbMetacarpal: return Slot(0, 0);
                case OVRSkeleton.BoneId.XRHand_ThumbProximal: return Slot(0, 1);
                case OVRSkeleton.BoneId.XRHand_ThumbDistal: return Slot(0, 3);
                case OVRSkeleton.BoneId.XRHand_ThumbTip: return Slot(0, 4);

                case OVRSkeleton.BoneId.XRHand_IndexMetacarpal: return Slot(1, 0);
                case OVRSkeleton.BoneId.XRHand_IndexProximal: return Slot(1, 1);
                case OVRSkeleton.BoneId.XRHand_IndexIntermediate: return Slot(1, 2);
                case OVRSkeleton.BoneId.XRHand_IndexDistal: return Slot(1, 3);
                case OVRSkeleton.BoneId.XRHand_IndexTip: return Slot(1, 4);

                case OVRSkeleton.BoneId.XRHand_MiddleMetacarpal: return Slot(2, 0);
                case OVRSkeleton.BoneId.XRHand_MiddleProximal: return Slot(2, 1);
                case OVRSkeleton.BoneId.XRHand_MiddleIntermediate: return Slot(2, 2);
                case OVRSkeleton.BoneId.XRHand_MiddleDistal: return Slot(2, 3);
                case OVRSkeleton.BoneId.XRHand_MiddleTip: return Slot(2, 4);

                case OVRSkeleton.BoneId.XRHand_RingMetacarpal: return Slot(3, 0);
                case OVRSkeleton.BoneId.XRHand_RingProximal: return Slot(3, 1);
                case OVRSkeleton.BoneId.XRHand_RingIntermediate: return Slot(3, 2);
                case OVRSkeleton.BoneId.XRHand_RingDistal: return Slot(3, 3);
                case OVRSkeleton.BoneId.XRHand_RingTip: return Slot(3, 4);

                case OVRSkeleton.BoneId.XRHand_LittleMetacarpal: return Slot(4, 0);
                case OVRSkeleton.BoneId.XRHand_LittleProximal: return Slot(4, 1);
                case OVRSkeleton.BoneId.XRHand_LittleIntermediate: return Slot(4, 2);
                case OVRSkeleton.BoneId.XRHand_LittleDistal: return Slot(4, 3);
                case OVRSkeleton.BoneId.XRHand_LittleTip: return Slot(4, 4);
            }
            return -1;
        }

        /// <summary>Finds OVRHand / OVRSkeleton components in the scene for any unassigned side.</summary>
        private void FindMissing()
        {
            if (leftHand == null || rightHand == null)
            {
                OVRHand[] hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
                for (int i = 0; i < hands.Length; i++)
                {
                    string which = SideOf(hands[i]);
                    if (leftHand == null && which == "Left") leftHand = hands[i];
                    else if (rightHand == null && which == "Right") rightHand = hands[i];
                }
            }

            if (leftSkeleton == null || rightSkeleton == null)
            {
                OVRSkeleton[] skeletons = FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None);
                for (int i = 0; i < skeletons.Length; i++)
                {
                    string type = skeletons[i].GetSkeletonType().ToString();
                    if (leftSkeleton == null && type.EndsWith("Left")) leftSkeleton = skeletons[i];
                    else if (rightSkeleton == null && type.EndsWith("Right")) rightSkeleton = skeletons[i];
                }
            }

            // Common rig layout: the skeleton sits on the same object as the hand.
            if (leftSkeleton == null && leftHand != null) leftSkeleton = leftHand.GetComponent<OVRSkeleton>();
            if (rightSkeleton == null && rightHand != null) rightSkeleton = rightHand.GetComponent<OVRSkeleton>();
        }

        /// <summary>
        /// "Left" / "Right" / "" for an OVRHand: from OVRHand.GetHand(), else the skeleton on the same object, else the
        /// object path (rigs name them e.g. LeftHandAnchor / RightHandAnchor).
        /// </summary>
        private static string SideOf(OVRHand hand)
        {
            string side = Side(hand.GetHand().ToString());
            if (side.Length > 0) return side;
            OVRSkeleton s = hand.GetComponent<OVRSkeleton>();
            if (s != null)
            {
                side = Side(s.GetSkeletonType().ToString());
                if (side.Length > 0) return side;
            }
            for (Transform t = hand.transform; t != null; t = t.parent)
            {
                side = Side(t.name);
                if (side.Length > 0) return side;
            }
            return "";
        }

        private static string Side(string label)
        {
            if (label.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Left";
            if (label.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Right";
            return "";
        }
    }
}
#endif

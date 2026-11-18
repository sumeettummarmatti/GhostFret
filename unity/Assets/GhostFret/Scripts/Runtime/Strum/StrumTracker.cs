using GhostFret.Core.Strum;
using GhostFret.Runtime.Audio;
using GhostFret.Runtime.Hands;
using UnityEngine;

namespace GhostFret.Runtime.Strum
{
    /// <summary>
    /// Watches the strumming hand's wrist every frame and answers "was that a down or up strum?"
    /// for a strum time from the audio onset (DESIGN.md §7.3). The wrist is projected onto a strum
    /// axis pointing from the low E string toward the high E string (roughly down for a held guitar):
    /// moving along the axis = down-strum. Hand data is used live only and never stored (rule 6).
    /// Real guitar: Init(geometry.AcrossAxis). Air mode: Init(air guitar's axis). Default: world down.
    /// </summary>
    public sealed class StrumTracker : MonoBehaviour
    {
        /// <summary>Left-handed players strum with the left hand.</summary>
        public bool LeftHanded;

        public float minConfidence = 0.2f;   // ignore samples the tracker itself doubts

        public Vector3 Axis { get; private set; }
        public Handedness StrummingHand { get { return LeftHanded ? Handedness.Left : Handedness.Right; } }

        /// <summary>The live classifier (tunable: MinSpeed, LookbackSeconds).</summary>
        public StrumDirectionClassifier Classifier { get { return _classifier; } }

#if UNITY_EDITOR
        /// <summary>Editor preview only: when not None, DirectionAt answers this instead of reading the wrist.</summary>
        public StrumDirection PreviewDirection { get; set; }
#endif

        private readonly StrumDirectionClassifier _classifier = new StrumDirectionClassifier();
        private bool _initialised;

        public void Init(Vector3 axis)
        {
            Axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.down;
            _classifier.Clear();   // old samples were on a different axis
            _initialised = true;
        }

        private void Awake()
        {
            if (!_initialised) Init(Vector3.down);
        }

        private void Update()
        {
            IHandInput hands = HandsHub.Input;
            if (hands == null) return;
            Handedness hand = StrummingHand;
            if (!hands.IsTracked(hand) || hands.Confidence(hand) < minConfidence) return;

            float along = Vector3.Dot(hands.Wrist(hand), Axis);
            _classifier.AddSample(DspClock.Now, along);
        }

        /// <summary>Direction around a strum time (AudioSettings.dspTime seconds). None when unsure.</summary>
        public StrumDirection DirectionAt(double dspTime)
        {
#if UNITY_EDITOR
            if (PreviewDirection != StrumDirection.None) return PreviewDirection;
#endif
            return _classifier.DirectionAt(dspTime);
        }

        /// <summary>Signed speed along the axis at a time (m/s, + = down), for Air mode or debugging.</summary>
        public bool TryVelocity(double dspTime, out double metresPerSecond)
        {
            return _classifier.TryVelocity(dspTime, out metresPerSecond);
        }
    }
}

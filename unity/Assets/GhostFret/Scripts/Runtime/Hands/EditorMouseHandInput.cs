using UnityEngine;

namespace GhostFret.Runtime.Hands
{
    /// <summary>
    /// Fake hands for testing in the Editor: the mouse ray hit point is the right index tip,
    /// left mouse button = pinch. Lets flows be tested without a headset.
    /// </summary>
    public sealed class EditorMouseHandInput : MonoBehaviour, IHandInput
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private float depth = 0.5f;

        private Vector3 MousePoint()
        {
            if (viewCamera == null) return Vector3.zero;
            return viewCamera.ScreenPointToRay(Input.mousePosition).GetPoint(depth);
        }

        public bool IsTracked(Handedness h) { return h == Handedness.Right; }
        public Vector3 IndexTip(Handedness h) { return MousePoint(); }
        public Vector3 Wrist(Handedness h) { return MousePoint() + Vector3.back * 0.1f; }
        public Vector3 PalmNormal(Handedness h) { return Input.GetKey(KeyCode.P) ? Vector3.up : Vector3.down; }
        public bool IsPinching(Handedness h) { return Input.GetMouseButton(0); }
        public float Confidence(Handedness h) { return 1f; }
    }
}

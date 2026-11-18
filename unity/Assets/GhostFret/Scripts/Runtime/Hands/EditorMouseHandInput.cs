using GhostFret.Core.Hands;
using GhostFret.Runtime.Util;
using UnityEngine;

namespace GhostFret.Runtime.Hands
{
    /// <summary>
    /// Fake hands for testing in the Editor without a headset.
    ///  - Mouse = right index tip (on a sphere `depth` metres from the camera; scroll wheel changes depth).
    ///  - Left mouse button = pinch (on the mouse hand).
    ///  - Hold Left Shift = the mouse drives the LEFT hand instead (e.g. for the calibration touches,
    ///    which use the fretting hand).
    ///  - Keys 1–4 toggle curl of the left index, middle, ring, pinky (Air mode chord shapes).
    ///  - Hold P = palm up (pause gesture).
    /// The hand the mouse isn't driving sits at a fixed spot in front of the camera.
    /// Uses the legacy Input Manager (Player Settings → Active Input Handling = Both).
    /// </summary>
    public sealed class EditorMouseHandInput : MonoBehaviour, IHandInput
    {
        public Camera viewCamera;
        public float depth = 0.5f;
        public float scrollMetersPerNotch = 0.02f;
        /// <summary>Camera-space spots for the hand the mouse isn't driving (low and wide, clear of panels so it never pokes a button).</summary>
        public Vector3 restingLeftOffset = new Vector3(-0.32f, -0.38f, 0.30f);
        public Vector3 restingRightOffset = new Vector3(0.32f, -0.40f, 0.30f);
        /// <summary>How fast toggled fingers bend, so curls ease instead of snapping.</summary>
        public float curlSeconds = 0.2f;

        private readonly bool[] _leftCurled = new bool[5];
        private readonly float[][] _curl = { new float[5], new float[5] };

        // Index, middle, ring, pinky knuckle offsets across the hand (metres, toward the little finger) and finger lengths.
        private static readonly float[] KnuckleSide = { -0.035f, -0.02f, 0f, 0.018f, 0.034f };
        private static readonly float[] FingerLength = { 0.06f, 0.075f, 0.082f, 0.077f, 0.062f };
        private const float PalmLength = 0.085f;

        private Camera Cam { get { return viewCamera != null ? viewCamera : Camera.main; } }
        private bool MouseDrivesLeft { get { return Input.GetKey(KeyCode.LeftShift); } }

        private void Update()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f) depth = Mathf.Clamp(depth + scroll * scrollMetersPerNotch, 0.15f, 1.5f);

            if (Input.GetKeyDown(KeyCode.Alpha1)) _leftCurled[1] = !_leftCurled[1];
            if (Input.GetKeyDown(KeyCode.Alpha2)) _leftCurled[2] = !_leftCurled[2];
            if (Input.GetKeyDown(KeyCode.Alpha3)) _leftCurled[3] = !_leftCurled[3];
            if (Input.GetKeyDown(KeyCode.Alpha4)) _leftCurled[4] = !_leftCurled[4];

            float step = curlSeconds > 0f ? Time.unscaledDeltaTime / curlSeconds : 1f;
            for (int f = 0; f < 5; f++)
            {
                _curl[0][f] = Mathf.MoveTowards(_curl[0][f], _leftCurled[f] ? 1f : 0f, step);
                _curl[1][f] = Mathf.MoveTowards(_curl[1][f], 0f, step);
            }
        }

        // ---------- IHandInput ----------

        public bool IsTracked(Handedness h) { return Cam != null; }
        public float Confidence(Handedness h) { return Cam != null ? 1f : 0f; }

        public bool IsPinching(Handedness h)
        {
            bool mouseHand = (h == Handedness.Left) == MouseDrivesLeft;
            return mouseHand && Input.GetMouseButton(0);
        }

        public Vector3 IndexTip(Handedness h) { return FingerTip(h, Finger.Index); }

        public Vector3 PalmNormal(Handedness h)
        {
            return Input.GetKey(KeyCode.P) ? Vector3.up : Vector3.down;
        }

        public Vector3 Wrist(Handedness h)
        {
            Vector3 fwd, side;
            Frame(h, out fwd, out side);
            return Knuckle(h, Finger.Index) - fwd * PalmLength - side * KnuckleSide[Finger.Index];
        }

        public Vector3 FingerTip(Handedness h, int finger)
        {
            finger = Mathf.Clamp(finger, 0, 4);
            Vector3 fwd, side;
            Frame(h, out fwd, out side);
            float curl = FingerCurl(h, finger);
            return HandMath.ProceduralTip(
                Knuckle(h, finger).ToNumerics(), fwd.ToNumerics(), PalmNormal(h).ToNumerics(),
                FingerLength[finger], curl).ToUnity();
        }

        public float FingerCurl(Handedness h, int finger)
        {
            if (finger < 0 || finger > 4) return 0f;
            return _curl[h == Handedness.Left ? 0 : 1][finger];
        }

        // ---------- Fake hand layout ----------

        /// <summary>Where the index tip is (when straight): the mouse point, or a fixed resting spot.</summary>
        private Vector3 Anchor(Handedness h)
        {
            Camera cam = Cam;
            if (cam == null) return Vector3.zero;
            bool mouseHand = (h == Handedness.Left) == MouseDrivesLeft;
            if (mouseHand) return cam.ScreenPointToRay(Input.mousePosition).GetPoint(depth);
            return cam.transform.TransformPoint(h == Handedness.Left ? restingLeftOffset : restingRightOffset);
        }

        /// <summary>Fingers point away from the camera; "side" points toward the little finger.</summary>
        private void Frame(Handedness h, out Vector3 fwd, out Vector3 side)
        {
            Camera cam = Cam;
            fwd = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            // Palm down: the little finger is on the outside (right for the right hand). Palm up flips it.
            bool palmUp = Input.GetKey(KeyCode.P);
            float outward = h == Handedness.Right ? 1f : -1f;
            side = right * (palmUp ? -outward : outward);
        }

        private Vector3 Knuckle(Handedness h, int finger)
        {
            Vector3 fwd, side;
            Frame(h, out fwd, out side);
            // Place the hand so the straight index tip lands exactly on the anchor.
            Vector3 indexKnuckle = Anchor(h) - fwd * FingerLength[Finger.Index];
            Vector3 k = indexKnuckle + side * (KnuckleSide[finger] - KnuckleSide[Finger.Index]);
            if (finger == Finger.Thumb) k -= fwd * 0.045f;
            return k;
        }
    }
}

using System;
using GhostFret.Runtime.Hands;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.UI
{
    /// <summary>
    /// Hands-only button. Press by poking it with an index fingertip (push through the face),
    /// or by pinching while the fingertip is touching it. Works with any IHandInput
    /// (MetaHandInput on device, EditorMouseHandInput in the Editor). Never needs a controller.
    /// Local space: the button face is the XY plane, the player is on the −Z side.
    /// </summary>
    public sealed class PokeButton : MonoBehaviour
    {
        public Vector2 size = new Vector2(0.12f, 0.045f);
        public float hoverDepth = 0.04f;    // fingertip this close in front = hover
        public float pressDepth = 0.004f;   // fingertip pushed this far = press
        public float margin = 0.008f;       // forgiving edges
        public float cooldownSeconds = 0.6f;

        public event Action Pressed;
        public bool Interactable = true;

        public TextMeshPro Label { get; set; }
        public Renderer Background { get; set; }

        private bool _armed = true;
        private float _lastPress = -10f;
        private bool _wasPinching;

        private void Update()
        {
            IHandInput hands = HandsHub.Input;
            if (hands == null || !Interactable) { SetHover(false); return; }

            bool hover = false;
            bool press = false;
            for (int h = 0; h < 2; h++)
            {
                var hand = (Handedness)h;
                if (!hands.IsTracked(hand)) continue;

                Vector3 local = transform.InverseTransformPoint(hands.IndexTip(hand));
                // Scale-aware: transform.lossyScale is assumed ~1 (UIKit builds buttons unscaled).
                bool inside = Mathf.Abs(local.x) < size.x * 0.5f + margin && Mathf.Abs(local.y) < size.y * 0.5f + margin;
                if (!inside) continue;

                if (local.z > -hoverDepth && local.z < 0.03f) hover = true;
                if (local.z > -pressDepth) press = true;

                bool pinching = hands.IsPinching(hand);
                if (pinching && !_wasPinching && local.z > -hoverDepth) press = true;
                _wasPinching = pinching;
            }

            SetHover(hover);

            // Re-arm only after the finger has backed off, so one poke = one press.
            if (!press) _armed = true;
            if (press && _armed && Time.unscaledTime - _lastPress > cooldownSeconds)
            {
                _armed = false;
                _lastPress = Time.unscaledTime;
                Press();
            }
        }

        /// <summary>Also callable from code (e.g. "strum to start").</summary>
        public void Press()
        {
            UiSounds.PlayAt(UiSounds.Click, transform.position);
            if (Pressed != null) Pressed();
        }

        private void SetHover(bool hover)
        {
            if (Background == null) return;
            Color target = !Interactable ? Theme.WithAlpha(Theme.ButtonBg, 0.4f) : hover ? Theme.ButtonHover : Theme.ButtonBg;
            Background.material.color = Color.Lerp(Background.material.color, target, Time.unscaledDeltaTime / Theme.EaseSeconds * 3f);
        }
    }
}

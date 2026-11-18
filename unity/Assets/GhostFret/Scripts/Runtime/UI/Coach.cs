using GhostFret.Runtime.Hands;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.UI
{
    public enum CoachTone { Neutral, Good, Hint }

    /// <summary>
    /// The coach's voice: one short caption at a time, with a matching soft sound.
    /// Captions are always on, so every sound has text (accessibility). Tone adds a shape:
    /// Good → "✓", Hint → "!". Text follows UX_GUIDE.md: short, specific, kind.
    /// Anyone can call Coach.Say(...). Set Coach.Anchor to keep captions beside the guitar.
    /// </summary>
    public sealed class Coach : MonoBehaviour
    {
        public float holdSeconds = 3.5f;

        /// <summary>Where captions sit (e.g. a transform beside the guitar). Null = in front of the player.</summary>
        public static Transform Anchor;

        private static Coach _instance;
        private TextMeshPro _text;
        private Transform _panel;
        private float _shownAt = -100f;
        private float _alpha;

        public static void Say(string message, CoachTone tone = CoachTone.Neutral, bool withSound = true)
        {
            Ensure();
            _instance.Show(message, tone, withSound);
        }

        public static void Clear()
        {
            if (_instance != null) _instance._shownAt = -100f;
        }

        private static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("GhostFret Coach");
            _instance = go.AddComponent<Coach>();
            _instance._panel = UIKit.CreatePanel("Caption", new Vector2(0.34f, 0.07f), go.transform);
            _instance._text = UIKit.CreateText(_instance._panel, "", Theme.BodyHeight, new Vector3(0, 0, -0.001f), 0.32f);
            _instance.SetAlpha(0f);
        }

        private void Show(string message, CoachTone tone, bool withSound)
        {
            string m = tone == CoachTone.Good ? Theme.GoodMark : tone == CoachTone.Hint ? Theme.HintMark : "";
            string mark = m.Length > 0 ? m + " " : "";
            _text.text = mark + message;
            _text.color = tone == CoachTone.Good ? Theme.Good : tone == CoachTone.Hint ? Theme.Hint : Theme.Text;
            _shownAt = Time.unscaledTime;
            Place();
            if (withSound)
            {
                if (tone == CoachTone.Good) UiSounds.Play(UiSounds.Chime);
                else if (tone == CoachTone.Hint) UiSounds.Play(UiSounds.Hmm, 0.7f);
            }
        }

        private void Place()
        {
            if (Anchor != null)
            {
                _panel.position = Anchor.position;
                UIKit.FacePlayer(_panel);
            }
            else UIKit.PlaceInFront(_panel, 0.5f, 0.05f);
        }

        private void Update()
        {
            bool visible = Time.unscaledTime - _shownAt < holdSeconds;
            float target = visible ? 1f : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime / Theme.EaseSeconds);
            SetAlpha(Theme.Ease(_alpha));
            if (visible && Anchor != null) Place();
        }

        private void SetAlpha(float a)
        {
            if (_text != null) _text.alpha = a;
            var bg = _panel != null ? _panel.GetComponentInChildren<Renderer>() : null;
            if (bg != null) bg.material.color = Theme.WithAlpha(Theme.PanelBg, Theme.PanelBg.a * a);
        }
    }
}

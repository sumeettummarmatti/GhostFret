using System;
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// A small one-message card with up to two buttons: "No guitar? Try Air mode" during fitting,
    /// "Skip tuning" after a while, the friendly mic-permission message. Not a menu: one choice, one line.
    /// </summary>
    public sealed class HelperPanel : MonoBehaviour
    {
        private AppPanel _panel;
        private TextMeshPro _text;
        private PokeButton _a, _b;
        private Action _onA, _onB;

        public bool Visible { get { return _panel != null && _panel.Visible; } }

        public void Init(string name)
        {
            _panel = AppPanel.Create(name, new Vector2(0.28f, 0.10f));
            _text = _panel.AddText("", Theme.SmallHeight, 0.025f, Theme.TextDim);
            _a = _panel.AddButton("", -0.065f, -0.02f, AppPanel.SmallButton * 1.1f, () => { if (_onA != null) _onA(); });
            _b = _panel.AddButton("", 0.065f, -0.02f, AppPanel.SmallButton * 1.1f, () => { if (_onB != null) _onB(); });
        }

        /// <param name="belowOffset">Metres to drop below the normal spot, so it doesn't cover the main panel.</param>
        public void Show(string message, string a, Action onA, string b, Action onB, FretboardGeometry g, float belowOffset = 0.17f)
        {
            _text.text = message;
            _onA = onA;
            _onB = onB;
            _a.Label.text = a ?? "";
            _a.gameObject.SetActive(a != null);
            _b.Label.text = b ?? "";
            _b.gameObject.SetActive(b != null);
            // One button → centre it.
            _a.transform.localPosition = new Vector3(b == null ? 0f : -0.065f, -0.02f, -0.002f);
            _panel.Show(g);
            _panel.transform.position += Vector3.down * belowOffset;
        }

        public void Hide() { if (_panel != null) _panel.Hide(); }
    }
}

using System;
using System.Collections.Generic;
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// A small world-space card built with UIKit: dark glass, big poke buttons, eased in and out.
    /// Placed beside the guitar once fitted, otherwise in front of the player within reach.
    /// </summary>
    public sealed class AppPanel : MonoBehaviour
    {
        public static readonly Vector2 BigButton = new Vector2(0.16f, 0.055f);
        public static readonly Vector2 SmallButton = new Vector2(0.11f, 0.042f);

        public Vector2 Size { get; private set; }
        public bool Visible { get; private set; }

        private float _t;   // 0 hidden .. 1 shown
        private static readonly List<AppPanel> All = new List<AppPanel>();

        /// <summary>After a re-fit, move every open card to the guitar's new spot.</summary>
        public static void PlaceAllVisible(FretboardGeometry g)
        {
            foreach (AppPanel p in All) if (p != null && p.Visible) p.Place(g);
        }

        private void OnDestroy() { All.Remove(this); }

        public static AppPanel Create(string name, Vector2 size)
        {
            Transform root = UIKit.CreatePanel(name, size);
            var p = root.gameObject.AddComponent<AppPanel>();
            p.Size = size;
            All.Add(p);
            root.localScale = Vector3.one * 0.9f;
            root.gameObject.SetActive(false);
            return p;
        }

        /// <summary>Text line; y is measured from the panel centre (metres).</summary>
        public TextMeshPro AddText(string text, float height, float y, Color? colour = null)
        {
            TextMeshPro t = UIKit.CreateText(transform, text, height, new Vector3(0, y, -0.001f), Size.x * 0.9f);
            if (colour.HasValue) t.color = colour.Value;
            return t;
        }

        public PokeButton AddButton(string label, float x, float y, Vector2 size, Action onPress)
        {
            PokeButton b = UIKit.CreateButton(transform, label, new Vector3(x, y, -0.002f), size, onPress);
            // A finger that just pressed the previous card mustn't also press a new button in the same spot.
            b.Interactable = false;
            _pending.Add(b);
            _armAt = Time.unscaledTime + ArmDelaySeconds;
            return b;
        }

        public static float ArmDelaySeconds = 0.5f;
        private readonly List<PokeButton> _pending = new List<PokeButton>();
        private float _armAt;

        /// <summary>Removes all children except the background (for panels that rebuild their content).</summary>
        public void ClearContent()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform c = transform.GetChild(i);
                if (c.name != "Background") Destroy(c.gameObject);
            }
        }

        public void Show(FretboardGeometry g)
        {
            Place(g);
            if (!Visible)
            {
                foreach (PokeButton b in GetComponentsInChildren<PokeButton>(true)) { b.Interactable = false; _pending.Add(b); }
                _armAt = Time.unscaledTime + ArmDelaySeconds;
            }
            Visible = true;
            gameObject.SetActive(true);
        }

        public void Place(FretboardGeometry g)
        {
            if (g != null) UIKit.PlaceBesideGuitar(transform, g);
            else UIKit.PlaceInFront(transform);
        }

        public void Hide()
        {
            Visible = false;
            if (_t <= 0f) gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_pending.Count > 0 && Time.unscaledTime >= _armAt)
            {
                foreach (PokeButton b in _pending) if (b != null) b.Interactable = true;
                _pending.Clear();
            }

            // Gentle scale ease (no flashing): 0.9 → 1 on show, back on hide, then switch off.
            _t = Mathf.MoveTowards(_t, Visible ? 1f : 0f, Time.unscaledDeltaTime / Theme.EaseSeconds);
            transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, Theme.Ease(_t));
            if (!Visible && _t <= 0f) gameObject.SetActive(false);
        }
    }
}

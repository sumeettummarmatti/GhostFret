using System;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Progress;
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// Accessibility settings (DESIGN.md §14): left-handed mode, overlay brightness, metronome volume,
    /// and "Delete my progress" with an in-app confirm step (a second button, never a system dialog).
    /// Captions are always on. Changes apply at once and are saved with progress (on device only).
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        public float brightnessStep = 0.15f;
        public float volumeStep = 0.2f;

        /// <summary>Raised after any change, so the app can apply and save.</summary>
        public event Action<PracticeSettings> Changed;
        public event Action DeleteConfirmed;
        public event Action Closed;

        private PracticeSettings _settings;
        private AppPanel _panel;
        private TextMeshPro _handText, _brightText, _volText, _deleteText;
        private PokeButton _hand, _delete, _confirm, _keep;

        public bool Visible { get { return _panel != null && _panel.Visible; } }

        public void Init()
        {
            _panel = AppPanel.Create("Settings", new Vector2(0.36f, 0.30f));
            _panel.AddText("Settings", Theme.TitleHeight, 0.12f);

            _hand = _panel.AddButton("Left-handed: Off", 0f, 0.075f, new Vector2(0.22f, 0.045f), ToggleHand);
            _handText = _hand.Label;

            _brightText = _panel.AddText("", Theme.SmallHeight, 0.025f);
            _panel.AddButton("-", -0.13f, 0.025f, new Vector2(0.05f, 0.04f), () => Brightness(-brightnessStep));
            _panel.AddButton("+", 0.13f, 0.025f, new Vector2(0.05f, 0.04f), () => Brightness(brightnessStep));

            _volText = _panel.AddText("", Theme.SmallHeight, -0.025f);
            _panel.AddButton("-", -0.13f, -0.025f, new Vector2(0.05f, 0.04f), () => Volume(-volumeStep));
            _panel.AddButton("+", 0.13f, -0.025f, new Vector2(0.05f, 0.04f), () => Volume(volumeStep));

            _delete = _panel.AddButton("Delete my progress", -0.07f, -0.08f, new Vector2(0.15f, 0.04f), AskDelete);
            _deleteText = _panel.AddText("", Theme.SmallHeight, -0.08f, Theme.Hint);
            _confirm = _panel.AddButton("Yes, delete", -0.07f, -0.125f, AppPanel.SmallButton, ConfirmDelete);
            _keep = _panel.AddButton("Keep it", 0.07f, -0.125f, AppPanel.SmallButton, CancelDelete);
            _panel.AddButton("Back", 0.09f, -0.08f, AppPanel.SmallButton, () => { Hide(); if (Closed != null) Closed(); });
        }

        public void Show(PracticeSettings settings, FretboardGeometry g)
        {
            _settings = settings;
            CancelDelete();
            Refresh();
            _panel.Show(g);
            // Sit slightly in front of whatever panel opened us.
            _panel.transform.position -= _panel.transform.forward * 0.02f;
        }

        public void Hide() { _panel.Hide(); }

        private void Refresh()
        {
            _handText.text = "Left-handed: " + (_settings.leftHanded ? "On" : "Off");
            _brightText.text = "Overlay brightness " + Mathf.RoundToInt(_settings.overlayBrightness * 100f) + "%";
            _volText.text = "Metronome volume " + Mathf.RoundToInt(_settings.metronomeVolume * 100f) + "%";
        }

        private void ToggleHand() { _settings.leftHanded = !_settings.leftHanded; Apply(); }
        private void Brightness(float d) { _settings.overlayBrightness += d; Apply(); }
        private void Volume(float d) { _settings.metronomeVolume += d; Apply(); }

        private void Apply()
        {
            _settings.Sanitize();
            Refresh();
            if (Changed != null) Changed(_settings);
        }

        private void AskDelete()
        {
            _delete.gameObject.SetActive(false);
            _deleteText.text = "Delete streak and scores?";
            _deleteText.transform.localPosition = new Vector3(-0.07f, -0.08f, -0.001f);
            _confirm.gameObject.SetActive(true);
            _keep.gameObject.SetActive(true);
        }

        private void CancelDelete()
        {
            _delete.gameObject.SetActive(true);
            _deleteText.text = "";
            _confirm.gameObject.SetActive(false);
            _keep.gameObject.SetActive(false);
        }

        private void ConfirmDelete()
        {
            CancelDelete();
            if (DeleteConfirmed != null) DeleteConfirmed();
            Coach.Say("Your progress was deleted.", CoachTone.Neutral, false);
        }
    }
}

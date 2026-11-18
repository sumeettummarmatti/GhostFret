using System;
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.UI;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// "Paused. Take your time." Big Resume button; palm up again also resumes.
    /// Small Settings and "Finish for today" for anyone who wants to stop here.
    /// </summary>
    public sealed class PausePanel : MonoBehaviour
    {
        public event Action ResumePressed;
        public event Action SettingsPressed;
        public event Action FinishPressed;

        private AppPanel _panel;

        public void Init()
        {
            _panel = AppPanel.Create("Paused", new Vector2(0.30f, 0.20f));
            _panel.AddText("Paused. Take your time.", Theme.TitleHeight, 0.065f);
            _panel.AddText("Turn your palm up again, or tap Resume.", Theme.SmallHeight, 0.03f, Theme.TextDim);
            _panel.AddButton("Resume", 0f, -0.015f, AppPanel.BigButton, () => Raise(ResumePressed));
            _panel.AddButton("Settings", -0.065f, -0.07f, AppPanel.SmallButton, () => Raise(SettingsPressed));
            _panel.AddButton("Finish for today", 0.065f, -0.07f, AppPanel.SmallButton, () => Raise(FinishPressed));
        }

        public void Show(FretboardGeometry g) { _panel.Show(g); }
        public void Hide() { _panel.Hide(); }
        public bool Visible { get { return _panel != null && _panel.Visible; } }

        private static void Raise(Action a) { if (a != null) a(); }
    }
}

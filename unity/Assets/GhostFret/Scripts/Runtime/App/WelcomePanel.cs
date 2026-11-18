using System;
using System.Collections.Generic;
using GhostFret.Core.Content;
using GhostFret.Core.Fretboard;
using GhostFret.Core.Lesson;
using GhostFret.Runtime.UI;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// The first card. First-time players: one big Start (plus Air mode) and nothing else, so they go
    /// straight Fit → Tune → first chord. Returning players: streak, "Day N: skill", Continue,
    /// and a small menu of modes. Songs shows only unlocked songs.
    /// </summary>
    public sealed class WelcomePanel : MonoBehaviour
    {
        public event Action<SessionMode> ModeChosen;
        public event Action<SongChart> SongChosen;
        public event Action SettingsChosen;

        private AppPanel _panel;

        public void Init()
        {
            _panel = AppPanel.Create("Welcome", new Vector2(0.38f, 0.30f));
        }

        public void Hide() { if (_panel != null) _panel.Hide(); }

        public void ShowFirstTime(FretboardGeometry g)
        {
            _panel.ClearContent();
            _panel.AddText("GhostFret", Theme.TitleHeight, 0.10f);
            _panel.AddText("Grab your guitar and sit comfortably.", Theme.BodyHeight, 0.05f);
            _panel.AddButton("Start", 0f, -0.01f, AppPanel.BigButton, () => Choose(SessionMode.Guided));
            _panel.AddButton("No guitar? Try Air mode", 0f, -0.075f, new Vector2(0.22f, 0.042f), () => Choose(SessionMode.Air));
            _panel.AddText("Sound and hands are used live and never saved.", Theme.SmallHeight, -0.125f, Theme.TextDim);
            _panel.Show(g);
        }

        /// <param name="drillPair">null hides Change Drill (nothing to drill yet).</param>
        public void ShowReturning(int streak, string dayLine, string drillPair, FretboardGeometry g)
        {
            _panel.ClearContent();
            _panel.AddText(streak > 1 ? streak + "-day streak!" : "Welcome back!", Theme.TitleHeight, 0.115f, Theme.Good);
            _panel.AddText(dayLine, Theme.BodyHeight, 0.075f);
            _panel.AddButton("Continue", 0f, 0.025f, AppPanel.BigButton, () => Choose(SessionMode.Guided));

            // Small menu: two rows of three.
            Vector2 small = new Vector2(0.11f, 0.04f);
            float r1 = -0.035f, r2 = -0.083f, dx = 0.118f;
            _panel.AddButton("Chord Coach", -dx, r1, small, () => Choose(SessionMode.ChordCoach));
            _panel.AddButton("Strum Coach", 0f, r1, small, () => Choose(SessionMode.StrumCoach));
            if (drillPair != null) _panel.AddButton("Change Drill", dx, r1, small, () => Choose(SessionMode.ChangeDrill));
            _panel.AddButton("Songs", -dx, r2, small, () => Choose(SessionMode.Song));
            _panel.AddButton("Tuner", 0f, r2, small, () => Choose(SessionMode.Tuner));
            _panel.AddButton("Settings", dx, r2, small, () => { if (SettingsChosen != null) SettingsChosen(); });
            _panel.AddButton("Air mode", 0f, -0.128f, small, () => Choose(SessionMode.Air));
            _panel.Show(g);
        }

        /// <summary>Unlocked songs as big cards (up to 6), plus Back.</summary>
        public void ShowSongs(IList<SongChart> unlocked, Action back, FretboardGeometry g)
        {
            _panel.ClearContent();
            _panel.AddText("Pick a song", Theme.TitleHeight, 0.115f);
            int n = Mathf.Min(6, unlocked.Count);
            for (int i = 0; i < n; i++)
            {
                SongChart song = unlocked[i];
                float x = i % 2 == 0 ? -0.09f : 0.09f;
                float y = 0.06f - (i / 2) * 0.055f;
                _panel.AddButton(song.title, x, y, new Vector2(0.17f, 0.045f), () => { if (SongChosen != null) SongChosen(song); });
            }
            if (n == 0) _panel.AddText("Learn a chord first and songs will unlock.", Theme.BodyHeight, 0.03f, Theme.TextDim);
            _panel.AddButton("Back", 0f, -0.12f, AppPanel.SmallButton, back);
            _panel.Show(g);
        }

        private void Choose(SessionMode mode)
        {
            if (ModeChosen != null) ModeChosen(mode);
        }
    }
}

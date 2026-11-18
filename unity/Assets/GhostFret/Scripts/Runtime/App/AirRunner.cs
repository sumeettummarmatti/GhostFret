using System;
using GhostFret.Core.Content;
using GhostFret.Runtime.UI;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// Air Guitar path (DESIGN.md §7.6): no guitar, no microphone, hands only.
    /// First time → AirGuitar.RunDay1; later → an unlocked song played on the Air guitar.
    /// Hard rule: always completes. An "I'm done" button is always there as a safety net.
    /// </summary>
    public sealed class AirRunner : MonoBehaviour
    {
        private AppServices _s;
        private SongRunner _song;
        private Action _done;
        private bool _active, _inSong;
        private AppPanel _panel;

        public void Init(AppServices s, SongRunner song)
        {
            _s = s;
            _song = song;
            _panel = AppPanel.Create("Air mode", new Vector2(0.30f, 0.11f));
            _panel.AddText("Air mode practises shapes and rhythm. Real-guitar mode checks your actual sound.",
                Theme.SmallHeight, 0.02f, Theme.TextDim);
            _panel.AddButton("I'm done", 0f, -0.028f, AppPanel.SmallButton, Complete);
        }

        /// <param name="song">Song for a returning player (null → Air Day 1).</param>
        public void Begin(SongChart song, Action done)
        {
            _done = done;
            _active = true;
            _s.Summary.AirMode = true;
            _s.Air.Begin(_s.Hands, _s.Progress.settings.leftHanded);
            _s.Geometry = _s.Air.Geometry;
            _panel.Show(_s.Geometry);
            if (_s.Geometry != null) _panel.transform.position += Vector3.up * 0.12f;   // above the song panel

            if (song == null || !_s.Progress.airDay1Done)
            {
                _inSong = false;
                _s.Air.RunDay1(() =>
                {
                    if (!_active) return;
                    _s.Progress.airDay1Done = true;
                    Complete();
                });
            }
            else
            {
                _inSong = true;
                _song.Begin(song, true, Complete);
            }
        }

        public void SetPaused(bool paused)
        {
            if (_active && _inSong) _song.SetPaused(paused);
        }

        public void End()
        {
            if (_inSong) _song.End();
            _inSong = false;
            _active = false;
            _s.Air.End();
            _s.Geometry = _s.Fitted;
            _panel.Hide();
        }

        private void Complete()
        {
            if (!_active) return;
            Action d = _done;
            _done = null;
            if (d != null) d();
        }
    }
}

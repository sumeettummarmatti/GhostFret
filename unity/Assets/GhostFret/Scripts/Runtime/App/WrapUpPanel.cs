using System;
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.UI;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// Wrap-up (DESIGN.md §5.2): streak, today's best, tomorrow's goal. Kind, never a harsh grade.
    /// "Practise again" goes home; "Done" shows a short see-you-tomorrow card.
    /// </summary>
    public sealed class WrapUpPanel : MonoBehaviour
    {
        public event Action PractiseAgain;
        public event Action Done;

        private AppPanel _panel;

        public void Init()
        {
            _panel = AppPanel.Create("Wrap-up", new Vector2(0.34f, 0.24f));
        }

        public void Show(string streakLine, string bestLine, string tomorrowLine, string tipLine, FretboardGeometry g)
        {
            _panel.ClearContent();
            _panel.AddText(streakLine, Theme.TitleHeight, 0.085f, Theme.Good);
            _panel.AddText(bestLine, Theme.BodyHeight, 0.045f);
            _panel.AddText(tomorrowLine, Theme.SmallHeight, 0.01f, Theme.TextDim);
            if (!string.IsNullOrEmpty(tipLine)) _panel.AddText(tipLine, Theme.SmallHeight, -0.02f, Theme.TextDim);
            _panel.AddButton("Practise again", -0.075f, -0.075f, AppPanel.BigButton * 0.85f, () => { if (PractiseAgain != null) PractiseAgain(); });
            _panel.AddButton("Done", 0.08f, -0.075f, AppPanel.SmallButton, () => { if (Done != null) Done(); });
            _panel.Show(g);
        }

        public void ShowGoodbye(FretboardGeometry g)
        {
            _panel.ClearContent();
            _panel.AddText("See you tomorrow!", Theme.TitleHeight, 0.05f, Theme.Good);
            _panel.AddText("Your progress is saved on this headset.", Theme.SmallHeight, 0.01f, Theme.TextDim);
            _panel.AddButton("Practise again", 0f, -0.05f, AppPanel.BigButton, () => { if (PractiseAgain != null) PractiseAgain(); });
            _panel.Show(g);
        }

        public void Hide() { _panel.Hide(); }
    }
}

#if UNITY_EDITOR
using GhostFret.Runtime.UI;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.Preview
{
    /// <summary>
    /// Editor preview only: a small dark-glass key map in the top-left corner of the view, built with
    /// UIKit and parented to the camera so it stays put while looking around. H toggles it.
    /// </summary>
    public sealed class PreviewLegend : MonoBehaviour
    {
        public float distance = 0.8f;          // metres in front of the camera
        public float margin = 0.03f;           // gap from the view's edges
        public static readonly Vector2 Size = new Vector2(0.34f, 0.42f);

        private const float TextHeight = 0.0135f;

        private static readonly string KeyColour = "#" + ColorUtility.ToHtmlStringRGB(Theme.Ghost);

        private Camera _cam;
        private TextMeshPro _status;

        public static PreviewLegend Create(Camera cam)
        {
            Transform root = UIKit.CreatePanel("Preview Keys (Editor only)", Size, cam.transform);
            var legend = root.gameObject.AddComponent<PreviewLegend>();
            legend._cam = cam;
            legend.Build();
            return legend;
        }

        public bool Visible
        {
            get { return gameObject.activeSelf; }
            set { gameObject.SetActive(value); }
        }

        public void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }

        private void Build()
        {
            float w = Size.x - 0.03f;
            TextMeshPro title = UIKit.CreateText(transform, "Editor preview", Theme.BodyHeight, new Vector3(0, Size.y * 0.5f - 0.025f, -0.001f), w, TextAlignmentOptions.TopLeft);
            title.rectTransform.sizeDelta = new Vector2(w, 0.03f);

            string keys =
                Key("C") + "clean strum\n" +
                Key("M") + "muted G string\n" +
                Key("B") + "buzz on the A string\n" +
                Key("W") + "wrong chord (heard G)\n" +
                Key("Q") + "too quiet   " + Key("Shift+Q") + "not sure\n" +
                Key("Space") + "strum on the beat\n" +
                Key("T") + "tuning done   " + Key("F") + "fit fretboard\n" +
                Key("A") + "Air mode   " + Key("N") + "next / skip\n" +
                Key("P") + "(hold) palm up = pause\n" +
                Key("L") + "use the real mic on/off\n" +
                Key("H") + "hide these keys\n" +
                "<color=#B3BFCC>Mouse = right hand, click = pinch\n" +
                "Right-drag = look   Shift = left hand\n" +
                "1–4 = bend left fingers (Air mode)</color>";
            TextMeshPro body = UIKit.CreateText(transform, keys, TextHeight, new Vector3(0, 0.015f, -0.001f), w, TextAlignmentOptions.TopLeft);
            body.rectTransform.sizeDelta = new Vector2(w, 0.30f);
            body.richText = true;

            _status = UIKit.CreateText(transform, "", TextHeight * 0.9f, new Vector3(0, -Size.y * 0.5f + 0.04f, -0.001f), w, TextAlignmentOptions.TopLeft);
            _status.rectTransform.sizeDelta = new Vector2(w, 0.06f);
            _status.color = Theme.TextDim;
        }

        private static string Key(string k)
        {
            return "<color=" + KeyColour + "><b>" + k + "</b></color>  ";
        }

        /// <summary>Keeps the card in the top-left corner whatever the Game view's size.</summary>
        private void LateUpdate()
        {
            if (_cam == null) return;
            float halfH = distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * _cam.aspect;
            float scale = Mathf.Min(1f, (halfH * 2f - margin * 2f) / Size.y * 0.6f);   // never more than ~60% of the height
            transform.localScale = Vector3.one * scale;
            transform.localRotation = Quaternion.identity;   // readable face (−Z) toward the camera
            transform.localPosition = new Vector3(
                -halfW + margin + Size.x * 0.5f * scale,
                halfH - margin - Size.y * 0.5f * scale,
                distance);
        }
    }
}
#endif

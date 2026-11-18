using GhostFret.Core.Fretboard;
using GhostFret.Runtime.UI;
using GhostFret.Runtime.Util;
using UnityEngine;

namespace GhostFret.Runtime.App
{
    /// <summary>
    /// A small, soft sparkle near the strings (UX_GUIDE: celebrations are small, never full-screen,
    /// nothing flashes). Dots drift up and fade out smoothly over about a second.
    /// </summary>
    public sealed class Celebration : MonoBehaviour
    {
        public static int Count = 14;
        public static float Seconds = 1.3f;
        public static float Spread = 0.06f;

        private Transform[] _dots;
        private Vector3[] _drift;
        private Material[] _mats;
        private float _age;

        /// <summary>Somewhere friendly on the neck: between the nut and the 12th fret.</summary>
        public static Vector3 NearStrings(FretboardGeometry g)
        {
            if (g == null) return UIKit.HeadPosition + (UIKit.Head != null ? UIKit.Head.forward : Vector3.forward) * 0.45f;
            Vector3 nut = ((g.NutLowE + g.NutHighE) * 0.5f).ToUnity();
            return Vector3.Lerp(nut, g.TwelfthLowE.ToUnity(), 0.5f) + g.SurfaceNormal.ToUnity() * 0.03f;
        }

        public static void Burst(Vector3 at)
        {
            var go = new GameObject("Sparkle");
            go.transform.position = at;
            go.AddComponent<Celebration>().Build();
        }

        private void Build()
        {
            _dots = new Transform[Count];
            _drift = new Vector3[Count];
            _mats = new Material[Count];
            for (int i = 0; i < Count; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                q.transform.localScale = Vector3.one * Random.Range(0.006f, 0.012f);
                q.transform.localPosition = Random.insideUnitSphere * 0.02f;
                Color c = i % 3 == 0 ? Theme.Ghost : Theme.Good;
                _mats[i] = Theme.UnlitMaterial(c);
                q.GetComponent<Renderer>().material = _mats[i];
                _drift[i] = (Random.insideUnitSphere * Spread + Vector3.up * 0.08f);
                _dots[i] = q.transform;
            }
        }

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_age / Seconds);
            for (int i = 0; i < _dots.Length; i++)
            {
                _dots[i].localPosition += _drift[i] * Time.unscaledDeltaTime;
                UIKit.FacePlayer(_dots[i]);
                Color c = _mats[i].color;
                c.a = 1f - Theme.Ease(k);   // smooth fade, no blinking
                _mats[i].color = c;
            }
            if (_age >= Seconds)
            {
                foreach (Material m in _mats) Destroy(m);
                Destroy(gameObject);
            }
        }
    }
}

using System.Collections.Generic;
using GhostFret.Core.AirMode;
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.UI;
using GhostFret.Runtime.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostFret.Runtime.AirMode
{
    /// <summary>
    /// The pretend guitar you see in Air mode: a soft, translucent cyan outline of an acoustic body,
    /// neck and headstock, with strings over the body. Built from LineRenderers and thin meshes in code.
    /// The FretboardOverlay draws frets and strings on the neck; this draws everything else.
    /// Motion is eased: fade in/out, strings glow briefly on a strum, small glints drift up. No flashing.
    /// </summary>
    public sealed class AirGuitarVisual : MonoBehaviour
    {
        // Look (tunable).
        public float outlineWidth = 0.0025f;
        public float stringWidth = 0.0012f;
        public float outlineAlpha = 0.55f;
        public float fillAlpha = 0.07f;
        public float stringAlpha = 0.45f;
        public float fadeSeconds = 0.8f;       // gentle fade-in when the guitar appears
        public float sparkleSeconds = 0.45f;   // string glow after a strum
        public float glintSeconds = 0.6f;
        public float soundholeRadius = 0.045f;
        public float soundholeFromNut = 0.43f;
        public float bodyLength = 0.50f;

        private readonly List<LineRenderer> _outlines = new List<LineRenderer>();
        private readonly List<Material> _fills = new List<Material>();
        private readonly LineRenderer[] _strings = new LineRenderer[6];
        private readonly float[] _glow = new float[6];
        private readonly float[] _glowAt = new float[6];
        private LineRenderer _soundhole;
        private Transform _root;
        private Material _lineMaterial;
        private float _fade;
        private float _fadeTarget;
        private bool _strumHint;

        private VirtualGuitar _guitar;
        private Vector3 _toward;

        // Glints: a small pool of soft dots.
        private const int GlintCount = 18;
        private Transform[] _glints;
        private Material[] _glintMats;
        private float[] _glintBorn;
        private Vector3[] _glintVel;
        private int _nextGlint;
        private static Texture2D _dot;

        private static readonly Color Cyan = new Color(0.35f, 0.88f, 1f, 1f);
        private static readonly Color StringColour = new Color(0.80f, 0.95f, 1f, 1f);

        public bool Visible { get { return _fadeTarget > 0f; } }

        /// <summary>(Re)builds the guitar shape for a placed VirtualGuitar. Starts invisible; call FadeIn().</summary>
        public void Build(VirtualGuitar guitar)
        {
            _guitar = guitar;
            Clear();
            _fade = 0f; // a rebuilt guitar always fades in, never pops
            if (_lineMaterial == null) _lineMaterial = Theme.UnlitMaterial(Color.white);
            _root = new GameObject("Air Guitar Shape").transform;
            _root.SetParent(transform, false);

            FretboardGeometry g = guitar.Geometry;
            _toward = guitar.Zone.TowardPlayer.ToUnity();
            Vector3 neck = g.NeckAxis.ToUnity();
            Vector3 across = g.AcrossAxis.ToUnity();
            Vector3 behindTop = -_toward * 0.012f;   // the top sits a little behind the strings
            Vector3 behindNeck = -_toward * 0.006f;

            // Neck: from the nut to where the body starts (14th fret), just outside the outer strings.
            float bodyStart = FretboardGeometry.FretDistance(g.ScaleLength, 14);
            const int neckSteps = 8;
            var neckLow = new Vector3[neckSteps + 1];
            var neckHigh = new Vector3[neckSteps + 1];
            for (int i = 0; i <= neckSteps; i++)
            {
                float d = bodyStart * i / neckSteps;
                neckLow[i] = g.StringPoint(0, d).ToUnity() - across * 0.004f + behindNeck;
                neckHigh[i] = g.StringPoint(5, d).ToUnity() + across * 0.004f + behindNeck;
            }
            AddLine("Neck edge low", neckLow, false, outlineWidth * 0.8f);
            AddLine("Neck edge high", neckHigh, false, outlineWidth * 0.8f);
            AddFill("Neck fill", neckHigh, neckLow, fillAlpha * 1.3f);

            // Headstock: a slim, slightly flared plate before the nut.
            Vector3 nutLow = neckLow[0], nutHigh = neckHigh[0];
            Vector3 nutMid = (nutLow + nutHigh) * 0.5f;
            Vector3 tipMid = nutMid - neck * 0.17f;
            var head = new[]
            {
                nutLow, tipMid - across * 0.044f, tipMid - across * 0.040f - neck * 0.012f,
                tipMid + across * 0.040f - neck * 0.012f, tipMid + across * 0.044f, nutHigh
            };
            AddLine("Headstock", head, true, outlineWidth * 0.8f);
            AddFill("Headstock fill", new[] { nutHigh, tipMid + across * 0.044f }, new[] { nutLow, tipMid - across * 0.044f }, fillAlpha);
            AddLine("Nut", new[] { nutLow, nutHigh }, false, outlineWidth);

            // Body: soft union of an upper and a lower bout, centred on the strings.
            Vector3 bodyOrigin = VirtualGuitar.StringCentre(g, bodyStart).ToUnity() + behindTop;
            const int bodySteps = 48;
            var top = new Vector3[bodySteps];
            var bottom = new Vector3[bodySteps];
            for (int i = 0; i < bodySteps; i++)
            {
                float t = i / (float)(bodySteps - 1);
                float x = bodyLength * 0.5f * (1f - Mathf.Cos(Mathf.PI * t)); // denser near the rounded ends
                float w = BodyHalfWidth(x / bodyLength) * (bodyLength / 0.50f);
                top[i] = bodyOrigin + neck * x - across * w;
                bottom[i] = bodyOrigin + neck * x + across * w;
            }
            var outline = new Vector3[bodySteps * 2];
            for (int i = 0; i < bodySteps; i++) { outline[i] = top[i]; outline[bodySteps * 2 - 1 - i] = bottom[i]; }
            AddLine("Body outline", outline, true, outlineWidth);
            AddFill("Body fill", top, bottom, fillAlpha);

            // Soundhole: the "strum here" ring.
            Vector3 holeCentre = VirtualGuitar.StringCentre(g, soundholeFromNut).ToUnity() + behindTop * 0.5f;
            const int ringSteps = 40;
            var ring = new Vector3[ringSteps];
            for (int i = 0; i < ringSteps; i++)
            {
                float a = 2f * Mathf.PI * i / ringSteps;
                ring[i] = holeCentre + neck * (Mathf.Cos(a) * soundholeRadius) + across * (Mathf.Sin(a) * soundholeRadius);
            }
            _soundhole = AddLine("Soundhole", ring, true, outlineWidth);

            // Bridge, and strings over the body (the overlay covers the neck up to fret 12).
            float bridge = g.ScaleLength;
            Vector3 bLow = g.StringPoint(0, bridge).ToUnity() - across * 0.012f + behindTop * 0.5f;
            Vector3 bHigh = g.StringPoint(5, bridge).ToUnity() + across * 0.012f + behindTop * 0.5f;
            AddLine("Bridge", new[] { bLow, bHigh, bHigh + neck * 0.025f, bLow + neck * 0.025f }, true, outlineWidth * 0.8f);

            float from = FretboardGeometry.FretDistance(g.ScaleLength, 12);
            for (int s = 0; s < 6; s++)
            {
                var pts = new[] { g.StringPoint(s, from).ToUnity(), g.StringPoint(s, bridge).ToUnity() };
                _strings[s] = AddLine("String " + s, pts, false, StringWidthFor(s), register: false);
                _glow[s] = 0f;
            }

            EnsureGlints();
            ApplyColours();
        }

        public void FadeIn() { gameObject.SetActive(true); _fadeTarget = 1f; }
        public void FadeOut() { _fadeTarget = 0f; }

        /// <summary>A slow, soft breathing on the soundhole ring while we wait for a strum.</summary>
        public void SetStrumHint(bool on) { _strumHint = on; }

        /// <summary>Strings glow briefly in strum order; a few glints drift up from the strum spot.</summary>
        public void Sparkle(bool[] played, bool upStrum, float stagger = 0.012f, int glints = 3)
        {
            if (_guitar == null) return;
            int order = 0;
            for (int k = 0; k < 6; k++)
            {
                int s = upStrum ? 5 - k : k;
                if (played != null && !played[s]) continue;
                _glowAt[s] = Time.unscaledTime + order * stagger;
                order++;
            }
            for (int i = 0; i < glints; i++) SpawnGlint(Random.Range(0, 6), Random.Range(-0.05f, 0.05f));
        }

        /// <summary>The Day 1 finish: every string glows and a little shower of glints. Small, not full-screen.</summary>
        public void Celebrate()
        {
            Sparkle(null, false, 0.05f, 0);
            for (int i = 0; i < GlintCount; i++) SpawnGlint(i % 6, Random.Range(-0.12f, 0.12f));
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _fade = Mathf.MoveTowards(_fade, _fadeTarget, dt / Mathf.Max(0.05f, fadeSeconds));
            if (_fade <= 0f && _fadeTarget <= 0f) { if (_root != null) _root.gameObject.SetActive(false); return; }
            if (_root != null && !_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

            float now = Time.unscaledTime;
            for (int s = 0; s < 6; s++)
            {
                if (_glowAt[s] > 0f && now >= _glowAt[s]) { _glow[s] = 1f; _glowAt[s] = 0f; }
                else _glow[s] = Mathf.MoveTowards(_glow[s], 0f, dt / sparkleSeconds);
            }
            ApplyColours();
            UpdateGlints(now);
        }

        private void ApplyColours()
        {
            float a = Theme.Ease(_fade) * Theme.OverlayBrightness;
            Color line = Theme.WithAlpha(Cyan, outlineAlpha * a);
            for (int i = 0; i < _outlines.Count; i++)
                if (_outlines[i] != null) { _outlines[i].startColor = line; _outlines[i].endColor = line; }
            for (int i = 0; i < _fills.Count; i++)
                if (_fills[i] != null) _fills[i].color = Theme.WithAlpha(Cyan, fillAlpha * a);

            if (_soundhole != null)
            {
                float breathe = _strumHint ? 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI)) : 0f; // 0.5 Hz
                Color c = Theme.WithAlpha(Cyan, Mathf.Clamp01(outlineAlpha + breathe) * a);
                _soundhole.startColor = c; _soundhole.endColor = c;
            }

            for (int s = 0; s < 6; s++)
            {
                if (_strings[s] == null) continue;
                float glow = Theme.Ease(_glow[s]);
                Color c = Color.Lerp(Theme.WithAlpha(StringColour, stringAlpha), Theme.WithAlpha(Cyan, 1f), glow);
                c.a *= a;
                _strings[s].startColor = c; _strings[s].endColor = c;
                _strings[s].widthMultiplier = StringWidthFor(s) * (1f + 0.9f * glow);
            }
        }

        // ---------- shapes ----------

        /// <summary>Half the body width at u (0 = neck end, 1 = tail), for a 0.50 m body. Soft max of two bouts.</summary>
        private static float BodyHalfWidth(float u)
        {
            float x = u * 0.50f;
            float upper = Bout(x, 0.13f, 0.13f, 0.140f);
            float lower = Bout(x, 0.33f, 0.17f, 0.190f);
            const float p = 4f; // soft union fills the waist into a smooth curve
            return Mathf.Pow(Mathf.Pow(upper, p) + Mathf.Pow(lower, p), 1f / p);
        }

        private static float Bout(float x, float centre, float halfLength, float halfWidth)
        {
            float k = (x - centre) / halfLength;
            return k * k >= 1f ? 0f : halfWidth * Mathf.Sqrt(1f - k * k);
        }

        private float StringWidthFor(int s) { return stringWidth * Mathf.Lerp(1.35f, 0.75f, s / 5f); }

        private LineRenderer AddLine(string name, Vector3[] points, bool loop, float width, bool register = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = points.Length;
            lr.SetPositions(points);
            lr.widthMultiplier = width;
            lr.sharedMaterial = _lineMaterial;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch;
            if (register) _outlines.Add(lr);
            return lr;
        }

        /// <summary>A thin translucent strip between two matching edges (both run the same way).</summary>
        private void AddFill(string name, Vector3[] edgeA, Vector3[] edgeB, float alpha)
        {
            int n = Mathf.Min(edgeA.Length, edgeB.Length);
            if (n < 2) return;
            var verts = new Vector3[n * 2];
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n; i++) { verts[i * 2] = edgeA[i]; verts[i * 2 + 1] = edgeB[i]; }
            for (int i = 0; i < n - 1; i++)
            {
                int v = i * 2, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = Theme.UnlitMaterial(Theme.WithAlpha(Cyan, alpha));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _fills.Add(mat);
        }

        private void Clear()
        {
            foreach (var m in _fills) if (m != null) Destroy(m);
            _fills.Clear();
            _outlines.Clear();
            if (_root != null)
            {
                foreach (var mf in _root.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(_root.gameObject);
            }
            _root = null;
            _soundhole = null;
            for (int s = 0; s < 6; s++) _strings[s] = null;
        }

        private void OnDestroy()
        {
            Clear();
            if (_glintMats != null) foreach (var m in _glintMats) if (m != null) Destroy(m);
            if (_lineMaterial != null) Destroy(_lineMaterial);
        }

        // ---------- glints ----------

        private void EnsureGlints()
        {
            if (_glints != null) return;
            if (_dot == null) _dot = MakeDot(32);
            _glints = new Transform[GlintCount];
            _glintMats = new Material[GlintCount];
            _glintBorn = new float[GlintCount];
            _glintVel = new Vector3[GlintCount];
            for (int i = 0; i < GlintCount; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Glint";
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                q.transform.localScale = Vector3.one * 0.012f;
                var mat = Theme.UnlitMaterial(Theme.WithAlpha(Cyan, 0f));
                mat.mainTexture = _dot;
                var r = q.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                q.SetActive(false);
                _glints[i] = q.transform;
                _glintMats[i] = mat;
                _glintBorn[i] = -10f;
            }
        }

        private void SpawnGlint(int stringIndex, float alongOffset)
        {
            if (_glints == null || _guitar == null) return;
            FretboardGeometry g = _guitar.Geometry;
            float d = _guitar.StrumDistanceFromNut + alongOffset;
            Vector3 p = g.StringPoint(Mathf.Clamp(stringIndex, 0, 5), d).ToUnity() + _toward * 0.004f;

            int i = _nextGlint;
            _nextGlint = (_nextGlint + 1) % GlintCount;
            _glints[i].position = p;
            _glints[i].gameObject.SetActive(true);
            UIKit.FacePlayer(_glints[i]);
            _glintBorn[i] = Time.unscaledTime;
            _glintVel[i] = (Vector3.up * 0.10f + _toward * 0.05f + Random.insideUnitSphere * 0.03f);
        }

        private void UpdateGlints(float now)
        {
            if (_glints == null) return;
            float dt = Time.unscaledDeltaTime;
            float a = Theme.Ease(_fade) * Theme.OverlayBrightness;
            for (int i = 0; i < GlintCount; i++)
            {
                if (!_glints[i].gameObject.activeSelf) continue;
                float age = (now - _glintBorn[i]) / glintSeconds;
                if (age >= 1f) { _glints[i].gameObject.SetActive(false); continue; }
                _glints[i].position += _glintVel[i] * dt;
                // Rise in quickly, ease out slowly: soft, never a flash.
                float env = age < 0.2f ? Theme.Ease(age / 0.2f) : 1f - Theme.Ease((age - 0.2f) / 0.8f);
                _glintMats[i].color = Theme.WithAlpha(Cyan, 0.8f * env * a);
                _glints[i].localScale = Vector3.one * Mathf.Lerp(0.012f, 0.006f, age);
            }
        }

        private static Texture2D MakeDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                    float k = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * size + x] = new Color(1f, 1f, 1f, k * k);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}

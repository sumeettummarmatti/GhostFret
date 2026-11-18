#if UNITY_EDITOR
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.Util;
using UnityEngine;

namespace GhostFret.Runtime.Preview
{
    /// <summary>
    /// Editor preview only: a simple acoustic guitar built from primitives, held where a seated,
    /// right-handed player holds one (body on the right leg, neck to the left, top tilted back toward the eyes).
    /// The three calibration points are worked out first and the strings and frets are drawn FROM that
    /// FretboardGeometry, so the overlay and ghost hand land exactly on this neck.
    /// Local frame: origin = middle of the nut on the string plane, +Z = nut → body, +Y = out of the top.
    /// </summary>
    public sealed class StandInGuitar : MonoBehaviour
    {
        [Header("Where it is held, relative to the eyes (metres / degrees)")]
        public float forward = 0.40f;            // string plane near the bridge, in front of the eyes
        public float below = 0.47f;              // ...and this far below them
        public float right = 0.10f;              // body sits a little to the player's right
        public float neckRiseDegrees = 18f;      // headstock higher than the body
        public float neckForwardDegrees = 14f;   // headstock further away than the body
        public float topTiltBackDegrees = 70f;   // 0 = top faces straight ahead (concert hold); beginners tilt it back to see the board

        [Header("Size")]
        public float scaleLength = 0.648f;       // 25.5"
        public float nutStringSpread = 0.036f;   // low E to high E at the nut

        public FretboardGeometry Geometry { get; private set; }

        private const float BridgeFromNut = 0.60f;   // where the "held here" point sits along the neck
        private const float FretboardTop = -0.0035f; // below the string plane (≈2.5 mm action)
        private const float BodyTop = -0.013f;
        private const float BodyDepth = 0.10f;
        private const int LastFret = 20;

        private float _acrossSign = 1f;   // +1 if local +X points from low E toward high E

        // ---------- Build ----------

        /// <summary>Places the guitar for a seated player at eye position `head` facing `flatForward`.</summary>
        public void Build(Vector3 head, Vector3 flatForward)
        {
            Vector3 fwd = flatForward.normalized;
            Vector3 up = Vector3.up;
            Vector3 side = Vector3.Cross(up, fwd).normalized;   // player's right

            float rise = neckRiseDegrees * Mathf.Deg2Rad;
            float yaw = neckForwardDegrees * Mathf.Deg2Rad;
            // Nut → body: to the right, a little lower and a little nearer than the headstock.
            Vector3 neck = (side * Mathf.Cos(rise) * Mathf.Cos(yaw) - up * Mathf.Sin(rise) - fwd * Mathf.Cos(rise) * Mathf.Sin(yaw)).normalized;

            Vector3 heldPoint = head + fwd * forward - up * below + side * right;
            Vector3 nutMid = heldPoint - neck * BridgeFromNut;

            // The top faces away from the player, tilted back toward the ceiling so the board is visible.
            float tilt = topTiltBackDegrees * Mathf.Deg2Rad;
            Vector3 want = fwd * Mathf.Cos(tilt) + up * Mathf.Sin(tilt);
            Vector3 face = (want - Vector3.Dot(want, neck) * neck).normalized;

            // Right-handed guitar: looking at the top with the headstock on your right, low E is the upper
            // string. That fixes low E → high E as neck × face (low E ends up nearest the player's eyes).
            Vector3 across = Vector3.Cross(neck, face).normalized;

            Vector3 nutLowE = nutMid - across * (nutStringSpread * 0.5f);
            Vector3 nutHighE = nutMid + across * (nutStringSpread * 0.5f);
            Vector3 twelfthLowE = nutLowE + neck * (scaleLength * 0.5f);
            Geometry = FretboardGeometry.FromCalibration(nutLowE.ToNumerics(), nutHighE.ToNumerics(), twelfthLowE.ToNumerics());

            transform.SetPositionAndRotation(nutMid, Quaternion.LookRotation(neck, face));
            _acrossSign = Vector3.Dot(transform.right, across) >= 0f ? 1f : -1f;

            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            BuildBody();
            BuildNeck();
            BuildHeadstock();
            BuildStrings();
        }

        // ---------- Parts ----------

        private void BuildBody()
        {
            float join = FretboardGeometry.FretDistance(scaleLength, 14);
            float upperZ = join + 0.11f, upperD = 0.29f;
            float lowerZ = upperZ + 0.23f, lowerD = 0.39f;
            float midY = BodyTop - 0.001f - BodyDepth * 0.5f;

            // Sides and back (darker), then a spruce top a hair smaller so a thin dark edge reads as binding.
            Disc("Upper Bout", new Vector3(0, midY, upperZ), upperD, BodyDepth, Palette.Mahogany);
            Disc("Lower Bout", new Vector3(0, midY, lowerZ), lowerD, BodyDepth, Palette.Mahogany);
            Disc("Top Upper", new Vector3(0, BodyTop - 0.001f, upperZ), upperD - 0.006f, 0.002f, Palette.Spruce);
            Disc("Top Lower", new Vector3(0, BodyTop - 0.001f, lowerZ), lowerD - 0.006f, 0.002f, Palette.Spruce);

            float holeZ = FretboardGeometry.FretDistance(scaleLength, LastFret) + 0.062f;
            Disc("Pickguard", new Vector3(Across(0.065f), BodyTop + 0.0001f, holeZ + 0.06f), 0.085f, 0.0004f, Palette.Tortoise);
            Disc("Rosette", new Vector3(0, BodyTop + 0.0002f, holeZ), 0.128f, 0.0004f, Palette.Rosewood);
            Disc("Rosette Inlay", new Vector3(0, BodyTop + 0.0003f, holeZ), 0.116f, 0.0004f, Palette.Bone);
            Disc("Soundhole", new Vector3(0, BodyTop + 0.0004f, holeZ), 0.100f, 0.0004f, Palette.Hole);

            Box("Bridge", new Vector3(0, BodyTop + 0.004f, scaleLength + 0.008f), new Vector3(0.15f, 0.008f, 0.028f), Palette.Rosewood);
            Box("Saddle", new Vector3(0, BodyTop + 0.0095f, scaleLength), new Vector3(0.075f, 0.004f, 0.003f), Palette.Bone);
            for (int s = 0; s < 6; s++)
            {
                float x = Across(-0.025f + s * 0.01f);
                Disc("Bridge Pin " + (s + 1), new Vector3(x, BodyTop + 0.0085f, scaleLength + 0.016f), 0.005f, 0.002f, Palette.Bone);
            }
        }

        private void BuildNeck()
        {
            float join = FretboardGeometry.FretDistance(scaleLength, 14) - 0.03f;   // the neck meets the body here
            float fbEnd = FretboardGeometry.FretDistance(scaleLength, LastFret) + 0.006f;

            // Fretboard and neck as short segments so they taper with the strings.
            float prev = -0.001f;
            for (int f = 1; f <= LastFret + 1; f++)
            {
                float next = f <= LastFret ? FretboardGeometry.FretDistance(scaleLength, f) : fbEnd;
                if (next <= prev) continue;
                float mid = (prev + next) * 0.5f;
                Box("Fretboard " + f, new Vector3(0, FretboardTop - 0.003f, mid), new Vector3(BoardWidth(mid), 0.006f, next - prev + 0.0002f), Palette.Rosewood);
                if (prev < join)
                {
                    float end = Mathf.Min(next, join);
                    float m = (prev + end) * 0.5f;
                    Box("Neck " + f, new Vector3(0, FretboardTop - 0.006f - 0.011f, m), new Vector3(BoardWidth(m) - 0.002f, 0.022f, end - prev + 0.0002f), Palette.NeckWood);
                }
                prev = next;
            }

            Box("Nut", new Vector3(0, FretboardTop + 0.0025f, -0.0015f), new Vector3(BoardWidth(0f), 0.0055f, 0.004f), Palette.Bone);

            for (int f = 1; f <= LastFret; f++)
            {
                float z = FretboardGeometry.FretDistance(scaleLength, f);
                Box("Fret " + f, new Vector3(0, FretboardTop + 0.0004f, z), new Vector3(BoardWidth(z), 0.0016f, 0.0016f), Palette.Nickel);
            }

            int[] dots = { 3, 5, 7, 9, 15, 17 };
            foreach (int f in dots) Inlay(f, 0f);
            Inlay(12, -0.011f);
            Inlay(12, 0.011f);
        }

        private void BuildHeadstock()
        {
            // Angled back ~13° behind the nut, like a real acoustic.
            var head = new GameObject("Headstock").transform;
            head.SetParent(transform, false);
            head.localPosition = new Vector3(0, FretboardTop - 0.008f, -0.004f);
            head.localRotation = Quaternion.Euler(-13f, 0f, 0f);

            Box("Headstock Plate", new Vector3(0, 0, -0.095f), new Vector3(0.078f, 0.016f, 0.19f), Palette.Rosewood, head);

            for (int s = 0; s < 6; s++)
            {
                Vector3 post = PostLocal(s);
                Transform p = Disc("Tuner Post " + (s + 1), post, 0.006f, 0.012f, Palette.Nickel, head);
                p.localPosition = post - new Vector3(0, 0.003f, 0);
                float sideSign = Mathf.Sign(post.x);
                Transform button = Primitive(PrimitiveType.Sphere, "Tuner Button " + (s + 1), head, Palette.Bone);
                button.localPosition = new Vector3(sideSign * 0.052f, 0f, post.z);
                button.localScale = new Vector3(0.018f, 0.008f, 0.013f);
            }
        }

        private void BuildStrings()
        {
            Transform head = transform.Find("Headstock");
            for (int s = 0; s < 6; s++)
            {
                // Strings sit 1 mm behind the overlay's string lines so the overlay is never hidden.
                Vector3 back = transform.up * -0.001f;
                Vector3 nut = Geometry.StringPoint(s, 0f).ToUnity() + back;
                Vector3 saddle = Geometry.StringPoint(s, scaleLength).ToUnity() + back;
                float thick = Mathf.Lerp(0.0016f, 0.0008f, s / 5f);
                Color c = s <= 3 ? Palette.Bronze : Palette.Steel;
                Rod("String " + (s + 1), nut, saddle, thick, c);
                if (head != null)
                {
                    Vector3 postTop = head.TransformPoint(PostLocal(s) + new Vector3(0, 0.004f, 0));
                    Rod("String " + (s + 1) + " (head)", nut, postTop, thick, c);
                }
            }
        }

        // ---------- Layout helpers ----------

        /// <summary>Width of the fretboard at a distance from the nut: the strings plus a small edge margin.</summary>
        private float BoardWidth(float d)
        {
            return Geometry.WidthAt(Mathf.Max(0f, d)) + 0.007f;
        }

        /// <summary>Local X for a distance toward the high E side (negative = toward the low E side).</summary>
        private float Across(float towardHighE)
        {
            return towardHighE * _acrossSign;
        }

        /// <summary>3+3 tuner posts: low E, A, D on the bass side (D nearest the nut); G, B, high E on the treble side.</summary>
        private Vector3 PostLocal(int s)
        {
            bool bass = s <= 2;
            int rank = bass ? 2 - s : s - 3;   // 0 = nearest the nut
            float side = Across(bass ? -0.024f : 0.024f);
            return new Vector3(side, 0.014f, -0.05f - rank * 0.045f);
        }

        private void Inlay(int fret, float towardHighE)
        {
            float a = FretboardGeometry.FretDistance(scaleLength, fret - 1);
            float b = FretboardGeometry.FretDistance(scaleLength, fret);
            Disc("Inlay " + fret, new Vector3(Across(towardHighE), FretboardTop + 0.0001f, (a + b) * 0.5f), 0.006f, 0.0004f, Palette.Pearl);
        }

        // ---------- Primitive helpers ----------

        private Transform Primitive(PrimitiveType type, string name, Transform parent, Color colour)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);   // nothing here should catch rays or physics
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.GetComponent<Renderer>().sharedMaterial = Palette.Lit(colour);
            return go.transform;
        }

        private Transform Box(string name, Vector3 localPos, Vector3 size, Color colour, Transform parent = null)
        {
            Transform t = Primitive(PrimitiveType.Cube, name, parent, colour);
            t.localPosition = localPos;
            t.localScale = size;
            return t;
        }

        /// <summary>A flat cylinder whose axis is the local Y (out of the guitar top).</summary>
        private Transform Disc(string name, Vector3 localPos, float diameter, float thickness, Color colour, Transform parent = null)
        {
            Transform t = Primitive(PrimitiveType.Cylinder, name, parent, colour);
            t.localPosition = localPos;
            t.localScale = new Vector3(diameter, thickness * 0.5f, diameter);   // the cylinder mesh is 2 units tall
            return t;
        }

        /// <summary>A thin cylinder between two world points.</summary>
        private void Rod(string name, Vector3 a, Vector3 b, float diameter, Color colour)
        {
            Transform t = Primitive(PrimitiveType.Cylinder, name, transform, colour);
            Vector3 d = b - a;
            t.position = (a + b) * 0.5f;
            t.rotation = Quaternion.FromToRotation(Vector3.up, d);
            t.localScale = new Vector3(diameter, d.magnitude * 0.5f, diameter);
        }
    }
}
#endif

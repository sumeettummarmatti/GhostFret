#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostFret.Runtime.Preview
{
    /// <summary>
    /// Editor preview only: a dim, neutral room in place of passthrough, so the UI is judged against
    /// something like a living room instead of a bright skybox. Floor, a soft rug, one faint wall, a
    /// warm key light (only if the scene has no light) and flat ambient light.
    /// </summary>
    public static class PreviewRoom
    {
        public static Transform Build(Vector3 head, Vector3 flatForward, Camera cam)
        {
            var root = new GameObject("Preview Room (Editor only)").transform;
            Vector3 fwd = flatForward.normalized;
            Vector3 feet = new Vector3(head.x, 0f, head.z);

            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Palette.RoomClear;
            }

            Transform floor = Make(PrimitiveType.Plane, "Floor", root, Palette.Floor);
            floor.position = feet;
            floor.localScale = new Vector3(0.8f, 1f, 0.8f);   // the plane mesh is 10 m, so 8 m across

            Transform rug = Make(PrimitiveType.Cylinder, "Rug", root, Palette.Rug);
            rug.position = feet + fwd * 0.35f + Vector3.up * 0.002f;
            rug.localScale = new Vector3(1.7f, 0.002f, 1.7f);

            Transform wall = Make(PrimitiveType.Quad, "Wall", root, Palette.Wall);
            wall.position = feet + fwd * 2.6f + Vector3.up * 1.5f;
            wall.rotation = Quaternion.LookRotation(fwd, Vector3.up);   // the quad's visible face (−Z) faces the player
            wall.localScale = new Vector3(8f, 3f, 1f);

            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Preview Key Light");
                lightGo.transform.SetParent(root, false);
                Light key = lightGo.AddComponent<Light>();
                key.type = LightType.Directional;
                key.color = new Color(1f, 0.94f, 0.86f);
                key.intensity = 1.1f;
                key.shadows = LightShadows.Soft;
                // From above, behind and to the left of the player, like a lamp over the shoulder.
                Vector3 dir = (fwd + Vector3.Cross(Vector3.up, fwd) * 0.5f + Vector3.down * 1.4f).normalized;
                lightGo.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Palette.Ambient;
            return root;
        }

        private static Transform Make(PrimitiveType type, string name, Transform parent, Color colour)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = Palette.Lit(colour);
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
#endif

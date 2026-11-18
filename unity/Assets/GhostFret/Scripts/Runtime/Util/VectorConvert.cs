using N = System.Numerics;
using U = UnityEngine;

namespace GhostFret.Runtime.Util
{
    /// <summary>Core uses System.Numerics; Unity uses UnityEngine. Convert at the boundary.</summary>
    public static class VectorConvert
    {
        public static U.Vector3 ToUnity(this N.Vector3 v) { return new U.Vector3(v.X, v.Y, v.Z); }
        public static N.Vector3 ToNumerics(this U.Vector3 v) { return new N.Vector3(v.x, v.y, v.z); }
    }
}

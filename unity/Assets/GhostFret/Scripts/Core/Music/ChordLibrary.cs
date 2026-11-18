using System.Collections.Generic;

namespace GhostFret.Core.Music
{
    /// <summary>
    /// The beginner chord set. This is the single source of truth for chord shapes.
    /// Arrays: low E → high E. −1 = skip string, 0 = open.
    /// </summary>
    public static class ChordLibrary
    {
        private static readonly Dictionary<string, ChordShape> Shapes = new Dictionary<string, ChordShape>
        {
            { "Em", new ChordShape("Em", new[] { 0, 2, 2, 0, 0, 0 },    new[] { 0, 2, 3, 0, 0, 0 }, "Two fingers. The friendliest first chord.") },
            { "D",  new ChordShape("D",  new[] { -1, -1, 0, 2, 3, 2 },  new[] { 0, 0, 0, 1, 3, 2 }, "Strum only the top four strings.") },
            { "G",  new ChordShape("G",  new[] { 3, 2, 0, 0, 0, 3 },    new[] { 2, 1, 0, 0, 0, 3 }, "Simplified three-finger G.") },
            { "C",  new ChordShape("C",  new[] { -1, 3, 2, 0, 1, 0 },   new[] { 0, 3, 2, 0, 1, 0 }, "Skip the low E string.") },
            { "Am", new ChordShape("Am", new[] { -1, 0, 2, 2, 1, 0 },   new[] { 0, 0, 2, 3, 1, 0 }, "Same shape as E, one string over.") },
            { "A",  new ChordShape("A",  new[] { -1, 0, 2, 2, 2, 0 },   new[] { 0, 0, 1, 2, 3, 0 }, "Three fingers side by side on fret 2.") },
            { "E",  new ChordShape("E",  new[] { 0, 2, 2, 1, 0, 0 },    new[] { 0, 2, 3, 1, 0, 0 }, "Like Em plus one finger.") },
            { "Dm", new ChordShape("Dm", new[] { -1, -1, 0, 2, 3, 1 },  new[] { 0, 0, 0, 2, 3, 1 }, "Strum only the top four strings.") },
            { "F",  new ChordShape("F",  new[] { -1, -1, 3, 2, 1, 1 },  new[] { 0, 0, 3, 2, 1, 1 }, "Mini F: index finger covers the top two strings.") },
        };

        /// <summary>Teaching order used by the curriculum.</summary>
        public static readonly string[] TeachingOrder = { "Em", "D", "G", "C", "Am", "A", "E", "Dm", "F" };

        public static bool Contains(string name)
        {
            return name != null && Shapes.ContainsKey(name);
        }

        public static ChordShape Get(string name)
        {
            ChordShape shape;
            if (name != null && Shapes.TryGetValue(name, out shape)) return shape;
            throw new KeyNotFoundException("Unknown chord: " + name);
        }

        public static IEnumerable<ChordShape> All()
        {
            return Shapes.Values;
        }
    }
}

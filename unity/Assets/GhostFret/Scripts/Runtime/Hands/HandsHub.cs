namespace GhostFret.Runtime.Hands
{
    /// <summary>
    /// One shared place to get the active hand input, so UI widgets (buttons, gestures) don't each
    /// need a serialized reference. GhostFretApp sets it at startup.
    /// </summary>
    public static class HandsHub
    {
        public static IHandInput Input { get; set; }
    }
}

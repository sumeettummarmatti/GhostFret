using UnityEngine;

namespace GhostFret.Runtime.UI
{
    /// <summary>
    /// Small app sounds made in code (no audio assets): soft wooden click, gentle chime, friendly "hmm",
    /// metronome ticks. Kept quieter than the guitar (UX_GUIDE.md "Sound").
    /// Every sound that carries meaning must also have a caption (see Coach.Say).
    /// </summary>
    public static class UiSounds
    {
        public static float Volume = 0.6f;   // Quest speakers are quiet; still softer than a strummed guitar

        private const int Rate = 48000;
        private static AudioClip _click, _chime, _hmm, _tick, _tickAccent;
        private static AudioSource _source;

        public static AudioClip Click { get { return _click ?? (_click = Make("click", 0.05f, Wood(1800f, 60f))); } }
        public static AudioClip Chime { get { return _chime ?? (_chime = Make("chime", 0.9f, Bell(880f, 1320f, 4f))); } }
        public static AudioClip Hmm { get { return _hmm ?? (_hmm = Make("hmm", 0.35f, Soft(196f, 165f))); } }
        public static AudioClip Tick { get { return _tick ?? (_tick = Make("tick", 0.04f, Wood(1400f, 90f))); } }
        public static AudioClip TickAccent { get { return _tickAccent ?? (_tickAccent = Make("tickAccent", 0.05f, Wood(2000f, 70f))); } }

        /// <summary>Plays a one-shot near the player. Pass a position to spatialise (e.g. beside the guitar).</summary>
        public static void Play(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;
            EnsureSource();
            _source.PlayOneShot(clip, Volume * volumeScale);
        }

        public static void PlayAt(AudioClip clip, Vector3 position, float volumeScale = 1f)
        {
            if (clip == null) return;
            AudioSource.PlayClipAtPoint(clip, position, Volume * volumeScale);
        }

        private static void EnsureSource()
        {
            if (_source != null) return;
            var go = new GameObject("GhostFret UiSounds");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }

        private delegate float Wave(float t);

        private static AudioClip Make(string name, float seconds, Wave wave)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            // Short fade at both ends so nothing clicks harshly.
            int fade = Mathf.Min(96, n / 4);
            for (int i = 0; i < fade; i++) { float g = i / (float)fade; data[i] *= g; data[n - 1 - i] *= g; }
            var clip = AudioClip.Create("GhostFret_" + name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Pitched, fast-decaying sine with a little noise: a wooden knock.
        private static Wave Wood(float hz, float decay)
        {
            var rng = new System.Random(7);
            return t => Mathf.Exp(-decay * t) * (0.8f * Mathf.Sin(2f * Mathf.PI * hz * t) + 0.2f * (float)(rng.NextDouble() * 2 - 1));
        }

        // Two soft partials with a slow decay: a gentle chime.
        private static Wave Bell(float a, float b, float decay)
        {
            return t => Mathf.Exp(-decay * t) * (0.55f * Mathf.Sin(2f * Mathf.PI * a * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * b * t));
        }

        // Low, rounded tone that slides down slightly: a friendly "hmm", never a buzzer.
        private static Wave Soft(float from, float to)
        {
            return t =>
            {
                float hz = Mathf.Lerp(from, to, t / 0.35f);
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.35f));
                return 0.5f * env * Mathf.Sin(2f * Mathf.PI * hz * t);
            };
        }
    }
}

using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace GhostFret.Runtime.Audio
{
    /// <summary>
    /// Reads the headset microphone into a looping buffer. Audio is never saved or sent.
    /// TODO (Phase 1): explain the permission before asking ("so GhostFret can hear your guitar").
    /// </summary>
    public sealed class MicrophoneInput : MonoBehaviour
    {
        public int sampleRate = 48000;
        public int bufferSeconds = 2;

        public bool IsRunning { get; private set; }
        public int SampleRate { get { return sampleRate; } }

        private AudioClip _clip;
        private string _device;

        public void StartListening()
        {
#if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                return; // TODO: retry when permission is granted (PermissionCallbacks)
            }
#endif
            _device = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;
            _clip = Microphone.Start(_device, true, bufferSeconds, sampleRate);
            IsRunning = _clip != null;
        }

        public void StopListening()
        {
            if (IsRunning) Microphone.End(_device);
            IsRunning = false;
        }

        /// <summary>Copies the most recent samples into target (newest at the end).</summary>
        public bool GetLatest(float[] target)
        {
            if (!IsRunning || target.Length > _clip.samples) return false;
            int pos = Microphone.GetPosition(_device) - target.Length;
            if (pos < 0) pos += _clip.samples;
            return _clip.GetData(target, pos);
        }

        /// <summary>Approximate time (seconds) of the newest sample, on the audio DSP clock.</summary>
        public double NewestSampleTime { get { return AudioSettings.dspTime; } }

        private void OnDisable() { StopListening(); }
    }
}

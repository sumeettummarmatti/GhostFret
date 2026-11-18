#if UNITY_EDITOR
using System;
using GhostFret.Core.Feedback;
using GhostFret.Core.Lesson;
using GhostFret.Core.Strum;
using GhostFret.Core.Timing;
using GhostFret.Runtime.App;
using GhostFret.Runtime.Hands;
using GhostFret.Runtime.UI;
using GhostFret.Runtime.Util;
using UnityEngine;

namespace GhostFret.Runtime.Preview
{
    /// <summary>
    /// Editor-only preview: press Play on a laptop (no headset, no guitar) and see the app as it looks
    /// on a guitar, and trigger every feedback state from the keyboard.
    ///  - Seats the camera at eye height looking down at a stand-in acoustic guitar in a dim room
    ///    (only when no XR device drives the camera, i.e. no passthrough).
    ///  - Fits the fretboard to the stand-in as if the player touched the three spots and pressed "Looks right".
    ///  - Keyboard strums go through AudioCoach.InjectForPreview, the same events real audio raises.
    /// Added by GhostFretApp when running in the Editor with mouse hands. Never compiled into a build.
    /// </summary>
    public sealed class EditorPreview : MonoBehaviour
    {
        [Header("Tuning values (Editor preview only)")]
        public float seatedEyeHeight = 1.15f;
        public float fieldOfView = 70f;              // closer to the headset's view than Unity's default 60
        public float lookDegreesPerUnit = 2.5f;      // right-drag mouse look speed
        public float buttonHoverGap = 0.015f;        // mouse fingertip sits this far in front of a button under the cursor
        public bool ignoreRealMic = true;            // laptop mics hear key clicks; L toggles

        private GhostFretApp _app;
        private AppServices _s;
        private EditorMouseHandInput _mouse;
        private Camera _cam;
        private Transform _rig;
        private bool _xrActive;
        private StandInGuitar _guitar;
        private PreviewLegend _legend;
        private Transform _cursor;
        private StrumDirection _lastFreeDirection = StrumDirection.Up;
        private string _lastAction = "";
        private float _statusAt;

        public void Init(GhostFretApp app, AppServices services)
        {
            _app = app;
            _s = services;
            _mouse = services.Hands as EditorMouseHandInput;
        }

        // ---------- Setup ----------

        private void Start()
        {
            if (_app == null || _s == null) { enabled = false; return; }

            _cam = EnsureCamera();
            if (_mouse != null) _mouse.viewCamera = _cam;
            _xrActive = UnityEngine.XR.XRSettings.isDeviceActive;
            _rig = PickRig(_cam);

            Vector3 flatFwd = _cam.transform.forward;
            flatFwd.y = 0f;
            if (flatFwd.sqrMagnitude < 1e-6f) flatFwd = Vector3.forward;
            flatFwd.Normalize();
            Vector3 head = _cam.transform.position;
            if (!_xrActive) head.y = seatedEyeHeight;

            var guitarGo = new GameObject("Stand-in Guitar (Editor only)");
            _guitar = guitarGo.AddComponent<StandInGuitar>();
            _guitar.Build(head, flatFwd);

            if (!_xrActive)
            {
                PreviewRoom.Build(head, flatFwd, _cam);
                SeatCamera(head);
            }

            _cursor = MakeCursor();
            _legend = PreviewLegend.Create(_cam);
            _s.Audio.PreviewIgnoreMic = ignoreRealMic;

            Fit();
            KeepRestingHandOffTheNut();
            Debug.Log("GhostFret Editor preview: stand-in guitar fitted. Press H for the key map.");
        }

        private Camera EnsureCamera()
        {
            Camera cam = Camera.main;
            if (cam != null) return cam;
            var go = new GameObject("Preview Camera (Editor only)");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            if (FindFirstObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
            return cam;
        }

        /// <summary>
        /// What to move to place the camera: a rig (e.g. OVRCameraRig) rewrites the camera's local pose,
        /// so move its root instead, unless the app itself lives under that root.
        /// </summary>
        private Transform PickRig(Camera cam)
        {
            Transform root = cam.transform.root;
            if (root == cam.transform) return root;
            if (_app.transform.IsChildOf(root)) return cam.transform;
            return root;
        }

        /// <summary>Seated eye height, looking down at the neck with room above it for the cards.</summary>
        private void SeatCamera(Vector3 head)
        {
            _cam.fieldOfView = fieldOfView;
            _cam.nearClipPlane = 0.03f;
            var g = _guitar.Geometry;
            Vector3 target = g.BoardPoint(2.5f, g.ScaleLength * 0.5f).ToUnity() + Vector3.up * 0.12f;
            Quaternion want = Quaternion.LookRotation(target - head, Vector3.up);

            Quaternion delta = want * Quaternion.Inverse(_cam.transform.rotation);
            _rig.rotation = delta * _rig.rotation;
            _rig.position += head - _cam.transform.position;
        }

        private Transform MakeCursor()
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Mouse Fingertip (Editor only)";
            Collider col = dot.GetComponent<Collider>();
            if (col != null) Destroy(col);
            dot.GetComponent<Renderer>().material = Theme.UnlitMaterial(Theme.WithAlpha(Color.white, 0.8f));
            dot.transform.localScale = Vector3.one * 0.008f;
            return dot.transform;
        }

        /// <summary>The idle (non-mouse) left hand must not rest on the nut, or it would start a quick re-fit.</summary>
        private void KeepRestingHandOffTheNut()
        {
            if (_mouse == null) return;
            Vector3 nut = _guitar.Geometry.NutLowE.ToUnity();
            for (int i = 0; i < 4; i++)
            {
                Vector3 rest = _cam.transform.TransformPoint(_mouse.restingLeftOffset);
                if (Vector3.Distance(rest, nut) > 0.12f) return;
                _mouse.restingLeftOffset += new Vector3(0f, -0.08f, -0.04f);
            }
        }

        // ---------- Every frame ----------

        private void Update()
        {
            if (_guitar == null) return;
            HandleKeys();
            MouseLook();
            SnapFingertipToButtonUnderCursor();
            UpdateCursor();

            // Air mode is for playing without a guitar: hide the stand-in so its own guitar reads clearly.
            SessionState st = _app.Flow.State;
            bool air = st == SessionState.AirMode || (st == SessionState.Paused && _app.Flow.PausedFrom == SessionState.AirMode);
            if (_guitar.gameObject.activeSelf == air) _guitar.gameObject.SetActive(!air);

            if (Time.unscaledTime >= _statusAt) { _statusAt = Time.unscaledTime + 0.2f; RefreshStatus(); }
        }

        private void HandleKeys()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (Input.GetKeyDown(KeyCode.H)) _legend.Visible = !_legend.Visible;
            if (Input.GetKeyDown(KeyCode.C)) Strum(Clean(), "Clean strum");
            if (Input.GetKeyDown(KeyCode.M)) Strum(StringTip(3, "The G string is muted. Arch your fingers so they don't touch it."), "Muted G string");
            if (Input.GetKeyDown(KeyCode.B)) Strum(StringTip(1, "Buzz on the A string. Press closer to the fret."), "Buzz on the A string");
            if (Input.GetKeyDown(KeyCode.W)) Strum(WrongChord(), "Wrong chord");
            if (Input.GetKeyDown(KeyCode.Q))
            {
                if (shift) Strum(ChordFeedback.NotSure(Target, 0.5), "Not sure");
                else Strum(ChordFeedback.TooQuiet(), "Too quiet");
            }
            if (Input.GetKeyDown(KeyCode.Space)) StrumOnBeat();
            if (Input.GetKeyDown(KeyCode.T)) FinishTuning();
            if (Input.GetKeyDown(KeyCode.F)) { Fit(); _lastAction = "Fretboard fitted to the stand-in"; }
            if (Input.GetKeyDown(KeyCode.A)) GoAir();
            if (Input.GetKeyDown(KeyCode.N)) Next();
            if (Input.GetKeyDown(KeyCode.L))
            {
                ignoreRealMic = !ignoreRealMic;
                _s.Audio.PreviewIgnoreMic = ignoreRealMic;
                _lastAction = ignoreRealMic ? "Real mic ignored" : "Listening to the real mic too";
            }
        }

        // ---------- Fake strums (same path as real audio) ----------

        private string Target
        {
            get { return string.IsNullOrEmpty(_s.Audio.TargetChord) ? "Em" : _s.Audio.TargetChord; }
        }

        private ChordFeedback Clean()
        {
            return new ChordFeedback { Kind = FeedbackKind.Clean, Message = "Clean " + Target + "!", HeardChord = Target, TargetScore = 0.95 };
        }

        /// <summary>
        /// Core has no separate "buzz" kind; a buzzing string is reported like a weak one (string highlight +
        /// amber tip), with DESIGN.md's buzz wording.
        /// </summary>
        private ChordFeedback StringTip(int stringIndex, string message)
        {
            return new ChordFeedback { Kind = FeedbackKind.WeakString, StringIndex = stringIndex, Message = message, HeardChord = Target, TargetScore = 0.85 };
        }

        private ChordFeedback WrongChord()
        {
            string heard = Target == "G" ? "C" : "G";
            return new ChordFeedback
            {
                Kind = FeedbackKind.WrongChord,
                Message = "That sounds like " + heard + ", not " + Target + ". Match the ghost hand and try again.",
                HeardChord = heard,
                TargetScore = 0.4
            };
        }

        private void Strum(ChordFeedback fb, string label)
        {
            Inject(fb, AudioSettings.dspTime, label);
        }

        private void Inject(ChordFeedback fb, double onsetDspTime, string label)
        {
            // InjectForPreview subtracts the input latency like a real onset, so add it back here.
            bool heard = _s.Audio.InjectForPreview(fb, onsetDspTime + _s.Audio.inputLatencySeconds);
            _lastAction = heard ? label : label + ": not listening right now (try a lesson, drill or song)";
        }

        /// <summary>
        /// Space: a clean strum exactly on the nearest beat slot of whatever is running (song or Strum Coach).
        /// Down on the beat, up on the "and", like real alternate strumming. Free strums alternate.
        /// </summary>
        private void StrumOnBeat()
        {
            double now = AudioSettings.dspTime;
            BeatGrid grid = _s.Songs != null && _s.Songs.IsPlaying && _s.Songs.Grid != null ? _s.Songs.Grid
                          : _s.Metronome != null && _s.Metronome.Running ? _s.Metronome.Grid : null;

            double t = now;
            StrumDirection dir;
            if (grid != null)
            {
                double latency = grid.LatencyMs / 1000.0;   // the grid subtracts this when judging
                int sub = Math.Max(1, grid.Subdivision);
                double slot = Math.Round(grid.BeatAt(now - latency) * sub) / sub;
                t = grid.TimeOfBeat(slot) + latency;
                dir = Math.Abs(slot - Math.Round(slot)) < 1e-6 ? StrumDirection.Down : StrumDirection.Up;
            }
            else
            {
                dir = _lastFreeDirection == StrumDirection.Down ? StrumDirection.Up : StrumDirection.Down;
            }
            _lastFreeDirection = dir;

            if (_s.Strum != null) _s.Strum.PreviewDirection = dir;
            Inject(Clean(), t, "On-beat strum (" + (dir == StrumDirection.Down ? "down" : "up") + ")");
            if (_s.Strum != null) _s.Strum.PreviewDirection = StrumDirection.None;
        }

        // ---------- Flow shortcuts ----------

        private void Fit()
        {
            if (_guitar == null || _guitar.Geometry == null) return;
            // Completes a running "fit the fretboard" step too (the app moves on as after "Looks right").
            _s.Calibrator.AcceptForPreview(_guitar.Geometry);
        }

        private void FinishTuning()
        {
            if (_s.Tuner.IsActive) { _s.Tuner.CompleteForPreview(); _lastAction = "All strings in tune"; }
            else _lastAction = "The tuner isn't open";
        }

        private void GoAir()
        {
            if (_app.Flow.State == SessionState.Welcome) _app.ChooseModeForPreview(SessionMode.Air);
            else _app.Fire(SessionTrigger.NoGuitar);
            _lastAction = "Air mode";
        }

        /// <summary>N: whatever "carry on" means right now.</summary>
        private void Next()
        {
            switch (_app.Flow.State)
            {
                case SessionState.Welcome: _app.ChooseModeForPreview(SessionMode.Guided); break;
                case SessionState.Calibrate: Fit(); break;
                case SessionState.Tune: if (_s.Tuner.IsActive) _s.Tuner.Skip(); else _app.Fire(SessionTrigger.Skip); break;
                case SessionState.Paused: _app.Fire(SessionTrigger.Resume); break;
                case SessionState.AirMode: _app.Fire(SessionTrigger.Finish); break;
                case SessionState.WrapUp: _app.Fire(SessionTrigger.Finish); break;
                case SessionState.Done: _app.Fire(SessionTrigger.Restart); break;
                default: _app.Fire(SessionTrigger.Skip); break;
            }
            _lastAction = "Next";
        }

        // ---------- Mouse ----------

        private void MouseLook()
        {
            if (_xrActive || _rig == null || !Input.GetMouseButton(1)) return;
            float yaw = Input.GetAxis("Mouse X") * lookDegreesPerUnit;
            float pitch = -Input.GetAxis("Mouse Y") * lookDegreesPerUnit;

            Vector3 pivot = _cam.transform.position;
            _rig.RotateAround(pivot, Vector3.up, yaw);

            float current = Mathf.Asin(Mathf.Clamp(-_cam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;   // + = looking down
            float next = Mathf.Clamp(current + pitch, -60f, 80f);
            _rig.RotateAround(pivot, _cam.transform.right, next - current);
        }

        /// <summary>
        /// The mouse fingertip lives on a sphere around the camera, so a button nearer or further than that
        /// would never hover (or would press on hover). When the cursor is over a button, move the fingertip
        /// to just in front of it: hover then works and a click (pinch) presses it the normal PokeButton way.
        /// </summary>
        private void SnapFingertipToButtonUnderCursor()
        {
            if (_mouse == null || Input.GetMouseButton(1)) return;
            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            float best = float.MaxValue;
            foreach (PokeButton b in FindObjectsByType<PokeButton>(FindObjectsSortMode.None))
            {
                if (!b.isActiveAndEnabled || !b.Interactable) continue;
                Transform t = b.transform;
                float denom = Vector3.Dot(ray.direction, t.forward);
                if (Mathf.Abs(denom) < 1e-4f) continue;
                float dist = Vector3.Dot(t.position - ray.origin, t.forward) / denom;
                if (dist <= 0f || dist >= best) continue;
                Vector3 local = t.InverseTransformPoint(ray.GetPoint(dist));
                if (Mathf.Abs(local.x) > b.size.x * 0.5f + b.margin || Mathf.Abs(local.y) > b.size.y * 0.5f + b.margin) continue;
                best = dist;
            }
            if (best < float.MaxValue) _mouse.depth = Mathf.Clamp(best - buttonHoverGap, 0.15f, 1.5f);
        }

        private void UpdateCursor()
        {
            IHandInput hands = HandsHub.Input;
            bool show = hands != null && !Input.GetMouseButton(1);
            if (_cursor.gameObject.activeSelf != show) _cursor.gameObject.SetActive(show);
            if (!show) return;
            Handedness mouseHand = Input.GetKey(KeyCode.LeftShift) ? Handedness.Left : Handedness.Right;
            _cursor.position = hands.IndexTip(mouseHand);
        }

        private void RefreshStatus()
        {
            if (_legend == null || !_legend.Visible) return;
            string listening = _s.Audio.Listening ? "listening" : "not listening";
            _legend.SetStatus(_app.Flow.State + " · target " + Target + " · " + listening
                              + (ignoreRealMic ? " · real mic off" : " · real mic on")
                              + (string.IsNullOrEmpty(_lastAction) ? "" : "\nLast: " + _lastAction));
        }
    }
}
#endif

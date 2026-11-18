using System;
using GhostFret.Core.Fretboard;
using GhostFret.Runtime.Hands;
using GhostFret.Runtime.Overlay;
using GhostFret.Runtime.UI;
using GhostFret.Runtime.Util;
using TMPro;
using UnityEngine;

namespace GhostFret.Runtime.Calibration
{
    /// <summary>
    /// "Fit the fretboard" (DESIGN.md §10.2–10.3, §17): three 1-second fingertip touches → FretboardGeometry.
    ///  1. One glowing target at a time with a short instruction; a ring fills over the 1 s hold while the
    ///     fingertip stays still; a soft chime on each touch.
    ///  2. FretboardGeometry.FromCalibration; if it isn't plausible, a kind reason and we start again.
    ///  3. The fitted lines are shown with "Looks right" / "Try again" ("does this line up?").
    ///  4. After that, quick re-fit: hold the index tip on the nut (by the thickest string) for 1 s to
    ///     move the model there, keeping its angle (Refitted).
    /// The touches use the fretting hand's index finger (left, or right in left-handed mode), so a resting
    /// strumming hand can't trigger anything. Touch positions are used live and never stored or logged.
    /// Works when added by code: call Begin(hands). Init(overlay) optionally reuses an existing overlay.
    /// </summary>
    public sealed class FretboardCalibrator : MonoBehaviour
    {
        public enum Phase { Idle, Touching, Confirming, Fitted }

        public float holdSeconds = 1f;
        public float stillnessMeters = 0.007f;
        public float smoothingSeconds = 0.06f;
        public float armTravelMeters = 0.03f;
        public float lostHandHintSeconds = 1.5f;

        public bool refitEnabled = true;
        public float refitHoldSeconds = 1f;
        public float refitRadius = 0.04f;
        public float refitMaxPastNut = 0.012f;
        /// <summary>The re-fit ring only appears after this much of the hold, so normal playing near the nut stays calm.</summary>
        public float refitRingDelay = 0.3f;

        public float targetDiameter = 0.012f;
        public float ringRadius = 0.018f;

        /// <summary>Left-handed mode: the right hand frets, and the 12th fret is predicted to the player's left.</summary>
        public bool LeftHanded;

        public event Action<FretboardGeometry> Fitted;
        public event Action<FretboardGeometry> Refitted;

        /// <summary>The accepted fretboard (after "Looks right"), updated by quick re-fits.</summary>
        public FretboardGeometry Current { get; private set; }
        public Phase State { get; private set; }

        private IHandInput _hands;
        private FretboardOverlay _overlay;
        private bool _ownsOverlay;

        private readonly DwellDetector _dwell = new DwellDetector();
        private readonly DwellDetector _refit = new DwellDetector();
        private bool _refitLeftZone = true;

        private int _step;
        private readonly Vector3[] _touches = new Vector3[3];
        private FretboardGeometry _pending;
        private float _lostFor;
        private string _notice = "";
        private bool _noticeIsHint;

        // Visuals
        private Transform _visualRoot;
        private Renderer _targetCore, _targetHalo;
        private LineRenderer _ringTrack, _ringFill;
        private readonly Renderer[] _touchDots = new Renderer[3];
        private Transform _panel;
        private Renderer _panelBg;
        private TextMeshPro _title, _stepText, _body, _sub;
        private PokeButton _yes, _again;
        private float _panelAlpha;
        private bool _panelVisible;
        private float _ringAlpha;

        private Handedness FretHand { get { return LeftHanded ? Handedness.Right : Handedness.Left; } }
        private string FretHandWord { get { return LeftHanded ? "right" : "left"; } }

        // ---------- Public API ----------

        /// <summary>Optional: reuse an existing overlay for the "does this line up?" preview.</summary>
        public void Init(FretboardOverlay overlay)
        {
            _overlay = overlay;
            _ownsOverlay = false;
        }

        /// <summary>Starts (or restarts) the three touches.</summary>
        public void Begin(IHandInput hands)
        {
            _hands = hands ?? HandsHub.Input;
            EnsureBuilt();
            _notice = "";
            StartTouches();
            UIKit.PlaceInFront(_panel, 0.48f, 0.10f);
        }

        /// <summary>Stops calibrating and hides everything. Keeps Current (and re-fit) if one was accepted.</summary>
        public void Cancel()
        {
            if (_overlay != null && State == Phase.Confirming) _overlay.Hide();
            State = Current != null ? Phase.Fitted : Phase.Idle;
            _pending = null;
            HideCalibrationVisuals();
        }

        // ---------- Flow ----------

        private void StartTouches()
        {
            State = Phase.Touching;
            _step = 0;
            _pending = null;
            _lostFor = 0f;
            _dwell.HoldSeconds = holdSeconds;
            _dwell.StillnessMeters = stillnessMeters;
            _dwell.SmoothingSeconds = smoothingSeconds;
            _dwell.ArmTravelMeters = armTravelMeters;
            _dwell.Reset(true);
            for (int i = 0; i < 3; i++) _touchDots[i].gameObject.SetActive(false);
            if (_overlay != null) _overlay.Hide();
            SetButtons(false);
            _panelVisible = true;
            _panel.gameObject.SetActive(true);
            RefreshTexts();
        }

        private void Update()
        {
            if (_visualRoot == null) return;
            float dt = Time.unscaledDeltaTime;
            IHandInput hands = _hands ?? HandsHub.Input;

            switch (State)
            {
                case Phase.Touching: UpdateTouching(hands, dt); break;
                case Phase.Fitted: UpdateRefit(hands, dt); break;
                default: _ringAlpha = Mathf.MoveTowards(_ringAlpha, 0f, dt / Theme.EaseSeconds); break;
            }

            UpdateTargetLook();
            UpdatePanelFade(dt);
        }

        private void UpdateTouching(IHandInput hands, float dt)
        {
            bool tracked = hands != null && hands.IsTracked(FretHand);
            Vector3 tip = tracked ? hands.IndexTip(FretHand) : Vector3.zero;

            // Kind nudge if the fretting hand is out of view for a while.
            bool wasLost = _lostFor > lostHandHintSeconds;
            _lostFor = tracked ? 0f : _lostFor + dt;
            if ((_lostFor > lostHandHintSeconds) != wasLost) RefreshTexts();

            bool done = _dwell.Update(tip.ToNumerics(), tracked, dt);

            // Target: the first touch has no known spot, so the hint follows the fingertip;
            // later touches are predicted from the earlier ones.
            Vector3 target;
            bool showTarget;
            if (_step == 0) { target = tip; showTarget = false; }
            else if (_step == 1) { target = CalibrationGuide.PredictNutHighE(_touches[0].ToNumerics(), Vector3.down.ToNumerics()).ToUnity(); showTarget = true; }
            else { target = CalibrationGuide.PredictTwelfth(_touches[0].ToNumerics(), _touches[1].ToNumerics(), PlayerRight().ToNumerics(), LeftHanded).ToUnity(); showTarget = true; }

            _targetCore.gameObject.SetActive(showTarget);
            _targetHalo.gameObject.SetActive(showTarget);
            if (showTarget)
            {
                _targetCore.transform.position = target;
                _targetHalo.transform.position = target;
            }

            // Ring: a faint track around the fingertip, filling while it holds still.
            float progress = _dwell.Progress;
            Vector3 ringAt = progress > 0f ? _dwell.Anchor.ToUnity() : tip;
            _ringAlpha = Mathf.MoveTowards(_ringAlpha, tracked ? 1f : 0f, dt / Theme.EaseSeconds);
            OverlayDraw.SetRing(_ringTrack, ringAt, ringRadius, 1f);
            OverlayDraw.SetRing(_ringFill, ringAt, ringRadius, progress);

            if (done) AcceptTouch(_dwell.Result.ToUnity());
        }

        private void AcceptTouch(Vector3 point)
        {
            string tip;
            if (!CalibrationGuide.CheckTouch(_step, point.ToNumerics(),
                    _touches[0].ToNumerics(), _touches[1].ToNumerics(), out tip))
            {
                SetNotice(tip, true);
                UiSounds.Play(UiSounds.Hmm, 0.6f);
                return;
            }

            _touches[_step] = point;
            _touchDots[_step].transform.position = point;
            _touchDots[_step].gameObject.SetActive(true);
            UiSounds.PlayAt(UiSounds.Chime, point, 0.8f);
            _notice = "";
            _step++;

            if (_step < CalibrationGuide.StepCount) { RefreshTexts(); return; }
            Fit();
        }

        private void Fit()
        {
            FretboardGeometry g = FretboardGeometry.FromCalibration(
                _touches[0].ToNumerics(), _touches[1].ToNumerics(), _touches[2].ToNumerics());

            string reason;
            if (!g.IsPlausible(out reason))
            {
                StartTouches();
                SetNotice(reason, true);
                UiSounds.Play(UiSounds.Hmm, 0.6f);
                return;
            }

            _pending = g;
            State = Phase.Confirming;
            for (int i = 0; i < 3; i++) _touchDots[i].gameObject.SetActive(false);
            EnsureOverlay();
            _overlay.Show(g);
            UIKit.PlaceBesideGuitar(_panel, g);
            SetButtons(true);
            RefreshTexts();
        }

        private void OnLooksRight()
        {
            if (State != Phase.Confirming || _pending == null) return;
            Current = _pending;
            _pending = null;
            State = Phase.Fitted;
            if (_ownsOverlay && _overlay != null) _overlay.Hide();   // the app shows its own overlay on Fitted
            HideCalibrationVisuals();
            _refitLeftZone = false;   // the finger may still be near the nut from touching it
            Coach.Say("Your fretboard is fitted.", CoachTone.Good);
            if (Fitted != null) Fitted(Current);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor preview only (Runtime/EditorPreview): accept a fretboard as if the three spots were
        /// touched and "Looks right" pressed. Raises Fitted like the real flow.
        /// </summary>
        public void AcceptForPreview(FretboardGeometry g)
        {
            if (g == null) return;
            EnsureBuilt();
            _pending = g;
            State = Phase.Confirming;
            OnLooksRight();
        }
#endif

        private void OnTryAgain()
        {
            if (State != Phase.Confirming) return;
            StartTouches();
            SetNotice("No problem. Let's fit it again.", false);
            UIKit.PlaceInFront(_panel, 0.48f, 0.10f);
        }

        // ---------- Quick re-fit ----------

        private void UpdateRefit(IHandInput hands, float dt)
        {
            float ringTarget = 0f;
            if (refitEnabled && Current != null && hands != null && hands.IsTracked(FretHand))
            {
                Vector3 tip = hands.IndexTip(FretHand);
                bool inZone = Current.IsInRefitZone(tip.ToNumerics(), refitRadius, refitMaxPastNut);
                if (!inZone)
                {
                    _refitLeftZone = true;
                    _refit.Reset(false);
                }
                else if (_refitLeftZone)
                {
                    _refit.HoldSeconds = refitHoldSeconds;
                    _refit.StillnessMeters = stillnessMeters;
                    _refit.SmoothingSeconds = smoothingSeconds;
                    _refit.ArmTravelMeters = 0f;
                    if (_refit.Update(tip.ToNumerics(), true, dt))
                    {
                        _refitLeftZone = false;
                        Vector3 offset = _refit.Result.ToUnity() - Current.NutLowE.ToUnity();
                        Current = Current.MovedBy(offset.ToNumerics());
                        Coach.Say("Re-fitted to your guitar.", CoachTone.Good);
                        if (Refitted != null) Refitted(Current);
                    }
                    else
                    {
                        float shown = Mathf.InverseLerp(refitRingDelay / Mathf.Max(0.01f, refitHoldSeconds), 1f, _refit.Progress);
                        if (shown > 0f)
                        {
                            ringTarget = 1f;
                            Vector3 at = _refit.Anchor.ToUnity();
                            OverlayDraw.SetRing(_ringTrack, at, ringRadius, 1f);
                            OverlayDraw.SetRing(_ringFill, at, ringRadius, _refit.Progress);
                        }
                    }
                }
            }
            else _refit.Reset(false);

            _ringAlpha = Mathf.MoveTowards(_ringAlpha, ringTarget, dt / Theme.EaseSeconds);
            _targetCore.gameObject.SetActive(false);
            _targetHalo.gameObject.SetActive(false);
        }

        // ---------- Text ----------

        private void SetNotice(string text, bool isHint)
        {
            _notice = text ?? "";
            _noticeIsHint = isHint;
            RefreshTexts();
        }

        private void RefreshTexts()
        {
            if (_title == null) return;
            string sub;
            if (State == Phase.Confirming)
            {
                _title.text = "Does this line up?";
                _stepText.text = "";
                _body.text = "The lines should sit on your real frets and strings.";
                sub = "";
            }
            else
            {
                _title.text = "Fit your fretboard";
                _stepText.text = "Step " + (_step + 1) + " of 3";
                if (_step == 0)
                {
                    _body.text = "Touch the nut, next to the thickest string.";
                    sub = "Use your " + FretHandWord + " index finger and hold still for a second.";
                }
                else if (_step == 1)
                {
                    _body.text = "Now the nut, next to the thinnest string.";
                    sub = "Hold still for a second.";
                }
                else
                {
                    _body.text = "Now the 12th fret, on the thickest string.";
                    sub = "It's usually the fret with two dots.";
                }
                if (_lostFor > lostHandHintSeconds)
                    sub = "I can't see your " + FretHandWord + " hand. Bring it into view.";
            }

            bool hint = _notice.Length > 0 && _noticeIsHint;
            _sub.text = _notice.Length > 0 ? (hint ? Theme.HintMark + " " : "") + _notice : sub;
            _sub.color = hint ? Theme.Hint : Theme.TextDim;
            if (_notice.Length == 0 && State == Phase.Touching && _step == 0 && _lostFor <= lostHandHintSeconds)
                _sub.text = sub + " Rest the guitar on your leg.";
        }

        // ---------- Visuals ----------

        private void EnsureBuilt()
        {
            if (_visualRoot != null) return;
            _visualRoot = new GameObject("Calibration Visuals").transform;
            _visualRoot.SetParent(transform, false);

            _targetHalo = OverlayDraw.Dot(_visualRoot, "Target Halo", targetDiameter * 2.6f, Theme.WithAlpha(Theme.Ghost, 0.2f));
            _targetCore = OverlayDraw.Dot(_visualRoot, "Target", targetDiameter, Theme.Ghost);
            _targetHalo.gameObject.SetActive(false);
            _targetCore.gameObject.SetActive(false);

            _ringTrack = OverlayDraw.Line(_visualRoot, "Ring Track", 0.0015f, Theme.WithAlpha(Color.white, 0.25f));
            _ringFill = OverlayDraw.Line(_visualRoot, "Ring Fill", 0.0025f, Theme.Ghost);
            _ringTrack.positionCount = 0;
            _ringFill.positionCount = 0;

            for (int i = 0; i < 3; i++)
            {
                _touchDots[i] = OverlayDraw.Dot(_visualRoot, "Touch " + (i + 1), 0.007f, Theme.WithAlpha(Theme.Good, 0.9f));
                _touchDots[i].gameObject.SetActive(false);
            }

            _panel = UIKit.CreatePanel("Fit Fretboard Panel", new Vector2(0.36f, 0.17f), _visualRoot);
            _panelBg = _panel.GetComponentInChildren<Renderer>();
            _title = UIKit.CreateText(_panel, "", Theme.TitleHeight * 0.85f, new Vector3(0, 0.056f, -0.001f), 0.33f);
            _stepText = UIKit.CreateText(_panel, "", Theme.SmallHeight * 0.85f, new Vector3(0, 0.030f, -0.001f), 0.33f);
            _stepText.color = Theme.TextDim;
            _body = UIKit.CreateText(_panel, "", Theme.BodyHeight, new Vector3(0, 0.004f, -0.001f), 0.33f);
            _sub = UIKit.CreateText(_panel, "", Theme.SmallHeight, new Vector3(0, -0.030f, -0.001f), 0.33f);
            _sub.color = Theme.TextDim;
            _yes = UIKit.CreateButton(_panel, "Looks right", new Vector3(-0.085f, -0.060f, -0.002f), new Vector2(0.15f, 0.036f), OnLooksRight);
            _again = UIKit.CreateButton(_panel, "Try again", new Vector3(0.085f, -0.060f, -0.002f), new Vector2(0.15f, 0.036f), OnTryAgain);
            SetButtons(false);
            _panel.gameObject.SetActive(false);
        }

        private void EnsureOverlay()
        {
            if (_overlay != null) return;
            var go = new GameObject("Calibration Preview Overlay");
            go.transform.SetParent(transform, false);
            _overlay = go.AddComponent<FretboardOverlay>();
            _ownsOverlay = true;
        }

        private void SetButtons(bool on)
        {
            if (_yes != null) _yes.gameObject.SetActive(on);
            if (_again != null) _again.gameObject.SetActive(on);
        }

        private void HideCalibrationVisuals()
        {
            if (_visualRoot == null) return;
            _panelVisible = false;
            SetButtons(false);
            _targetCore.gameObject.SetActive(false);
            _targetHalo.gameObject.SetActive(false);
            for (int i = 0; i < 3; i++) _touchDots[i].gameObject.SetActive(false);
        }

        private void UpdateTargetLook()
        {
            float t = Time.unscaledTime;
            // Gentle breathing glow on the target; slow enough to never read as flashing.
            float breathe = 1f + 0.12f * Mathf.Sin(2f * Mathf.PI * t / 1.6f);
            _targetHalo.transform.localScale = Vector3.one * targetDiameter * 2.6f * breathe;
            _targetHalo.material.color = Theme.WithAlpha(Theme.Ghost, 0.14f + 0.08f * breathe);

            float a = Theme.Ease(_ringAlpha) * Mathf.Clamp(Theme.OverlayBrightness, 0.3f, 1.6f);
            _ringTrack.enabled = a > 0.001f;
            _ringFill.enabled = a > 0.001f;
            OverlayDraw.SetLineColour(_ringTrack, Theme.WithAlpha(Color.white, 0.25f * a));
            OverlayDraw.SetLineColour(_ringFill, Theme.WithAlpha(Theme.Ghost, Mathf.Clamp01(a)));
        }

        private void UpdatePanelFade(float dt)
        {
            _panelAlpha = Mathf.MoveTowards(_panelAlpha, _panelVisible ? 1f : 0f, dt / Theme.EaseSeconds);
            float a = Theme.Ease(_panelAlpha);
            if (_panelBg != null) _panelBg.material.color = Theme.WithAlpha(Theme.PanelBg, Theme.PanelBg.a * a);
            _title.alpha = a;
            _stepText.alpha = a;
            _body.alpha = a;
            _sub.alpha = a;
            _panel.localScale = Vector3.one * (0.92f + 0.08f * a);
            if (!_panelVisible && _panelAlpha <= 0f && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
        }

        private static Vector3 PlayerRight()
        {
            Transform head = UIKit.Head;
            Vector3 r = head != null ? head.right : Vector3.right;
            r.y = 0f;
            return r.sqrMagnitude > 1e-6f ? r.normalized : Vector3.right;
        }
    }
}

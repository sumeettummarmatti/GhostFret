# PROGRESS.md — GhostFret

> Live status. Claude Code updates this at the end of every task: tick boxes, add a session log line, note issues. Keep it short and honest.

**Current phase:** 0 — Setup
**Overall:** Phases 1–7 code-complete (246/246 Core tests). Not yet compiled in Unity or run on Quest — next: Unity setup, first compile, device test.

## Workstreams (session 3)
| Agent | Area | Owns | Status |
|---|---|---|---|
| A | Audio | MicrophoneInput, TunerPanel, AudioCoach, StrumListener, TunerSmoother, confidence-gated ChordFeedback | ✅ written; 79 audio tests, 245/245 suite; Runtime stub-compiled |
| B | Hands + fretboard | IHandInput (+FingerTip/FingerCurl), MetaHandInput, Calibrator, FretboardOverlay, GhostHand, HandMath, DwellDetector, CalibrationGuide | ✅ written; 245/245 tests; Runtime stub-compiled (real SDK names unverified) |
| C | App flow | GhostFretApp bootstrap, SessionFlow (steps + modes), Lesson/StrumCoach/Drill/Song/Air runners, Welcome/Pause/WrapUp/Settings panels, PauseGesture, ProgressStore | ✅ written; 246/246 tests; App files stub-compiled |
| D | Songs + strum | ContentLoader, Metronome, StrumTracker, StrumLane, SongPlayer, LatencyCalibrator, SongRun scoring | ✅ written; 245/245 tests at hand-back; Runtime not yet compiled in Unity |
| E | Air Guitar | VirtualGuitar, AirChordMatcher, AirStrumDetector, PluckSynth, AirGuitar, AirGuitarVisual, RunDay1 | ✅ written; 32 Air tests pass; Runtime not yet compiled in Unity |
| Lead | Shared UI kit | Runtime/UI (Theme, UIKit, PokeButton, Coach, UiSounds), HandsHub | ✅ done |
| F | Editor preview | Runtime/EditorPreview (stand-in guitar auto-fitted, dim room, keyboard feedback: C/M/B/W/Q/Space/T/F/A/N/L/H) — editor-only | ✅ written; stub-compiled; device path unchanged |
| User | Unity setup | Unity Hub installed (brew); user must run Unity CLI installer + sign in, then install Unity 6 LTS + Android | 🟨 |

## Blockers
- [ ] Meta VR Start membership confirmed
- [ ] Unity 6 LTS + Android Build Support installed
- [ ] Quest 3 in Developer Mode

## Phases
Golden rule: a phase is ✅ only when it works on Quest 3 with controllers off. 🟨 = code written + Core tested.
- [ ] 0 Setup ⬜ (user: Unity project, SDK, device)
- [ ] 1 Tuner 🟨
- [ ] 2 Fit the fretboard 🟨
- [ ] 3 Chord Coach 🟨
- [ ] 4 Strum Coach 🟨 (mic latency calibration written, not wired into the flow)
- [ ] 5 Song Mode 🟨
- [ ] 6 Session flow + progress 🟨
- [ ] 7 Air Guitar mode 🟨
- [ ] 8 Polish + accessibility 🟨 partly (UX colours/motion/captions, left-handed, brightness in code; needs device pass)
- [ ] 9 Submission

## What exists already
| Area | State |
|---|---|
| Core: NoteMath, GuitarTuning, ChordShape, ChordLibrary (9 chords) | Written |
| Core: FretboardGeometry (calibration maths) | Written |
| Core: FFT, YIN pitch, chroma, ChordMatcher, StringChecker, OnsetDetector, ChordAnalyzer, StrumListener, TunerSmoother | Written + 79 tests (synthetic audio) |
| Core: ChordFeedback, BeatGrid, StrumDirectionClassifier | Written |
| Core: AdaptiveTempo, ChangeDrill, SessionFlow, ChordLesson, StrumPractice, DayPlanner, PauseDetector | Written + tests |
| Core: AirMode (VirtualGuitar, AirChordMatcher, AirStrumDetector, PluckSynth) | Written + 32 tests |
| Core: Hands/HandMath, DwellDetector, CalibrationGuide, SongRun, SongClock, LatencyCalibration | Written + tests |
| Core: SongChart/Timeline/Validator, Curriculum, PracticeProgress | Written |
| Content: curriculum.json + 7 song charts | Written (public domain / original); validator stricter, all pass |
| Runtime (Unity) | All modules written; never compiled in real Unity. Cross-module API check against stubs: 0 mismatches |
| core-tests | 246 tests, all passing |
| Unity project | Only `Assets/`; no `ProjectSettings/` or `Packages/` yet (Phase 0, by hand) |

## Decisions
| Decision | Why |
|---|---|
| Unity + Meta XR SDK, not WebXR | Passthrough Camera API is native-only; better audio; prior Quest guitar prototypes use Unity |
| Audio decides correctness; hand tracking for strum direction, calibration, UI | Fingers on the neck are hidden from the cameras |
| Core logic is pure C# with no UnityEngine | Testable with `dotnet test`, no headset needed |
| Track: Productivity | Daily practice habit + creative tool |
| Public-domain/original songs only | Copyright + Meta content policy |
| Air Guitar mode is mandatory | Judges may not have a guitar; hands-first rule |
| Pause = palm up, still, no pinch | Palm-toward-headset pinch is the reserved system gesture |
| UI built in code (UIKit), own PokeButton on IHandInput | No prefabs to wire by hand; same buttons work in Editor (mouse) and on Quest |
| All app sounds synthesised in code (UiSounds, PluckSynth) | No audio assets or licensing; Air mode chord sound without samples |
| GhostFretApp bootstraps every component | Unity scene = camera rig + passthrough + one GameObject |

## Tuning values
| Setting | Value | Notes |
|---|---|---|
| Chord FFT size | 8192 @ 48 kHz | ~170 ms window after onset |
| In-tune window | ±5 cents | |
| On-time window | ±90 ms | Beginner friendly |
| Weak string ratio | 15% of median | StringChecker.WeakRatio |
| Clean / string-tip gate | target score ≥0.8, unexplained ≤15% | ChordFeedbackEvaluator |
| "Sounds like X" gate | score ≥0.85, margin 0.08, unexplained ≥8% | ChordFeedbackEvaluator |
| Onset min flux | 6 (threshold ×2.5 median, 0.1 s min gap) | StrumListener |
| One result per strum | 0.35 s | StrumListener.MinSecondsBetweenChordResults |
| Tuner stability | median 5, 3 agreeing readings, in-tune hold 0.5 s | TunerSmoother |
| Strum min speed | 0.25 m/s | StrumDirectionClassifier |
| Song start tempo | 75% | AdaptiveTempo |
| Calibration hold | 1 s per touch, stillness 7 mm, arm after 3 cm travel | FretboardCalibrator |
| Quick re-fit | 1 s hold, ≤12 mm past nut, radius 4 cm | FretboardCalibrator |
| Ghost dot size | 11 mm, breathe 2.4 s | GhostHand |
| Overlay lift | 3 mm | FretboardOverlay |
| Poke press depth | 4 mm (hover 4 cm, cooldown 0.6 s) | PokeButton |
| UI sound volume | 0.35 | UiSounds.Volume |
| Song ghost-hand lead | 1 beat (morph ≤0.35 s) | SongPlayer.morphLeadBeats |
| Metronome schedule-ahead | 0.25 s | Metronome |
| Strum lane look-ahead | 3 beats | StrumLane |
| Latency calibration | 4 clicks @80 BPM, ≥3 pairs, spread ≤80 ms, clamp 0–300 ms | LatencyCalibration |
| Strum hand sample max age | 0.15 s | StrumDirectionClassifier |
| Air strum min speed | 0.30 m/s | AirStrumDetector.MinSpeed |
| Air guitar placement | 0.36 m forward, 0.50 m below head | VirtualGuitar |
| Air hint interval | 6 s (after 2 s off-shape) | AirGuitar |
| Silence pause | 10 s playing, 30 s in lesson | GhostFretApp |
| Palm-up pause | hold 1 s, dot ≥0.8, still <0.12 m/s, no pinch | PauseGesture |
| Clean strums to learn a chord | 3 new / 1 review | ChordLesson |
| Strum Coach tempo | 70 BPM (60 on day 1), 4 bars | StrumCoachRunner |
| Change drill | 60 s | DrillRunner |
| New-button arm delay | 0.5 s | AppPanel.ArmDelaySeconds |
| Break tip | after 15 min | BreakReminder |

## Known issues
- Core audio thresholds tuned on synthetic audio only; real Quest mic may give more NotSure (safe direction). Tune on device via AudioCoach.Listener.
- A muted string whose note is doubled elsewhere (e.g. D in Em) can't be heard as missing.
- TMP default font may lack ✓ ○ glyphs: add a symbol fallback font (Unity step). Lane arrows are geometry, not glyphs.
- Air mode: placement (BelowHead 0.50 m, Forward 0.36 m) and strum MinSpeed 0.30 m/s need a device check; Day 1 has no pause hook yet.
- Metronome clicks could trigger mic onsets; AudioCoach may need to ignore onsets near scheduled clicks.
- Meta SDK names (OVRHand.GetHand, BoneId.XRHand_*, IsSystemGestureInProgress) written from memory; verify on first Unity compile.
- OVRSkeleton must use XR Hand Left/Right skeleton type.
- Mic latency calibration not wired into the flow (AudioCoach.inputLatencySeconds = 0).
- Strum Coach resumes from its count-in after pause (song + drill resume exactly).
- Panel layout/spacing untested in a headset.
- Editor preview stand-in guitar is right-handed only; B (buzz) maps to a muted-string result (Core has no buzz kind).
- Browser mock-up of the look: https://claude.ai/artifact/HuiXZLbtpKXef8gLas6rtn (not the Unity build).
- Backing tracks not created yet (SongPlayer falls back to metronome).

## Session log
| Session | What changed | Next |
|---|---|---|
| 1 | Skeleton created | Phase 0 |
| 2 | Read all docs; core tests run (20/20 pass, test runner fixed for .NET 9); action plan written; Many Hands idea dropped, GhostFret is the entry | Phase 0 Unity setup (user) + parallel Runtime work (agents) |
| 3 | Shared UI kit + asmdef refs (TMP, Oculus.VR); 5 parallel agents wrote all Runtime modules + Core logic; 246/246 tests; cross-module API check clean; hard-rule audit clean; TMP wrap API updated | User: Unity setup + first compile; send console errors |
| 4 | Browser preview artifact; Editor-only preview helper (auto-fit stand-in guitar + keyboard feedback); Unity Hub installed | User: Unity CLI install + sign-in, open project, Play |

# PROGRESS.md — GhostFret

> Live status. Claude Code updates this at the end of every task: tick boxes, add a session log line, note issues. Keep it short and honest.

**Current phase:** 0 — Setup
**Overall:** Skeleton ready (Core logic + docs + content + Runtime stubs)

## Blockers
- [ ] **Confirm the real deadline.** Docs say Nov 18, 2026, 12:00 PM PST, which is today (2026-11-18).
- [ ] Meta VR Start membership confirmed
- [ ] Unity 6 LTS + Android Build Support installed
- [ ] Quest 3 in Developer Mode

## Phases
- [ ] 0 Setup
- [ ] 1 Tuner
- [ ] 2 Fit the fretboard
- [ ] 3 Chord Coach
- [ ] 4 Strum Coach
- [ ] 5 Song Mode
- [ ] 6 Session flow + progress
- [ ] 7 Air Guitar mode
- [ ] 8 Polish + accessibility
- [ ] 9 Submission

## What exists already
| Area | State |
|---|---|
| Core: NoteMath, GuitarTuning, ChordShape, ChordLibrary (9 chords) | Written |
| Core: FretboardGeometry (calibration maths) | Written |
| Core: FFT, YIN pitch, chroma, ChordMatcher, StringChecker, OnsetDetector, ChordAnalyzer | Written, needs more tests |
| Core: ChordFeedback, BeatGrid, StrumDirectionClassifier | Written |
| Core: AdaptiveTempo, ChangeDrill, SessionFlow | Written |
| Core: SongChart/Timeline/Validator, Curriculum, PracticeProgress | Written |
| Content: curriculum.json + 6 song charts | Written (public domain / original) |
| Runtime (Unity) | Stubs with TODOs |
| core-tests | Harness + 20 tests, all passing (`RollForward=Major` so .NET 9 can run them) |
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

## Tuning values
| Setting | Value | Notes |
|---|---|---|
| Chord FFT size | 8192 @ 48 kHz | ~170 ms window after onset |
| In-tune window | ±5 cents | |
| On-time window | ±90 ms | Beginner friendly |
| Weak string ratio | 15% of median | StringChecker.WeakRatio |
| Strum min speed | 0.25 m/s | StrumDirectionClassifier |
| Song start tempo | 75% | AdaptiveTempo |
| Calibration hold | 1 s per touch | |

## Known issues
- Core audio thresholds are untested on real guitar audio; tune on device in Phase 1–3.
- Backing tracks not created yet (SongPlayer falls back to metronome).

## Session log
| Session | What changed | Next |
|---|---|---|
| 1 | Skeleton created | Phase 0 |
| 2 | Read all docs; core tests run (20/20 pass, test runner fixed for .NET 9); action plan written; Many Hands idea dropped, GhostFret is the entry | Confirm deadline; Phase 0 Unity setup (user) + parallel Runtime work (agents) |

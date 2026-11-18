# PLAN.md — GhostFret build plan

Work top to bottom. Each phase has tasks, a **Done when** check and a Claude Code prompt. Tick boxes in `PROGRESS.md`, not here. Requirements live in `REQUIREMENTS.md`.

**Golden rule:** a phase isn't done until it works **on the Quest 3 with controllers off**.

| Phase | Goal | Done when |
|---|---|---|
| 0 | Setup | Passthrough "hello" app runs on Quest 3; core tests pass |
| 1 | Tuner | Pluck a string → tuner shows note and cents on the headset |
| 2 | Fit the fretboard | Three touches → glowing frets line up with the real neck |
| 3 | Chord Coach | Ghost dots for Em; strum → "Clean Em!" or the muted string pulses |
| 4 | Strum Coach | Arrows scroll in time; strums judged early/on time/late + direction |
| 5 | Song Mode | Play "Drunken Sailor" end to end with chord changes and tempo |
| 6 | Session flow + progress | Welcome → fit → tune → lesson → drill → song → wrap-up; streak saved |
| 7 | Air Guitar mode | Full session completable with no guitar, hands only |
| 8 | Polish + accessibility | UX_GUIDE.md applied; left-handed mode; captions |
| 9 | Submission | APK in "Competition" channel, Invite URL, video, form |

**Cut list if behind (in order):** change-drill high scores → reggae pattern → extra songs (keep 3) → hand-follow drift correction → overlay fade. **Never cut:** hands-only completion, Air mode, tuner, chord check, one full song.

---

## Phase 0 — Setup
- [ ] Follow `docs/SETUP_UNITY.md`: Unity 6 LTS + Android, open `unity/`, import Meta XR All-in-One SDK, run Project Setup Tool
- [ ] Enable passthrough, hand tracking, microphone permission
- [ ] Build an empty passthrough scene to the Quest 3
- [ ] `cd core-tests && dotnet test` passes
- [ ] Git repo + first commit (proves New Experience timing)

**Prompt:** "Read CLAUDE.md and PLAN.md Phase 0. Check that Core compiles and core-tests pass. List any setup steps I must do by hand in the Unity Editor, in order."

## Phase 1 — Tuner
- [ ] `MicrophoneInput`: ring buffer from Unity `Microphone`
- [ ] `TunerPanel`: `YinPitchDetector` → nearest string + cents; smoothing
- [ ] Panel floats in view (headstock position after Phase 2)

**Prompt:** "Implement MicrophoneInput and TunerPanel per DESIGN.md §7.1 and §11.2 using Core's YinPitchDetector and GuitarTuning. Add Core tests for any new logic. Update PROGRESS.md."

## Phase 2 — Fit the fretboard
- [ ] `IHandInput` + `MetaHandInput` (index tip, wrist, palm normal, pinch)
- [ ] `FretboardCalibrator`: 3 dwell-touches → `FretboardGeometry`; plausibility check + redo
- [ ] `FretboardOverlay`: fret and string lines; quick re-fit (touch nut 1 s)

**Prompt:** "Implement Phase 2 per DESIGN.md §10.2–10.3. Calibration must feel friendly: one glowing target at a time, a ring that fills during the 1-second hold, a soft chime on success."

## Phase 3 — Chord Coach
- [ ] `GhostHand`: numbered fingertip dots from `ChordShape` + `FretboardGeometry.FingerPosition`
- [ ] `AudioCoach`: onset → grab ~170 ms → `ChordAnalyzer.Analyze` → `ChordFeedback`
- [ ] String highlight (green clean / amber problem) + caption text
- [ ] Add Core tests with synthetic chords (clean Em, Em with muted G)

**Prompt:** "Implement Phase 3 per DESIGN.md §7.2 and §11.3. Feedback must stay silent or general when confidence is low (never a wrong claim)."

## Phase 4 — Strum Coach
- [ ] `StrumLane`: arrows from strum pattern + `BeatGrid`; metronome click
- [ ] Wrist projection → `StrumDirectionClassifier`; onsets → `BeatGrid.Judge`
- [ ] Mic latency calibration ("strum on the four clicks")

## Phase 5 — Song Mode
- [ ] `ContentLoader` reads songs + curriculum from StreamingAssets (Android: UnityWebRequest)
- [ ] `SongPlayer`: `SongTimeline`, dspTime beat clock, ghost hand morphs one beat early, `AdaptiveTempo`
- [ ] Section loop; end-of-song summary (kind, not harsh)

## Phase 6 — Session flow + progress
- [ ] `GhostFretApp` drives `SessionFlow`
- [ ] Pause: palm up + still (no pinch) or 10 s silence; resume restores state
- [ ] `ProgressStore` saves `PracticeProgress` to `persistentDataPath`; streak; curriculum day

## Phase 7 — Air Guitar mode
- [ ] Virtual guitar at lap height; chord-shape matching from finger joints (approximate)
- [ ] Strum via wrist crossing virtual strings; sampled chord audio
- [ ] Complete Day 1 + one song with no guitar

## Phase 8 — Polish + accessibility
- [ ] Apply `docs/UX_GUIDE.md` (colours, motion, sound, copy)
- [ ] Left-handed mode (mirror), captions on, colour + shape cues, overlay brightness
- [ ] Cold-start speed; no dropped frames during play

## Phase 9 — Submission
- [ ] Work through `REQUIREMENTS.md` §7 checklist
- [ ] Build release APK → Meta VR Developer Dashboard → release channel **"Competition"** → Invite URL
- [ ] Record video (script in SUBMISSION.md), < 3 min, public
- [ ] Fill the form from SUBMISSION.md

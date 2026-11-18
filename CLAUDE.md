# CLAUDE.md — rules for Claude Code

## Project
GhostFret: a mixed-reality guitar coach for Meta Quest 3 (Unity + Meta XR SDK). Ghost hand on the real fretboard, a listening chord coach, a strum coach, songs, and an Air Guitar mode.
Meta VR Start Developer Competition 2026 · New Experience · Productivity track.

Read first: `REQUIREMENTS.md` (rules we must pass), `DESIGN.md` (what to build), `PLAN.md` (current phase), `PROGRESS.md` (status), `docs/UX_GUIDE.md` (look and feel).

## Architecture boundaries
- `unity/Assets/GhostFret/Scripts/Core/` is **pure C#**: no `UnityEngine`, no Meta SDK. All logic that can be tested lives here. Use `System.Numerics.Vector3`, not `UnityEngine.Vector3`.
- `Scripts/Runtime/` is Unity glue: MonoBehaviours that read sensors, call Core, and draw. Keep logic thin here.
- Meta SDK code goes behind `#if GHOSTFRET_META_SDK` or the `IHandInput` interface, so the project compiles before the SDK is imported.
- C# language level: **C# 9** (Unity). No file-scoped namespaces, no `record`, no global usings. Use `Math.Log(x, 2)` instead of `Math.Log2`.

## Hard rules (from the hackathon and policies)
1. **Hands only.** Never add an action that needs a controller. Every screen must be usable with poke/pinch.
2. **Air Guitar mode must always complete a full session** without a guitar.
3. **Never use the system gesture** (palm toward headset + pinch). Pause = palm up, still, no pinch.
4. **Audio decides chord correctness.** Hand tracking only for calibration, strum direction, UI and optional hints.
5. **When unsure, stay quiet or give a general tip.** Never tell the player something wrong.
6. **Privacy:** mic, hand and camera data are processed live and never stored, logged or sent. Progress saved on device only.
7. **No health claims** in any text. "Take a break" is a practice tip, nothing more.
8. **Content:** public-domain or original songs only. No lyrics, recordings or copied tabs. `SongValidator` must pass.
9. **Seated, within reach:** all UI on or beside the guitar; no locomotion; no flashing.

## Code style
- Simple and readable over clever. Small classes, clear names, short comments explaining *why*.
- Tunable numbers live as public fields with defaults (and are listed in PROGRESS.md "Tuning values").
- Player-facing text: short, kind, specific, one tip at a time (see `docs/UX_GUIDE.md`).

## Workflow
1. `/next-task` picks the next unchecked item in the current phase of PLAN.md.
2. Put testable logic in Core and add a test in `core-tests/`. Run `cd core-tests && dotnet test`.
3. Unity Editor steps (scenes, prefabs, project settings) can't be done by you: write them as a numbered list for the user.
4. After each task: update `PROGRESS.md` (and `REQUIREMENTS.md` status if a rule is affected).
5. If a phase won't fit the time, say so and suggest the next item from the PLAN.md cut list.

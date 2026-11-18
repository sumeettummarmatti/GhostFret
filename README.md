# GhostFret

> Learn guitar on your **real guitar** in mixed reality. A ghost hand shows where your fingers go, a coach **listens** to every chord, and a strum lane keeps you in time.

| | |
|---|---|
| **Device** | Meta Quest 3 / 3S (passthrough, hand tracking, microphone) |
| **Engine** | Unity 6000.3.28f1 (6.3 LTS) + Meta XR Core SDK 209 + URP |
| **Competition** | Meta VR Start Developer Competition 2026 · **New Experience** division |
| **Track** | Productivity (recommended; see DESIGN.md §13.1) |
| **Deadline** | Nov 18, 2026, 12:00 PM PST |

## Docs (read in this order)

| File | What's in it |
|---|---|
| `DESIGN.md` | The full design: story, problem, features, architecture, MR + audio details, data, risks |
| `REQUIREMENTS.md` | Every hackathon rule mapped to how GhostFret meets it (the checklist we must pass) |
| `PLAN.md` | Build plan by phase, with "done when" checks and Claude Code prompts |
| `PROGRESS.md` | Live status: tick boxes, decisions, known issues |
| `SUBMISSION.md` | Devpost form drafts: tagline, description, hand-interaction text, video script |
| `CLAUDE.md` | Rules and workflow for Claude Code |
| `docs/SETUP_UNITY.md` | Unity + Meta SDK + Quest 3 setup, step by step |
| `docs/UX_GUIDE.md` | Look, sound, motion and tone so it feels great |
| `docs/TESTER_SETUP.md` | Baby-step setup + test guide: Quest 3, computer, Unity, Editor test, Quest build |

## Repo layout

```
ghostfret/
  README.md  DESIGN.md  REQUIREMENTS.md  PLAN.md  PROGRESS.md  SUBMISSION.md  CLAUDE.md
  .claude/commands/          # Claude Code slash commands (/next-task, /check-requirements, ...)
  docs/                      # setup + UX guide
  core-tests/                # plain .NET tests for Core logic (run without Unity)
  unity/                     # Unity project folder (Assets only; see docs/SETUP_UNITY.md)
    Assets/
      GhostFret/
        Scripts/
          Core/              # pure C#, no UnityEngine: music, fretboard, audio, lessons, content
          Runtime/           # Unity MonoBehaviours (stubs with TODOs)
      StreamingAssets/GhostFret/
        songs/*.json         # public-domain song charts
        curriculum.json      # day-by-day beginner path
```

## Quick start

1. **Unity:** follow `docs/SETUP_UNITY.md` (install, open `unity/`, import Meta XR SDK, build to Quest 3).
2. **Core tests (no Unity needed):**
   ```bash
   cd core-tests
   dotnet test
   ```
3. **Claude Code:** open the repo root, then run `/next-task`.

## How it works (short)

1. **Fit the fretboard:** touch the nut (both edges) and the 12th fret; GhostFret builds a 3D fretboard on your real guitar.
2. **Tune:** pluck each string; a tuner at the headstock guides you.
3. **Chord Coach:** a ghost hand shows the shape; the mic checks the sound and names any muted string.
4. **Strum Coach:** down/up arrows scroll beside the guitar; timing comes from the mic, direction from hand tracking.
5. **Song Mode:** public-domain songs with chords, strum lane and adaptive tempo.
6. **Air Guitar mode:** no guitar needed; lets anyone (including judges) complete the experience with hands only.

## Privacy

Mic audio and hand data are processed live on the headset and never stored or sent. Progress is saved on the device only.

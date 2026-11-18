# SUBMISSION.md — Devpost form drafts

Fill the blanks, check every item against `REQUIREMENTS.md`, then copy into the form.

## Basics
- **Submission name:** GhostFret
- **Track:** Productivity
- **Division:** New Experience
- **Target launch date:** _fill in_
- **Team:** _name · email · role_ for each member

## Tagline (≤ 140 characters)
> Learn guitar on your real guitar: a ghost hand shows where fingers go, and a coach listens and tells you exactly what to fix.

(123 characters.)

## Description (~500 words) — outline to expand

**Inspiration.** Most people who pick up a guitar give up within the first year. Videos are flat and mirrored, and when a chord sounds wrong, beginners can't tell why. Mixed reality lets us put the lesson on the player's own guitar.

**What it does.**
- Fit the fretboard with three fingertip touches; a glowing fretboard locks onto your real neck.
- Tune with a headstock tuner.
- Chord Coach: a ghost hand shows the shape; the coach listens and names the muted string.
- Strum Coach: down/up arrows beside the guitar; timing from sound, direction from hand tracking.
- Song Mode: public-domain songs with adaptive tempo.
- Air Guitar mode: practise shapes and rhythm with no guitar.

**How we built it.** Unity + Meta XR All-in-One SDK on Quest 3: passthrough, hand tracking, Interaction SDK. Our own audio engine (YIN pitch, chroma chord matching, per-string checks, spectral-flux onsets) and calibration maths (equal-temperament fret positions from three touches). Core logic is plain C#, tested outside Unity.

**Challenges.** Fingers on the neck hide from the cameras, so sound decides correctness and hand tracking handles strum direction, calibration and UI.

**What's next.** Headstock-marker tracking with the Passthrough Camera API, barre chords, licensed songs, teacher mode, bass and ukulele.

## Hand interactions (optional field)
- Calibration by fingertip touch (dwell 1 s on the nut and 12th fret).
- Poke and pinch for all menus, placed beside the guitar body.
- Strum direction from wrist motion, matched to audio onsets.
- Pause with palm up (no pinch), so the system gesture stays reserved.
- Air Guitar mode: chord shapes from finger joints, strums from wrist crossing virtual strings.
- The whole experience runs without ever pairing a controller.

## Demo video script (< 3:00, best first)
| Time | Shot |
|---|---|
| 0:00–0:15 | Ghost hand on a real guitar; strum; "Clean Em!" |
| 0:15–0:35 | Three-touch fretboard fit snapping onto the neck |
| 0:35–0:55 | Headstock tuner |
| 0:55–1:35 | Chord Coach: muted G string caught and fixed |
| 1:35–2:05 | Strum Coach: arrows, "a little early", fixed |
| 2:05–2:35 | Song Mode: two-chord song end to end |
| 2:35–2:50 | Air Guitar mode, controllers nowhere in sight |
| 2:50–3:00 | Title + "Learn on your real guitar" |

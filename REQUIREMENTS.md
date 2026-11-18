# REQUIREMENTS.md — Hackathon rules → how GhostFret meets them

Source: [Meta VR Start Developer Competition 2026](https://start-developer-competition-26.devpost.com/). Re-check the page and full rules before submitting.

Status key: ⬜ not done · 🟨 in progress · ✅ done. Update this file together with `PROGRESS.md`.

---

## 1. Eligibility

| Rule | How we meet it | Status |
|---|---|---|
| Must be a member of the Meta VR Start program | Confirm membership on the Start site before submitting | ⬜ |
| Above the legal age of majority; country not excluded | Check full rules for each team member | ⬜ |

## 2. What to build

| Rule | How we meet it | Status |
|---|---|---|
| Built for Meta VR devices (Meta Quest or Meta VR Glasses) | Meta Quest 3 / 3S, Unity + Meta XR SDK | ⬜ |
| Aligned to at least one track (Entertainment, Gaming, Productivity) | **Productivity**: daily practice habit + creative tool (DESIGN.md §13.1) | ⬜ |
| **New Experience division**: conceived and built in the window starting Sep 24, 2026; no pre-existing codebase | New repo; first commit after Sep 24, 2026; no reused shipped code | ⬜ |

## 3. Hard requirements

| Rule | How we meet it | How we test it | Status |
|---|---|---|---|
| **Hands-first:** fully usable with hands, end to end; can be completed without ever pairing a controller | All UI via Meta Interaction SDK poke/pinch; no controller-only actions; **Air Guitar mode** completes the experience even without a guitar | Full run with controllers switched off and unpaired: welcome → fit or air mode → lesson → song → wrap-up | ⬜ |
| **Policy compliance:** Start Program Terms, Community Standards, Code of Conduct in VR, Developer App Policies, Content Guidelines, all Developer Policies | No health claims; public-domain/original content only; mic/hand/camera data processed on device, never stored or sent; no user-to-user features | Wording audit; content source check (`SongValidator`); privacy review | ⬜ |
| Platform rule: system gesture reserved (palm toward headset + pinch opens system menu) | Our pause = palm up, still, **no pinch**; never bind actions to the system gesture | Do the system gesture in every screen: only the system menu appears | ⬜ |

## 4. Design guidelines (scored, not mandatory)

| Guideline | How we meet it | Status |
|---|---|---|
| **Seated-optimized** (airplane seat test: every interaction within two feet) | Guitar is played seated; all UI sits on or beside the guitar | ⬜ |
| **Easy in and out** (fast cold start, clean pause/resume, satisfying in ≤10 min; one bus stop test) | First chord within 5 min; sessions 10–15 min; pause via palm-up or silence; resume restores state | ⬜ |
| **Original, not a wrapper** (take-it-away test) | Calibration, audio engine, lessons all built by us; no third-party service required | ⬜ |
| Suggested SDK: Unity Meta XR SDKs v81+ All-in-One | Using Meta XR All-in-One SDK v81+ | ⬜ |

## 5. What to submit

| Item | Detail | Status |
|---|---|---|
| **Playable build** | APK uploaded to a release channel named exactly **"Competition"** in the Meta VR Developer Dashboard | ⬜ |
| Judge access | Create and submit an **Invite URL** for that channel | ⬜ |
| Build stays available | Don't change or remove the build during judging | ⬜ |
| **Demo video** | Under 3 minutes; footage on a Meta VR device (or XR Simulator); public on YouTube or Vimeo; best material first | ⬜ |
| **Submission form** | Name; tagline ≤140 chars; track; division; description (~500 words: inspiration, how built, future plans); hand interactions + future of VR (optional); target launch date; team names, emails, roles | ⬜ (drafts in SUBMISSION.md) |
| Deadline | **Nov 18, 2026, 12:00 PM PST**; entries can't be changed after | ⬜ |

## 6. Special awards we target

| Award | Our evidence |
|---|---|
| Best Reason to Come Back | Daily streaks, curriculum unlocks, drill personal bests |
| Best First Five Minutes | First real chord on your own guitar in under 5 minutes |
| Best Accessibility Forward Experience | Left-handed mode, captions, colour + shape cues, seated short sessions |

## 7. Final pre-submit checklist

- [ ] Fresh install from the Invite URL works on a Quest 3 with controllers off
- [ ] Air Guitar mode completes a full session without a guitar
- [ ] Cold start to first interactive screen is quick
- [ ] Pause and resume work in every mode
- [ ] No medical wording; no copyrighted songs, lyrics or recordings
- [ ] Mic permission prompt explains why; nothing recorded or uploaded
- [ ] Video < 3:00, public, shows the headset view
- [ ] Form complete; tagline ≤140 characters

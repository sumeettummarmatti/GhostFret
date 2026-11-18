# GhostFret: setup and test guide (for Tanisha and her coding agent)

GhostFret is a mixed-reality guitar coach for **Meta Quest 3**. You see your real room and real guitar through passthrough; a glowing fretboard and "ghost" finger dots appear on the real neck, a coach **listens** to each chord through the mic, a strum lane keeps time, and **Air Guitar mode** lets anyone finish a full session with no guitar (hands only).

- Repo: https://github.com/sumeettummarmatti/GhostFret (branch `main`)
- Engine: Unity **6000.3.28f1** (Unity 6.3 LTS) + Meta XR Core SDK 209 + URP
- What works today: all code is written, **246 Core tests pass**, the project **compiles and plays in the Unity Editor** with a stand-in guitar preview. **Not yet tested on a real Quest 3**, and the scene still needs the Quest camera rig (Part F). Your job: get it running and test everything, step by step.
- Browser mock-up of the look (optional, for reference only, not the real app): https://claude.ai/artifact/HuiXZLbtpKXef8gLas6rtn (only works if Sumeet has shared it with you).

---

## UPDATE 2026-11-19: fixes from the first headset test (do this first)

**What was wrong, and what changed in `main`:**
| Seen on the Quest | Cause | Fix |
|---|---|---|
| Text overlapping inside cards | Font sizes ~1.4x too big and text boxes let long lines spill into neighbours | Every text now sits in a fixed box and shrinks to fit (`UIKit.FitText`) |
| Cards overlapping each other (Air mode had 3 copies of the same note) | Air card + "I'm done" card + caption all in one spot | One honesty note, "I'm done" card sits beside the Air card, captions sit above cards |
| Cards covering the guitar body | "Beside the guitar" was placed over the strings | Cards float 20 cm above the body (`UIKit.CardRise`) and tilt toward your eyes |
| See-through cards | 82% opacity in passthrough | 95% |
| Solid blue/grey hand blob | Meta's hand mesh drawn opaque over your real hand | Hand meshes are hidden at runtime; tracking still works (`MetaHandInput.hideHandMeshes`) |
| No sound | The Quest camera rig had no AudioListener (Unity plays nothing without one) | Added automatically at start; UI sounds louder |
| Mic not working / "Paused" card after the mic dialog | The Android permission dialog paused the app and nothing resumed it | Permission dialog no longer pauses; the Meta menu pauses and auto-resumes; mic is requested when you press Start; waits 8 s for the first samples |
| Empty boxes instead of ✓ | Built-in font has no ✓ / ○ / … | Plain characters only |

**Tanisha: do these steps, in order**
1. In your repo folder: `git pull origin main` (if you changed files, commit them first; your scene edits from Part F won't conflict, these fixes are scripts only).
2. Switch to Unity and wait for it to recompile. Console: **no red errors**.
3. Open your Quest scene (the one with the Camera Rig). Check the rig's **CenterEyeAnchor** camera is tagged **MainCamera**. No need to add an AudioListener; the app adds one if it's missing.
4. **Reset the mic permission** (the old build may have been denied): in the headset, Settings → Apps (or Privacy & Safety → App permissions) → **GhostFret** → Microphone → **Allow**. Or simply uninstall GhostFret from the headset (Library → Unknown sources → ⋯ → Uninstall) so it asks again.
5. **Build And Run** to the Quest (File → Build Profiles → Android → Build And Run).
6. In the headset, test and note pass/fail:
   - [ ] Welcome card text is clean (no overlapping lines), floating above the guitar, readable.
   - [ ] You hear a soft click when you press a button.
   - [ ] Press **Start** → Android asks for the microphone → **Allow** → the app does **not** show "Paused".
   - [ ] Your real hands are visible (no blue/grey blob). Pressing buttons and the 3 fit touches still work.
   - [ ] Tuner: pluck a string → the needle moves and the note name shows (this proves the mic works).
   - [ ] Chord Coach: strum Em → green strings and "Clean Em!" (or a gentle tip). You hear the chime.
   - [ ] Air mode: one Air card + a separate small "I'm done" card; strumming plays a guitar sound.
   - [ ] Open the Meta menu (palm-toward-you pinch) then close it → the app picks up where it was.
7. If something still fails, send: a headset screenshot/clip, the step, and the Unity log from the headset:
   `adb logcat -s Unity` (run while reproducing; Android SDK platform-tools come with Unity: on Mac `/Applications/Unity/Hub/Editor/6000.3.28f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb`).

---

Work through the parts in order. Each step says what to do and how to know it worked (✅). Don't skip the ✅ checks.

---

## Part A: Set up the Quest 3 (about 45 minutes, mostly waiting)

You need: the Quest 3, its charger, a **USB-C data cable** (the one in the box works; some cheap cables are charge-only), a phone, and Wi-Fi.

1. **Charge** the headset for at least 30 minutes. The light on the side turns green when full.
2. **Turn it on:** hold the power button on the left side for 3 seconds.
3. Put it on and follow the on-screen steps: language, Wi-Fi.
   ✅ You see a pairing code or a "use the Meta Horizon app" screen.
4. On your phone, install the **Meta Horizon** app (App Store / Google Play) and sign in with your Meta account (create one if needed).
5. In the app: **Menu → Devices → Add device → Quest 3**, then enter the code shown in the headset.
   ✅ The app shows your Quest 3 as connected.
6. Let the headset **install its software update**. This can take 10–30 minutes; leave it on charge.
7. When asked to set up a **boundary**, choose **Stationary** (you'll be seated with a guitar).
8. **Turn on hand tracking** in the headset:
   Settings (gear icon) → **Movement tracking** → **Hand and body tracking** → turn on **Hand tracking** and **Auto switch between hands and controllers**.
   ✅ Put the controllers down; after a few seconds you see your hands instead of controllers.
9. Learn the **system gesture** (you'll need to know it to test that the app never steals it): look at your palm facing you, then pinch thumb and index finger. The Meta menu appears. GhostFret must never use this gesture.

### A2. Make the Quest a developer device (needed to install the app from Unity)

10. On a computer, go to **https://developers.meta.com/horizon/** and sign in with the **same Meta account**.
11. Create a developer **organization**: Manage → Organizations → **Create new organization** (any name, e.g. "Tanisha Dev"). Meta may ask you to verify the account (phone number / two-factor, or a card). Do it.
12. In the **Meta Horizon** phone app: **Menu → Devices → (your Quest 3) → Headset settings → Developer mode** → turn it **on**.
13. **Restart** the headset (hold power → Restart).
14. Connect the headset to your computer with the USB-C cable. In the headset a popup says **"Allow USB debugging?"** → tick **Always allow from this computer** → **Allow**.
    ✅ (after Part B installs Android tools) `adb devices` lists one device as `device`, not `unauthorized`.

---

## Part B: Set up your computer

You need **about 30 GB free disk space** (Unity + Android tools ~20 GB, project import ~3 GB).
Mac (Apple Silicon or Intel) or Windows 10/11 both work. **Windows bonus:** with Meta Quest Link you can press Play in Unity and see it live in the headset; on Mac you must build to the headset each time.

### B1. Basic tools

15. **Git**
    - Mac: open Terminal and run `git --version`. If it isn't installed, macOS offers to install it; accept.
    - Windows: install from https://git-scm.com/download/win (default options).
    ✅ `git --version` prints a version.
16. **.NET 8 SDK** (runs the logic tests without Unity): https://dotnet.microsoft.com/download/dotnet/8.0 → install the SDK.
    ✅ `dotnet --list-sdks` shows an 8.x (a newer 9.x alone also works; the test project rolls forward).
17. **Get the code:**
    ```
    cd ~/Desktop
    git clone https://github.com/sumeettummarmatti/GhostFret.git
    cd GhostFret
    ```
    ✅ The folder has `README.md`, `DESIGN.md`, `PLAN.md`, `PROGRESS.md`, `unity/`, `core-tests/`.
    If you'll push changes back, ask Sumeet to add your GitHub account as a collaborator, then work on your own branch (Part H).

### B2. Check the logic tests (2 minutes, no Unity needed)

18. Run:
    ```
    cd core-tests
    dotnet test
    cd ..
    ```
    ✅ Ends with **`Passed! - Failed: 0, Passed: 246`**. If not, stop and report the output.

### B3. Install Unity (the long part)

Pick **one** way.

**Way 1: Unity Hub (easiest by hand)**
19. Install **Unity Hub** from https://unity.com/download.
20. Open Hub → **sign in** (free Unity account) → when asked about a licence choose **Unity Personal** (free).
21. Hub → **Installs → Install Editor → Archive → download archive** (or the version list) → find **6000.3.28f1**. It must be exactly this version.
    On the modules screen tick **Android Build Support** and inside it **OpenJDK** and **Android SDK & NDK Tools**. Accept the licences.
    ✅ Installs shows 6000.3.28f1 with the Android icon.

**Way 2: Unity CLI (good for a coding agent)**
19. Mac/Linux: `curl -fsSL https://unity.com/install.sh | bash` then open a new terminal.
    Windows (PowerShell): `irm https://unity.com/install.ps1 | iex`
20. `unity auth login` (opens the browser; sign in) → `unity license activate --personal --accept-eula`
21. `unity install 6000.3.28f1 -m android --cm --accept-eula -y`
22. `unity terms accept 6000.3.28f1` (read and accept Unity's Editor terms).
    ✅ `unity editors` shows 6000.3.28f1 with Android modules; `unity license status` shows Unity Personal.

---

## Part C: Open the project in Unity (first open takes 5–15 minutes)

23. Open the project folder **`GhostFret/unity`** (not the repo root):
    - Hub: **Projects → Add → Add project from disk →** pick `GhostFret/unity` → click it.
    - CLI: `unity open ./unity --editor-version 6000.3.28f1`
24. Wait for the first import (progress bars). If Unity asks to **restart for the new Input System**, click **Yes**. If Meta pops up "Project Setup Tool" suggestions, you can close it for now (Part F uses it).
25. Open the scene: Project window → **Assets/GhostFret/Scenes/GhostFret.unity** (double-click).
26. Check the **Console** (Window → General → Console).
    ✅ **No red compile errors.** These are known and harmless:
    - one `ArgumentOutOfRangeException` from `UnityEditor.Search` on first start (Unity's own search indexer);
    - "Multiple ADB server instances found" warnings.

**If something is wrong (fixes we already hit):**
| You see | Fix |
|---|---|
| Unity opens in **Safe Mode** | Something failed to compile. Read the first red error in the Console and report it. Don't edit `Packages/manifest.json` versions unless the error names a package. |
| Everything is **magenta/pink** | Edit → Project Settings → Graphics → Default Render Pipeline must be **GhostFret_URP** (in `Assets/Settings`). Same under Quality for each level. |
| **No text** on panels | Window → TextMeshPro → **Import TMP Essential Resources**. |
| Licence error / "exit 198" | Sign in to Hub and activate **Unity Personal**. |

---

## Part D: Test on your computer (no headset needed)

27. Press **▶ Play** (top centre of Unity). You should see: a dim room, a stand-in acoustic guitar on your lap, the fretboard lines already fitted on its neck, a **Welcome** card ("Grab your guitar and sit comfortably." with **Start** and **No guitar? Try Air mode**), and a key list in the top-left.
28. Click inside the **Game** view once so it gets keyboard input. Controls:

| Input | Does |
|---|---|
| Mouse | your right index fingertip; **click = pinch** (presses buttons) |
| Right-drag | look around |
| Shift (hold) | mouse moves the left (fretting) hand instead |
| 1–4 | bend left index…pinky (Air mode shapes) |
| **N** | next / skip the current step |
| **C** | clean strum |
| **M** | muted G string |
| **B** | buzz on A string |
| **W** | wrong chord (sounds like G) |
| **Q** / Shift+Q | too quiet / not sure |
| **Space** | strum on the beat (Strum Coach, songs) |
| **T** | finish tuning |
| **F** | re-fit the fretboard |
| **A** | Air mode |
| **P** (hold) | palm up = pause |
| **L** | turn the real laptop mic on/off (strum a real guitar near the laptop) |
| **H** | hide/show the key list |

29. Walk through **every step** and tick each ✅. Write down anything that looks wrong, with a screenshot.
    - [ ] **Welcome:** both buttons respond to a click.
    - [ ] **Fit:** after Start, fretboard confirmation ("Looks right" / "Try again") works; **F** re-fits.
    - [ ] **Tune:** tuner panel near the headstock, six string chips; **T** completes it; "Skip tuning" works.
    - [ ] **Chord Coach (Em):** ghost dots 2 and 3 on the A and D strings, fret 2. **C** → green strings + "Clean Em!"; **M** → G string pulses amber + a kind tip; **W** → "sounds like G…" message; **Q** → general tip, never a wrong claim. 3 clean strums → celebration.
    - [ ] **Strum Coach:** arrows scroll to a line with a soft click; **Space** on the beat → "On time"; early/late messages are gentle (amber, never red).
    - [ ] **Song (Em Groove / Drunken Sailor):** count-in, ghost hand changes chord one beat early, summary panel at the end is kind.
    - [ ] **Change Drill:** 60-second timer, best score saved.
    - [ ] **Pause:** hold **P** → "Paused. Take your time." → Resume returns to the same place.
    - [ ] **Air mode (A):** see-through guitar outline, honesty note ("Air mode practises shapes and rhythm…"), strumming plays a plucked chord, Day 1 finishes (Em → 4 strums → D → 4 switches → celebration).
    - [ ] **Wrap-up:** streak + tomorrow's goal; stop and Play again: streak/progress remembered.
    - [ ] **Settings:** left-handed, overlay brightness, metronome volume, delete progress (asks to confirm with a second button).
    - [ ] **Console:** no new red errors during the whole run.

**Known issues already on the list** (no need to report, but note if they're worse): Welcome card text is too big and overlaps; panels look slanted when looking down; fretboard lines are faint on light wood; the stand-in guitar is right-handed only.

---

## Part E: Rules every test must respect (from REQUIREMENTS.md)

Report any break of these as **high priority**:
1. **Hands only.** Everything works without controllers. Never needs a controller button.
2. **Air mode always completes** a full session with no guitar.
3. **System gesture is never used.** Palm facing you + pinch must only open Meta's menu. Pause is palm **up**, held still, **no pinch**.
4. **Audio decides chord correctness**; when unsure the coach stays quiet or gives a general tip, never a wrong claim.
5. **Privacy:** mic/hand data is never saved or sent. Progress is saved on the device only.
6. **No health claims** in any text. **No copyrighted songs/lyrics.**
7. **Seated, within reach**, no flashing, no locomotion. Colour is never the only signal (✓ / ! shapes too).

---

## Part F: Get it onto the Quest 3

> Not done yet by anyone. The current scene only has a normal camera for the computer preview. These steps add the Quest pieces. Make a git branch first (Part H) so you can undo.

30. **Switch platform:** File → **Build Profiles** → **Android** → **Switch Platform** (takes a few minutes).
31. **Meta setup:** menu **Meta → Tools → Project Setup Tool** → select the **Android** tab → **Fix All**, then **Apply All**. Repeat until no red items remain. (It sets XR plug-in management / OpenXR, graphics API, ARM64, etc.)
32. Check: Edit → Project Settings → **XR Plug-in Management → Android tab**: the Meta/OpenXR provider is ticked. Player → Other Settings → **Scripting Define Symbols (Android)** contains `GHOSTFRET_META_SDK` (already set in the repo).
33. **Camera rig with passthrough and hands:** in the GhostFret scene:
    - Delete **Main Camera** (the Editor preview only runs when no Quest hands exist, so this is fine).
    - Menu **Meta → Tools → Building Blocks** → add **Camera Rig**, **Passthrough**, **Hand Tracking**.
    - Select the rig's **CenterEyeAnchor** camera: Tag = **MainCamera**, Background Type = **Solid Color**, colour alpha **0** (fully transparent) so passthrough shows.
    - On both hands' **OVRSkeleton**: Skeleton Type = **XR Hand Left / XR Hand Right**.
    - On **OVRManager** (on the rig): Hand Tracking Support = **Hands Only** (or Controllers and Hands), Hand Tracking Frequency = **High**, **Passthrough Support = Supported/Required**, and tick **Enable Passthrough** (Insight Passthrough).
    - Keep the **GhostFret** object (GhostFretApp) in the scene.
    - Save the scene (Cmd/Ctrl+S).
34. **Microphone permission:** Project Settings → Player → Android → Other Settings: nothing to add by hand; Unity adds `RECORD_AUDIO` because the code uses the Microphone. (Check after building.)
35. Plug in the Quest (Part A step 14). In Build Profiles → Android → **Run Device** pick your Quest 3 → **Build And Run** → save the APK as `Builds/GhostFret.apk`.
    ✅ The app starts in the headset and you see your real room.
    Later you can find it in the headset under **Library → Unknown sources** (or "Apps → filter: Unknown sources").
36. **Windows only, faster loop:** install the **Meta Quest Link** app on the PC, connect the headset (cable or Air Link), enable Link in the headset, set Meta Quest Link as the active OpenXR runtime (Link app → Settings → General). Then pressing ▶ Play in Unity shows the app live in the headset.

---

## Part G: Test on the headset (sit down, controllers off, real guitar if you have one)

Turn the controllers **off** (or leave them on a table out of reach). Then:

- [ ] Cold start to the Welcome card is quick; card is within arm's reach.
- [ ] Mic permission prompt explains why ("GhostFret listens to your guitar… never recorded or sent").
- [ ] **Fit:** touch nut (thick string), nut (thin string), 12th fret (thick string) with the left index finger, holding 1 s each; ring fills; lines line up with the real strings. Note how far off they are (mm).
- [ ] **Tune:** pluck each string; needle settles; "Tune up/down a little"; chips turn green.
- [ ] **Chord Coach:** play a real Em → "Clean Em!"? Mute the G string on purpose → does it name the G string? Play a G → does it say "sounds like G" or stay quiet (both fine; a wrong claim is a bug).
- [ ] **Strum Coach:** strum on the clicks → mostly "On time"? Note latency feeling.
- [ ] **Song:** one full song with chord changes.
- [ ] **Air mode:** full Day 1 with no guitar, hands only.
- [ ] **Pause:** palm up, still, no pinch → pauses. **System gesture** (palm toward you + pinch) → only Meta's menu opens, the app doesn't react.
- [ ] Left-handed mode mirrors correctly (if you can test).
- [ ] No flicker/flashing; text readable; nothing far away.

Record a few short clips (headset: Meta button → Camera → Record video). They help with the competition video.

---

## Part H: Reporting and pushing changes

37. Create a branch before changing anything: `git switch -c tanisha/testing`
38. For each problem, note: **which step**, what you expected, what happened, a screenshot or clip, and any red Console error (copy the full text).
39. Add a short entry to the **Session log** at the bottom of `PROGRESS.md` and tick things that now work on device in the Phases list (rule: a phase is ✅ only when it works on Quest 3 with controllers off).
40. **Never commit** `unity/Assets/Resources/DevAgentSettings.asset` (a per-machine Meta token; it's in `.gitignore`), APKs, or the `Library/` folder.
41. `git add -A && git commit -m "Testing: <what>" && git push -u origin tanisha/testing`, then open a Pull Request on GitHub.

---

## Part I: Instructions for Tanisha's coding agent (paste this to it)

> You are helping test and finish **GhostFret**, a Meta Quest 3 mixed-reality guitar coach (Unity 6000.3.28f1, URP, Meta XR Core SDK 209). Repo root = the `GhostFret` folder; Unity project = `GhostFret/unity`.
>
> **Read first, in order:** `CLAUDE.md` (hard rules + C# 9 limits), `REQUIREMENTS.md`, `DESIGN.md`, `PLAN.md`, `PROGRESS.md`, `docs/UX_GUIDE.md`, `docs/SETUP_UNITY.md`.
>
> **Architecture:** `unity/Assets/GhostFret/Scripts/Core/` is pure C# (no UnityEngine), tested by `core-tests/` (`cd core-tests && dotnet test`, must stay 246/246+). `Scripts/Runtime/` is Unity glue; all UI is built in code (`Runtime/UI`: Theme, UIKit, PokeButton, Coach, UiSounds). `GhostFretApp` bootstraps every component; the scene needs only a camera (rig) + one GameObject with `GhostFretApp`. Editor-only test harness: `Runtime/EditorPreview/` (stand-in guitar + keyboard shortcuts, only when no OVRHand exists). Meta code is behind `#if GHOSTFRET_META_SDK` (defined for Android only).
>
> **Driving Unity from the terminal:** the project includes `com.unity.pipeline`. With the Unity CLI (`unity`), use: `unity status --until-ready --project-path <abs path to unity/>`, `unity command console status` (save the cursor first), `unity recompile`, `unity command editor.playmode start|stop`, `unity command capture game_view -- --output-path <png>`, `unity command console get -- --since <cursor> --level error`, `unity command script eval '<C#>'`. Exit Play mode and restore `set_run_in_background`/`set_auto_tick` when done. Never run `console clear`.
>
> **Hard rules (never break):** hands only (no controller actions); Air mode must always complete a session; never use the system gesture (pause = palm up, still, no pinch); audio decides chord correctness and low confidence = quiet or a general tip; mic/hand/camera data never stored or sent; no health claims; public-domain/original songs only (`SongValidator` must pass); seated, everything within reach, no flashing.
>
> **Your tasks, in order:** (1) verify setup per Parts B–D and fix any compile/runtime errors; (2) run the Part D checklist in the Editor and fix bugs; (3) fix the known layout issues (Welcome card text overflow/overlap, panels should face the camera including pitch, fretboard lines more visible); (4) do Part F (Quest rig + passthrough + hands, Project Setup Tool) and get a build on the headset; (5) support the Part G device test and tune values listed in `PROGRESS.md` → "Tuning values" (audio thresholds were tuned on synthetic audio only). After each task: run `dotnet test`, check the Console for errors, update `PROGRESS.md` (tick boxes, session log line, tuning values, known issues), commit on the `tanisha/testing` branch. Ask before installing editors, changing licences, deleting files, or force-pushing.
>
> **Known gaps:** mic latency calibration exists (`LatencyCalibrator`) but isn't wired into the flow; Strum Coach restarts from the count-in after pause; panel layout untested in a headset; no backing tracks (metronome only); real-mic thresholds untuned.

---

## Quick reference

| Thing | Where |
|---|---|
| Unity version | 6000.3.28f1 (Unity 6.3 LTS) + Android Build Support |
| Project folder | `GhostFret/unity` |
| Scene | `Assets/GhostFret/Scenes/GhostFret.unity` |
| Logic tests | `cd core-tests && dotnet test` → 246 pass |
| Status / to-do | `PROGRESS.md` |
| Rules | `CLAUDE.md`, `REQUIREMENTS.md` |
| Design | `DESIGN.md`, `docs/UX_GUIDE.md` |
| Unity log (Mac) | `~/Library/Logs/Unity/Editor.log` |
| Unity log (Windows) | `%LOCALAPPDATA%\Unity\Editor\Editor.log` |

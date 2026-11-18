# Unity + Quest 3 setup

Follow Meta's current docs if any step differs; menus change between SDK versions.

## 1. Install
1. Unity Hub → install **Unity 6 LTS** with **Android Build Support** (OpenJDK + Android SDK & NDK).
2. Quest 3: enable **Developer Mode** (Meta Horizon phone app → device → Developer Mode).
3. Connect the Quest by USB and allow USB debugging in the headset.

## 2. Open the project
This repo ships only `unity/Assets`. Unity creates the rest when it opens the folder.
1. Unity Hub → **Add → Add project from disk** → choose `ghostfret/unity`.
2. If Hub refuses (no ProjectSettings yet): create a new **Universal 3D (URP)** project anywhere, close it, copy its `Packages/` and `ProjectSettings/` folders into `ghostfret/unity/`, then add `ghostfret/unity` in Hub.

## 3. Meta XR SDK
1. Install **Meta XR All-in-One SDK** (v81 or newer, as the competition suggests) via Package Manager / Asset Store.
2. Run **Meta → Tools → Project Setup Tool** and apply all fixes for Android.
3. Switch platform to **Android** (File → Build Profiles).
4. Player Settings → Scripting Define Symbols → add `GHOSTFRET_META_SDK` (turns on the Meta hand-input code).

## 4. Features and permissions
- **Passthrough:** enable in the OVRManager / camera rig and add a passthrough layer.
- **Hand tracking:** set Hand Tracking Support to "Hands Only" or "Controllers and Hands" (hands must work alone).
- **Microphone:** Android permission `RECORD_AUDIO`; `MicrophoneInput` requests it at runtime with a clear reason.
- **(v2) Passthrough Camera API:** headset camera permission; see Meta's Passthrough Camera API docs.

## 5. Scene
1. New scene `GhostFret.unity`: Meta camera rig (from building blocks), passthrough, hand tracking, Interaction SDK rig.
2. Add an empty `GhostFretApp` object with the `GhostFretApp` component and its child components (see the script headers).
3. Build & Run to the Quest 3.

## 6. Competition build
1. Release build (IL2CPP, ARM64).
2. Meta VR Developer Dashboard → your app → upload APK to a release channel named **Competition** → create an **Invite URL**.

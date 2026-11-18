Add a song chart: $ARGUMENTS

1. Confirm the song is public domain (traditional, or composer died long ago) or write an original. If unsure, stop and ask.
2. Create `unity/Assets/StreamingAssets/GhostFret/songs/<id>.json` following an existing chart. No lyrics.
3. Use only chords from `ChordLibrary`. Set `chordsRequired` to exactly the chords used.
4. Add the song id to the right day in `curriculum.json` (the day its last chord is taught).
5. Run `cd core-tests && dotnet test` so the content validator checks it.

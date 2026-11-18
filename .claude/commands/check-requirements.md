Audit the project against REQUIREMENTS.md.

For each row: read the relevant code and docs, then mark ✅ / 🟨 / ⬜ with a one-line reason.
Also search all player-facing strings for medical words (therapy, rehab, treatment, diagnose, patient, pain relief) and for controller-only input.
Run `cd core-tests && dotnet test` (this includes the song content validator).
Update the status column in REQUIREMENTS.md and list what's left, most important first.

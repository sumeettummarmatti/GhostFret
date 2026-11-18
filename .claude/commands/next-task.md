Read CLAUDE.md, REQUIREMENTS.md, PLAN.md and PROGRESS.md.

1. Find the current phase in PROGRESS.md and the first unchecked task for it in PLAN.md.
2. Say in one line which task you're doing.
3. Put testable logic in `unity/Assets/GhostFret/Scripts/Core/` (pure C#, C# 9) and add or extend tests in `core-tests/`.
4. Put Unity glue in `Scripts/Runtime/`. List any Unity Editor steps I must do by hand.
5. Run `cd core-tests && dotnet test` and fix failures.
6. Check the task against the hard rules in CLAUDE.md.
7. Update PROGRESS.md (tick, session log line, tuning values, issues).

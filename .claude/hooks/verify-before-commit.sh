#!/usr/bin/env bash
# PreToolUse hook: runs only for `git commit` (see "if" in settings.json).
# Exit 2 blocks the commit and returns stderr to Claude as the reason.
set -uo pipefail
cd "${CLAUDE_PROJECT_DIR:?}" || exit 2

if ! dotnet format --verify-no-changes >&2; then
  echo "Commit blocked: formatting drift. Run 'dotnet format' and retry." >&2
  exit 2
fi
if ! dotnet test tests/Ingestion.UnitTests --nologo >&2; then
  echo "Commit blocked: unit tests fail. Fix the code, not the tests." >&2
  exit 2
fi
exit 0

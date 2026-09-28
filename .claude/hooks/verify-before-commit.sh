#!/usr/bin/env bash
# PreToolUse hook for Bash. Claude Code sends the tool call as JSON on stdin. Only `git commit`
# is gated; every other command passes straight through. The script filters for itself because
# some Claude Code versions ignore the "if" in settings.json and run the hook for every command.
# Exit 2 blocks the commit and returns stderr to Claude as the reason.
set -uo pipefail
hooks_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)

input=$(cat)
# Cheap check first: most commands never mention both words.
case "$input" in
  *git*commit*) ;;
  *) exit 0 ;;
esac
# Then scan the command the way a shell reads it, so text that merely mentions git and commit (a
# heredoc, a quoted message) passes. Tests: runs-git-commit.test.sh.
if ! awk -f "$hooks_dir/runs-git-commit.awk" <<<"$input"; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:?}" || exit 2

# CLAUDE.md: never commit to main; one branch per work item.
branch=$(git branch --show-current)
case "$branch" in
  "")
    echo "Commit blocked: detached HEAD. Commit on a feature/<id>-<slug> branch." >&2
    exit 2 ;;
  main | master)
    echo "Commit blocked: '$branch' is protected. Create feature/<id>-<slug> first." >&2
    exit 2 ;;
esac

if ! dotnet format --verify-no-changes >&2; then
  echo "Commit blocked: formatting drift. Run 'dotnet format' and retry." >&2
  exit 2
fi
# Microsoft Testing Platform (global.json): options such as --nologo reach the test app, so pass none.
if ! dotnet test tests/Ingestion.UnitTests >&2; then
  echo "Commit blocked: unit tests fail. Fix the code, not the tests." >&2
  exit 2
fi
exit 0

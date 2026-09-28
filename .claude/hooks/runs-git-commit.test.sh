#!/usr/bin/env bash
# Tests for runs-git-commit.awk: which Bash tool calls the commit hook gates.
# Run: bash .claude/hooks/runs-git-commit.test.sh   (exits 1 if any case fails)
# Portability check: AWK="gawk --posix" bash .claude/hooks/runs-git-commit.test.sh
set -uo pipefail
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
read -r -a awk_cmd <<<"${AWK:-awk}"
failures=0

json_escape() {
  local s=$1
  s=${s//\\/\\\\}; s=${s//\"/\\\"}; s=${s//$'\n'/\\n}; s=${s//$'\t'/\\t}
  printf '%s' "$s"
}

# expect <gated|passes> <command>: builds the PreToolUse payload Claude Code sends and checks the verdict.
expect() {
  local want=$1 command=$2 got
  local payload="{\"session_id\":\"t\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"$(json_escape "$command")\",\"description\":\"test\"}}"
  if "${awk_cmd[@]}" -f "$here/runs-git-commit.awk" <<<"$payload"; then got=gated; else got=passes; fi
  if [ "$got" != "$want" ]; then
    failures=$((failures + 1))
    printf 'FAIL expected %s, got %s: %s\n' "$want" "$got" "$command"
  fi
}

# Commits, however they are written.
expect gated 'git commit -m x'
expect gated 'git commit'
expect gated 'cd repo && git add a && git commit -m msg'
expect gated $'git -c user.name="Michael A Griffey" -c user.email="m@x" commit -q -F - <<\'EOF\'\nfeat: x\nEOF'
expect gated 'git -C /repo commit --amend --no-edit'
expect gated 'git -c core.editor=true commit'
expect gated 'GIT_AUTHOR_NAME=x git commit -m y'
expect gated 'env GIT_EDITOR=true git commit'
expect gated '/usr/bin/git commit -m x'
expect gated '(cd repo; git commit -m x)'
expect gated 'echo x | git commit -F -'
expect gated 'git status; git commit -m "it'"'"'s done"'
expect gated "echo \"it's\" && git commit -m 'x'"
expect gated $'git commit -m "first line\nsecond line"'
expect gated $'cat <<EOF\ngit commit\nEOF\ngit commit -m real'
expect gated $'git add . \\\n  && git commit -m x'

# Everything else, including text that only mentions git and commit.
expect passes 'git status --short'
expect passes 'git log --oneline -n 3'
expect passes 'git log --grep commit'
expect passes 'git commit-tree abc'
expect passes 'git show HEAD:commit.txt'
expect passes 'echo committed'
expect passes 'echo "git commit -m x"'
expect passes "printf '%s\\n' 'run git commit later'"
expect passes $'cat > notes.md <<\'EOF\'\nauthored with git -c user.name=x, see commit history\nEOF'
expect passes $'cat <<-EOF\n\tgit commit -m inside\n\tEOF\necho done'
expect passes 'git switch -c feature/1-tighten-commit-hook'
expect passes 'dotnet test ClaudeArticle.slnx'

if [ "$failures" -gt 0 ]; then
  echo "$failures case(s) failed."
  exit 1
fi
echo "All commit-hook filter cases pass."

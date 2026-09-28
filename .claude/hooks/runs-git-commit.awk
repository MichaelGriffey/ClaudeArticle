# Reads a Claude Code PreToolUse payload (JSON) on stdin and exits 0 when its Bash command runs
# `git commit`, 1 otherwise. Used by verify-before-commit.sh.
#
# A text search is not enough: a heredoc or a quoted message that mentions git and commit would
# match. So this scans the command like a shell does, only as far as it needs to:
#   1. Take tool_input.command out of the JSON and undo its escapes.
#   2. Drop what is never a command: quoted text and heredoc bodies.
#   3. Split into simple commands at ; & | ( ) $( and backticks, and at newlines.
#   4. Gate a command whose program is git and whose subcommand, after git's own options, is commit.
# Known gap: a quoted program or subcommand ("git" 'commit') is not recognized.
# Portable awk only (macOS awk, gawk, mawk): no gawk extensions.

{ payload = payload $0 "\n" }

END { exit(runs_git_commit(code_of(command_of(payload))) ? 0 : 1) }

# The "command" string from the payload, with JSON escapes undone. A \uXXXX escape becomes "?":
# only ASCII characters matter to the scan.
function command_of(json,   i, c, n, out) {
    if (!match(json, /"command"[ \t\r\n]*:[ \t\r\n]*"/))
        return ""
    out = ""
    for (i = RSTART + RLENGTH; i <= length(json); i++) {
        c = substr(json, i, 1)
        if (c == "\"")
            break
        if (c == "\\") {
            n = substr(json, ++i, 1)
            if (n == "n") c = "\n"
            else if (n == "t") c = "\t"
            else if (n == "r") c = ""
            else if (n == "u") { c = "?"; i += 4 }
            else c = n
        }
        out = out c
    }
    return out
}

# The command with quoted text replaced by Q, heredoc bodies removed, and a newline wherever one
# simple command ends and another begins.
function code_of(s,   out, len, i, c, q, np, pending, k, dash, delim, rest, p, line) {
    out = ""; q = ""; np = 0; len = length(s)
    for (i = 1; i <= len; i++) {
        c = substr(s, i, 1)
        if (q == "'") { if (c == "'") q = ""; continue }
        if (q == "\"") { if (c == "\\") i++; else if (c == "\"") q = ""; continue }
        if (c == "\\") { i++; continue }                  # escaped character, or a line continuation
        if (c == "'" || c == "\"") { q = c; out = out "Q"; continue }

        # A heredoc (<<WORD, <<-WORD, <<'WORD'), not a here-string (<<<): its body starts on the next line.
        if (substr(s, i, 2) == "<<" && substr(s, i + 2, 1) != "<") {
            i += 2
            dash = (substr(s, i, 1) == "-")
            if (dash) i++
            while (substr(s, i, 1) ~ /[ \t]/) i++
            delim = ""
            for (; i <= len && substr(s, i, 1) !~ /[ \t\n;&|<>()]/; i++)
                if (substr(s, i, 1) !~ /["'\\]/) delim = delim substr(s, i, 1)
            i--
            pending[++np] = (dash ? "-" : "+") delim
            continue
        }

        if (c == "\n") {
            for (k = 1; k <= np; k++) {                    # skip each pending heredoc body in order
                dash = (substr(pending[k], 1, 1) == "-")
                delim = substr(pending[k], 2)
                while (i < len) {
                    rest = substr(s, i + 1)
                    p = index(rest, "\n")
                    line = p ? substr(rest, 1, p - 1) : rest
                    i += (p ? p : length(rest))
                    if (dash) sub(/^\t+/, "", line)
                    if (line == delim) break
                }
            }
            np = 0
            out = out "\n"
            continue
        }

        if (c ~ /[;&|()`]/ || (c == "$" && substr(s, i + 1, 1) == "(")) { out = out "\n"; continue }
        out = out c
    }
    return out
}

# True when a simple command in `code` is git with the subcommand commit.
function runs_git_commit(code,   commands, n, j, w, nw, k) {
    n = split(code, commands, "\n")
    for (j = 1; j <= n; j++) {
        nw = split(commands[j], w, /[ \t]+/)
        k = 1
        # Skip variable assignments and wrappers: GIT_EDITOR=true git ..., env ... git ...
        while (k <= nw && (w[k] == "" || w[k] ~ /^[A-Za-z_][A-Za-z0-9_]*=/ || w[k] ~ /^(env|command|exec|time|nohup)$/))
            k++
        if (k > nw || w[k] !~ /(^|\/)git(\.exe)?$/)
            continue
        # Skip git's own options; these take their value as the next word.
        for (k++; k <= nw; k++) {
            if (w[k] ~ /^(-C|-c|--git-dir|--work-tree|--namespace|--config-env)$/) { k++; continue }
            if (w[k] !~ /^-/) break
        }
        if (k <= nw && w[k] == "commit")
            return 1
    }
    return 0
}

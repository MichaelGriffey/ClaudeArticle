# Organization managed settings (example)

`managed-settings.json` is the organization-level policy from section 4 of the article.
Claude Code does **not** read it from this repository. IT deploys it through the Claude admin
console (server-managed settings), MDM, or as a file at:

- Windows: `C:\Program Files\ClaudeCode\managed-settings.json`
- macOS: `/Library/Application Support/ClaudeCode/managed-settings.json`
- Linux and WSL: `/etc/claude-code/managed-settings.json`

Replace the example MCP server URL, marketplace, network allowlist, and OpenTelemetry endpoint
with your own. Run `/status` in Claude Code to confirm which managed source is in force.

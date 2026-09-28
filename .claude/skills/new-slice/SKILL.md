---
name: new-slice
description: Scaffolds a vertical slice (pure core, thin endpoint, failing acceptance tests) that follows our engineering rules. Use when starting a new feature endpoint from a user story.
argument-hint: <work-item-id> <FeatureName>
arguments: [id, feature]
disable-model-invocation: true
allowed-tools: Read, Grep, Glob, Bash(git branch *), Bash(dotnet build *), Bash(dotnet test *)
---
# New vertical slice for AB#$id: $feature

Current branch: !`git branch --show-current`

1. Read the story for work item $id in docs/stories/. If it has no tagged
   acceptance criteria (@AC-n), stop and ask for them. Apply the
   "When to stop and ask" rules in CLAUDE.md.
2. Create src/Ingestion.Api/Features/$feature/ following [slice-template.md](slice-template.md):
   - a pure core: static functions returning Result<T, TError>; no I/O, no clock
   - a thin endpoint that gathers inputs, calls the core, and maps every error to a status
3. Create tests/Ingestion.UnitTests/$feature/ with one named test per criterion,
   written first. They must fail for the right reason.
4. Run `dotnet build` and `dotnet test`, then report each failing test and why.

Stop there. Implementation starts after a human reviews the failing tests.

---
name: start-story
description: Starts work on an Azure Boards user story - reads it, resolves open questions, creates the branch, and writes failing acceptance tests. Use when beginning a new work item.
argument-hint: <work-item-id> <short-slug>
arguments: [id, slug]
disable-model-invocation: true
allowed-tools: Read, Grep, Glob, Bash(git status), Bash(git switch *), Bash(git pull *), Bash(dotnet build *), Bash(dotnet test *)
---
# Start work item AB#$id

Working tree: !`git status --short`

1. Stop if the working tree above is not clean, and say why.
2. Read the story for work item $id in docs/stories/ and any ADRs it references.
3. List every assumption you would need to make. For each one that affects behavior
   or architecture, ask using the "When to stop and ask" rules in CLAUDE.md.
   Do not continue until the blocking questions are answered.
4. Record answers: new behavior as tagged criteria (@AC-n) in the story,
   design decisions as a new ADR in docs/adr/.
5. Update main and create the branch: `git switch main`, `git pull --ff-only`,
   then `git switch -c feature/$id-$slug`.
6. Add one failing test per new criterion. Run `dotnet test` and confirm each fails
   for the right reason.

Stop there and summarize: questions asked, answers recorded, tests added.

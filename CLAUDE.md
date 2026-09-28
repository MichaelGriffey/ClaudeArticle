# Ingestion service

Telemetry ingestion microservice from the article "Mission-Critical Microservices with Claude Code".
Correctness, predictability, and fault tolerance take priority over feature velocity.

## Layout
- `src/Ingestion.Domain`: functional core. Pure functions and validated types only.
- `src/Ingestion.Api`: imperative shell. Vertical slices under `Features/`.
- `tests/`: unit (FsCheck properties), integration (in-memory host), acceptance (Reqnroll Gherkin).
- `docs/stories`: user stories and their tagged acceptance criteria (@AC-n). `docs/adr`: decisions.

## Commands
- Build: `dotnet build ClaudeArticle.slnx`
- Test: `dotnet test ClaudeArticle.slnx`
- Format gate: `dotnet format ClaudeArticle.slnx --verify-no-changes`
- Mutation gate: `dotnet tool restore`, then `dotnet stryker --break-at 80` in `tests/Ingestion.UnitTests`

## Engineering rules (non-negotiable)
- Domain logic is pure: no DateTime.Now, Guid.NewGuid, Random, I/O, or statics.
  Inject TimeProvider and ID generators at the shell.
- No exceptions for expected failures. Return Result<T, TError>.
- Validate at the boundary; the core only receives validated types.
  Check null, empty, NaN, Infinity, min/max values, and out-of-range enums.
- Every acceptance criterion (AC-n) maps to at least one named test.
- Never modify, skip, or delete an acceptance test without explicit approval.
- Nullable reference types enabled; warnings are errors.
- Never commit to main. One branch per work item: feature/<id>-<slug>.
- Commits follow Conventional Commits and reference the work item (AB#<id>).
- Do not mark a task done until `dotnet test` passes locally.

## When to stop and ask (never guess)
Ask before writing code when any of these is not stated in the story, an ADR, or existing code:
- Behavior on invalid, missing, duplicate, late, or out-of-order input
- Failure semantics: retry, give up, dead-letter, compensate, or fail closed
- Consistency, ordering, and idempotency guarantees
- Units, time zones, precision, rounding, and numeric limits
- Security: who may call this, what is logged, which data classification applies
- Architecture: a new dependency, project, layer, or pattern, or any change to a public contract
- Two or more reasonable designs with different trade-offs

How to ask:
- Ask all blocking questions at once, as multiple choice, recommended option first with its trade-off.
- Cite the evidence that made it ambiguous (file:line, story, ADR).
- Do not proceed past a blocking question. List non-blocking assumptions in the PR under "Assumptions".
- When answered, record it: behavior becomes an acceptance criterion in the story; design becomes an ADR in docs/adr/.

# Vertical slice template

Reference implementation: `src/Ingestion.Api/Features/Ingest/` and `src/Ingestion.Domain/`.
Match their shape. Decisions behind it: `docs/adr/0002` to `0007`.

## Core (src/Ingestion.Domain)
- Validated input types: private constructor, static `Create` returning
  `Result<T, ConfigurationError>`, get-only properties so `with` cannot bypass validation.
- One static, pure function per rule. Time arrives as a parameter, last, never beside another
  value of the same type; group related inputs into a value such as `Reading`.
- Expected failures are a closed error record per function: private base constructor, sealed
  nested cases, codes from `nameof`. A function returns only the errors it can produce.
- Values the story fixes are domain constants (like `FreshnessRequirements`); operational
  settings are configuration.
- Comment the acceptance criterion (AC-n) beside the line that satisfies it.

## Shell (src/Ingestion.Api/Features/<Feature>/)
- `Map<Feature>` extension: route plus `.RequireAuthorization(<policy>)`. No anonymous endpoints
  without an ADR.
- Handler parameters are explicit: `[FromRoute]`, `[FromBody]`, and an `[AsParameters]` record of
  `[FromServices]` collaborators. The return type is `Results<...>`, so OpenAPI documents it.
- Request types have nullable fields and a `Validate()` that returns `Result<T, errors>`; a missing
  or malformed field is a 400 naming the field, never a default value.
- Order: validate, load, call the core, persist, translate. No business decisions in the handler.
- Ports return outcomes as closed records, including outages; exceptions mean defects.
- Map every outcome in one switch; the default arm throws `UnreachableException`. Errors are
  problem details with a stable `code` (see `IngestProblems`).
- All dependency calls share one time budget on the injected `TimeProvider`; outages and
  timeouts map to 503 with `Retry-After`.
- Record logs and metrics from the returned result (see `IngestTelemetry`). Log IDs and codes,
  never values or tokens.

## Tests
- Unit: one theory per criterion, boundaries from the Gherkin examples, plus FsCheck properties
  for invariants (non-finite input, monotonicity, idempotency). Restate the story's numbers
  instead of reading the constants. The mutation gate must stay at 80% or better.
- Integration: `IngestionApi` host from `tests/Ingestion.Testing`; cover 401, 403, every problem
  code, outages, timeouts (advance the fake clock), and startup refusals.
- Acceptance: scenarios in the story's `.feature` under `docs/stories/`, linked into
  `tests/Ingestion.AcceptanceTests`. Tag scenarios that need the store, fault injection, or the
  clock `@in-process`; keep staging scenarios well clear of time boundaries.

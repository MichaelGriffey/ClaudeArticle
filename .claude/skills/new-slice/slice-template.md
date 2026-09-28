# Vertical slice template

Reference implementation: `src/Ingestion.Api/Features/Ingest/IngestEndpoint.cs` and
`src/Ingestion.Domain/RangeClassifier.cs`. Match their shape.

## Core (src/Ingestion.Domain)
- Validated input types: private constructor, static `Create` returning `Result<T, ReadingError>`,
  get-only properties so `with` cannot bypass validation.
- One static, pure function per rule. Time arrives as a parameter.
- Every expected failure is a sealed record under the error hierarchy.
- Comment the acceptance criterion (AC-n) beside the line that satisfies it.

## Shell (src/Ingestion.Api/Features/<Feature>/)
- `Map<Feature>` extension: route plus `.RequireAuthorization(<policy>)`. No anonymous endpoints.
- Handler parameters are explicit: `[FromRoute]`, `[FromBody]`, `[FromServices]`.
- Order: load, call the core, persist, translate. No business decisions in the handler.
- Map every error type to a status code in one switch; the default arm throws `UnreachableException`.
- Dependency outages map to 503 with `Retry-After`.

## Tests
- Unit: one theory per criterion, boundaries from the Gherkin examples, plus FsCheck properties
  for invariants (non-finite input, monotonicity, idempotency).
- Integration: `IngestionApi` host from `tests/Ingestion.Testing`; cover 401, 403, each error, and outages.
- Acceptance: scenarios in `tests/Ingestion.AcceptanceTests/Features`; tag store-dependent ones `@in-process`.

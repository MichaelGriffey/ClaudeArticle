# ADR 0007: Closed rejection types, the Reading value, and where each setting lives

- Status: Accepted
- Work item: AB#TBD (architecture hardening), AB#1234

## Context

- `ReadingError` was an open record hierarchy: any assembly could add a case, so the endpoint's
  error mapping needed a "cannot happen" arm. It also mixed setup errors (`InvalidLimits`,
  `InvalidPolicy`) with reading rejections, so `Classify` admitted errors it can never return.
  Error codes were string literals that could drift from the type names.
- `Classify(value, limits, observedAt, now, policy)` placed two `DateTimeOffset` parameters side by
  side; swapping them compiles and inverts stale and future.
- The freshness window and `Retry-After` were hard-coded in `Program.cs` and repeated in the
  OpenAPI description, the ADRs, and the story.

## Decision

- `ReadingRejection` (`NonFiniteValue`, `FutureTimestamp`, `StaleReading`) is what `Classify`
  returns; `ConfigurationError` (`InvalidLimits`, `InvalidPolicy`) is what the validated types'
  `Create` methods return. Both have a private base constructor, so only their nested cases can
  derive from them (C# records still expose a protected copy constructor, so this is a strong
  convention rather than a proof). Codes use `nameof`.
- `Reading(Value, ObservedAt)` groups the input; the signature is
  `Classify(Reading reading, Limits limits, FreshnessPolicy policy, DateTimeOffset now)`.
- **Requirement values live in code; operational values live in configuration.** The freshness
  window comes from AC-5, so `FreshnessRequirements.MaxAge` and `MaxSkew` are domain constants and
  changing them means changing the story. `Ingestion:RetryAfter` and `Ingestion:DependencyBudget`
  are operational and validated at startup. The OpenAPI description is built from the running
  values.

## Consequences

- The endpoint's rejection mapping covers exactly three cases.
- Unit tests restate the story's numbers instead of reading the constants, so changing a constant
  without changing the story fails a test.

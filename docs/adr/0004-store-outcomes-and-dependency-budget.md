# ADR 0004: Store outcomes are values, storage is opt-in, and dependencies have a budget

- Status: Accepted (supersedes the failure signal in [ADR 0001](0001-atomic-write-with-outbox.md));
  `AppendAsync` also takes the reading's audit entry since [ADR 0008](0008-audit-trail.md)
- Work item: AB#TBD (architecture hardening), AB#1234 (AC-3, AC-4, AC-8, AC-9)

## Context

Four problems shared one root, the `IReadingStore` contract:

- `AppendAsync` returned nothing, so the endpoint could not tell a retry from a conflict. A second
  request with the same key and a different value got 200 with its own classification while the
  first reading stayed stored.
- A store outage (AC-4, an expected and specified outcome) was an exception, contradicting the
  CLAUDE.md rule "No exceptions for expected failures".
- The in-memory store was registered in every environment and deployed to staging and production,
  where a restart loses readings already acknowledged with 200.
- Registry and store calls had no deadline; a hung database hung every request.

## Decision

- `AppendAsync` returns a closed `AppendOutcome`: `Inserted`, `Duplicate(StoredClassification)`,
  `Conflict`, or `Unavailable`. Duplicate means same sensor, timestamp, and value; the response
  carries the stored classification (AC-3). Same key with a different value is `Conflict` and
  maps to 409 `ConflictingReading` (AC-8). `Unavailable` maps to 503 with `Retry-After` (AC-4).
  Adapters translate their driver's transient failures into `Unavailable`; anything else is a
  defect and becomes a 500. `StoreUnavailableException` is removed.
- `Storage:Provider` selects the store. The only provider today is `InMemory`, and the service
  refuses to start when the setting is missing. Development and the test host set it; staging sets
  it until a durable adapter exists; production never does.
- Registry and store calls share one budget, `Ingestion:DependencyBudget` (2 seconds by default),
  measured with the injected `TimeProvider`. Exhausting it returns 503 `DependencyTimeout` with
  `Retry-After` (AC-9). A client disconnect is not a timeout and is not answered.
- `Ingestion:RetryAfter` (5 seconds by default) sets the header; both settings are validated at
  startup.

## Consequences

- Production deployments fail at startup until a durable store adapter exists. That is the
  intended, fail-closed outcome for "zero data loss".
- Each adapter must classify its driver's errors correctly; the adapter's tests prove it.
- Retries and a circuit breaker (`.claude/rules/api-security.md`) belong in the durable adapter.
- Tests: `IngestEndpointTests` (AC-3, AC-4, AC-8, AC-9) and `StartupTests`.

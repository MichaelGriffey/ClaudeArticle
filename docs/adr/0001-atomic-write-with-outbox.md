# ADR 0001: Write the reading and its event atomically through an outbox

- Status: Accepted; the failure signal (exception) is superseded by [ADR 0004](0004-store-outcomes-and-dependency-budget.md)
- Work item: AB#1234 (AC-4)

> **Superseded in part.** The atomic outbox write and the idempotency key still stand. The store no
> longer throws `StoreUnavailableException`; it returns `AppendOutcome.Unavailable`, and a same-key
> write with a different value returns `AppendOutcome.Conflict` (AC-8). See ADR 0004.

## Context

Claude raised this question while planning AB#1234: the event store can time out after the
reading row is written, and `IReadingStore` had no transactional contract.

Options considered:

- **A. One transaction via an outbox.** Return 503 on failure and rely on idempotent client retry.
  No partial state.
- **B. Return 202 and reconcile asynchronously.** Simpler for clients, but readers see eventual
  consistency and reconciliation becomes a new failure mode.
- **C. Distributed transaction across both stores.** Strong consistency, but new infrastructure
  and coupling to a coordinator.

## Decision

Option A. `IReadingStore.AppendAsync` writes the reading and its outbox event in one transaction
and is idempotent on (SensorId, ObservedAt). When the store is unreachable it throws
`StoreUnavailableException`, which the endpoint maps to 503 with `Retry-After: 5`.

## Consequences

- Clients must retry on 503; idempotency makes retries safe (AC-3).
- A relay publishes outbox rows; publishing is at-least-once, so consumers deduplicate.
- Tests: `An_unavailable_store_returns_503_with_retry_after_and_persists_nothing` and the
  `@AC-4` acceptance scenario.

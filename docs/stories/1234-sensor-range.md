# AB#1234: Out-of-range sensor reading detection

**As a** flight operations engineer
**I want** every sensor reading classified against its calibrated limits
**So that** anomalies are surfaced before they affect mission decisions

## Acceptance criteria

Executable form: [1234-sensor-range.feature](1234-sensor-range.feature), run by
`tests/Ingestion.AcceptanceTests`.

| Tag | Criterion |
| --- | --- |
| AC-1 | Limits are inclusive: a reading exactly on a limit is Nominal |
| AC-2 | A non-finite reading (NaN, ±Infinity) is rejected with `NonFiniteValue` and nothing is published |
| AC-3 | A duplicate reading (same sensor and timestamp) is idempotent: no second event |
| AC-4 | If the event store is unavailable, the API returns 503 with `Retry-After` and persists nothing |
| AC-5 | Readings older than 5 minutes or more than 2 seconds ahead of server time are rejected |

## Non-functional criteria

- p99 latency under 50 ms at 2,000 requests per second
- Zero data loss on pod termination
- Writes require a bearer token with the `readings:write` scope

## Decisions

- [ADR 0001](../adr/0001-atomic-write-with-outbox.md): reading and event are written in one transaction (AC-4)

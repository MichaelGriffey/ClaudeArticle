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
| AC-3 | A duplicate reading (same sensor, timestamp, and value) is idempotent: 200 with the stored classification, no second event |
| AC-4 | If the event store is unavailable, the API returns 503 with `Retry-After` and persists nothing |
| AC-5 | Readings older than 5 minutes or more than 2 seconds ahead of server time are rejected |
| AC-6 | A request without `value` or `observedAt` (missing or null) is rejected with 400 naming each missing field; nothing is persisted |
| AC-7 | An `observedAt` without `Z` or an explicit UTC offset is rejected with 400 naming `observedAt` |
| AC-8 | A reading with the same sensor and timestamp as a stored reading but a different value is rejected with 409 `ConflictingReading`; the stored reading is unchanged |
| AC-9 | If the sensor registry or event store does not answer within the dependency budget (2 seconds by default), the API returns 503 `DependencyTimeout` with `Retry-After` and persists nothing |
| AC-10 | Readings may arrive out of order: a reading older than one already stored for the same sensor is accepted when it is inside the freshness window |
| AC-11 | Value checks come before time checks: a non-finite reading outside the freshness window is rejected as `NonFiniteValue` |
| AC-12 | A reading for an unregistered sensor is rejected with 404 `SensorNotFound`; nothing is persisted |

## Non-functional criteria

| Criterion | Verified by |
| --- | --- |
| p99 latency under 50 ms at 2,000 requests per second | Azure Load Testing gate in the staging stage ([ADR 0006](../adr/0006-test-gates.md)) |
| Zero data loss on pod termination | 200 only after the store commits; no in-memory store without an explicit opt-in ([ADR 0004](../adr/0004-store-outcomes-and-dependency-budget.md)) |
| Writes require the `readings:write` scope (users) or the `Readings.Write` app role (services) | Integration tests for 401, 403, scopes, and roles ([ADR 0003](../adr/0003-authorization-scope-or-app-role.md)) |
| Every error response is an RFC 9457 problem details document | Integration tests per status code ([ADR 0002](../adr/0002-problem-details-error-contract.md)) |

## Open questions

- **Timestamp precision.** `observedAt` keeps 100-nanosecond precision today. A database column with
  microsecond precision could merge two distinct readings into one duplicate key. Decide with the
  database adapter work item, once the column type is known.

## Decisions

- [ADR 0001](../adr/0001-atomic-write-with-outbox.md): reading and event are written in one transaction (AC-4)
- [ADR 0002](../adr/0002-problem-details-error-contract.md): every error is RFC 9457 problem details, with a stable `code`
- [ADR 0003](../adr/0003-authorization-scope-or-app-role.md): writes accept the delegated scope or the app role
- [ADR 0004](../adr/0004-store-outcomes-and-dependency-budget.md): store outcomes are values; storage is opt-in; dependency budget (AC-3, AC-4, AC-8, AC-9)
- [ADR 0005](../adr/0005-observability.md): health probes, structured logs, and OpenTelemetry over OTLP
- [ADR 0006](../adr/0006-test-gates.md): Microsoft Testing Platform, Stryker's MTP runner, load testing, and acceptance margins
- [ADR 0007](../adr/0007-domain-model.md): closed rejection types, the `Reading` value, and where each setting lives

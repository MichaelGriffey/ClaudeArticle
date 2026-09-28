# ADR 0008: Audit every accepted reading in the same transaction, IDs only

- Status: Accepted (retention period is an assumption until compliance confirms it; see Consequences)
- Work item: AB#TBD (architecture hardening), AB#1234 (AC-13)

## Context

`.claude/rules/api-security.md` requires an audit event for every state change: who, what, when,
and a correlation ID. The service changes state in one way, by accepting a new reading, and emitted
no audit events. Load is 500 readings per second on average and 2,000 at peak, about 43 million
audit records a day, or 11 to 13 GB at 250 to 300 bytes each. Investigators must get answers within
2 minutes, security wants the events in the SIEM, and there are no data residency requirements.

Options considered for capturing the record:

- **A. Same transaction as the reading.** No accepted reading without its audit record, and no
  audit record for a write that rolled back.
- **B. OpenTelemetry logs routed to an audit store.** Smallest change, but best effort: a crash
  between the commit and the export loses the record silently, and log pipelines sample and drop.
- **C. Caller identity on the outbox business event.** Every consumer would receive caller IDs, and
  audit could not have its own retention or access control.

Options considered for the store: immutable Blob Storage, a Log Analytics workspace, Azure Data
Explorer, and Event Hubs with Capture to immutable Blob Storage.

## Decision

**Capture (option A).** `IReadingStore.AppendAsync` takes an `AuditEntry` and writes it in the same
transaction as the reading and its outbox event (ADR 0001), and only when the reading is inserted
(AC-13). Duplicates, conflicts, rejections, and outages write none; they are in logs and metrics
(ADR 0005). Denied calls are security events, not audit records.

| Field | Source |
| --- | --- |
| `Action` | `reading.accepted` |
| `SubjectId` | The token's `oid`, else `sub`: the user, or the service principal for app-only tokens |
| `ClientAppId`, `TenantId` | `azp` (else `appid`) and `tid` |
| `SensorId`, `ObservedAt` | The reading's key, not its value; the reading row holds the value |
| `RecordedAt` | Server time from the injected `TimeProvider` |
| `CorrelationId` | The W3C trace ID, the same ID as in problem details, logs, and traces |

IDs only: no names, UPNs, or email addresses, and no reading values. The write policy requires a
`sub` claim, so every accepted write has a subject to name; a token without one gets 403.

`AuditEntry.Id` is derived from the action and the reading's key. A reading key is accepted at most
once, so every redelivered copy of a record carries the same ID and readers drop duplicates by it.
No ID generator is needed.

**Store and retention.** The outbox relay publishes audit records, batched, to a dedicated Event
Hubs hub (Standard tier, auto-inflate). From there:

- **System of record: Event Hubs Capture to immutable Blob Storage.** A locked time-based
  immutability policy of 30 months (913 days) prevents changes and deletes by anyone, including
  account owners. Geo-zone-redundant storage (no residency constraint). Lifecycle rules move blobs
  to cool after 30 days, cold after 90 days, and archive after 12 months.
- **Search and SIEM: the Microsoft Sentinel workspace**, custom table `IngestionAudit_CL`,
  ingested from the hub by a data collection rule (or by the relay through the Logs Ingestion API),
  with 12 months of interactive retention. Investigations of the last 12 months are KQL queries;
  detection rules watch for callers writing to unfamiliar sensors, unusual write rates, or unknown
  tenants and client apps. Older records are read from the archive, which takes hours.

The 30 months follow OMB M-21-31 (12 months active, 18 months cold). No regime was named; this is a
concrete, recognized baseline and an adequate organization-defined period for NIST SP 800-171
3.3.1, which the rules' mention of CUI suggests applies.

**Access.** The relay's identity can only send to the hub. Capture writes with the namespace's
managed identity. Auditors read the container and the table through their own role. No identity
can delete within retention.

## Consequences

- **Confirm retention before locking.** A locked immutability policy can be extended but never
  shortened. Use an unlocked policy in staging and lock production only after compliance confirms
  the period.
- **Two-minute freshness is a target, not a guarantee.** Log Analytics documents typical ingestion
  latency of 20 seconds to 3 minutes. The relay reports its lag, and an alert fires when a record's
  time from `RecordedAt` to searchable exceeds 2 minutes at the 95th percentile. If that alert fires
  regularly, add Azure Data Explorer with streaming ingestion as the search path.
- **Cost is driven by the Sentinel tier.** About 12 GB a day into an analytics-tier workspace is the
  largest cost; archived blobs are small by comparison. A commitment tier, or sending full records
  to a cheaper tier with per-caller summaries in the analytics tier, reduces it.
- **The infrastructure arrives with the durable store.** The database adapter work item adds the
  audit table, the relay, the hub, Capture, the container policy, and the data collection rule.
  Until then the in-memory store keeps each audit entry in the same row as its reading, so both are
  lost together on restart, as its opt-in accepts (ADR 0004).
- `IReadingStore` changes (ADR 0004 updated), and the write policy gains a `sub` requirement
  (ADR 0003 updated).
- Tests: `AuditTests` and `InMemoryReadingStoreTests` in `tests/Ingestion.IntegrationTests`, and the
  `@AC-13` acceptance scenario.

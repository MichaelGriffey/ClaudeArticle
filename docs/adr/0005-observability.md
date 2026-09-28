# ADR 0005: Health probes, structured logs, and OpenTelemetry over OTLP

- Status: Accepted
- Work item: AB#TBD (architecture hardening)

## Context

The service emitted no logs, metrics, traces, or health endpoints. The production environment's
health checks and the canary's error-rate and p99 alerts had nothing to observe, and rejections,
conflicts, and outages were invisible. The organization already runs an OTLP collector (see
`managed/managed-settings.json`).

## Decision

- **Health.** `/health/live` (process is up, no checks) and `/health/ready` (checks tagged `ready`,
  including the store). Probes are anonymous, because platform health checks carry no token. This
  ADR is the exception `.claude/rules/api-security.md` requires for an anonymous endpoint. The probes
  return only a status word, never configuration or dependency details.
- **Logs.** Source-generated `LoggerMessage` methods for rejections (Debug; counted by metrics),
  unknown sensors and conflicts (Warning), and timeouts and outages (Error). Logs carry the sensor
  ID and error code, never reading values or tokens. The trace ID comes from the logging scope.
- **Metrics and traces.** OpenTelemetry with ASP.NET Core instrumentation and the `Ingestion.Api`
  meter: `ingestion.readings.accepted` (by classification and outcome), `ingestion.readings.rejected`
  (by code), and `ingestion.store.append.duration`. The OTLP exporter is enabled only when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set, so development and tests export nothing.

## Consequences

- New packages: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol` (Apache-2.0). They pass the vulnerability audit.
- Each environment sets `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_SERVICE_NAME`; the collector
  forwards to Azure Monitor, where the canary alerts live.
- Tests: `HealthTests` in `tests/Ingestion.IntegrationTests`.

# ADR 0002: Every error is RFC 9457 problem details with a stable code

- Status: Accepted
- Work item: AB#TBD (architecture hardening), AB#1234

## Context

Only the domain's 422 rejections returned problem details. Framework responses (400, 401, 403, 404,
415) had empty bodies although the OpenAPI document promised a problem body for 400, and an
unexpected exception produced an empty 500. The 422 bodies used `title` as the machine-readable
code, while RFC 9457 defines `title` as a human-readable summary and `type` as the identifier. The
age and skew the classifier computes were discarded. `.claude/rules/api-security.md` requires
problem details and forbids returning exception messages.

Options considered:

- **A. Problem details everywhere, additive fields.** Exception handler and status code pages for
  framework errors; errors this service decides gain `type`, `code`, `detail`, and data members,
  and keep `title` equal to the code so existing clients do not break.
- **B. Full RFC 9457 now.** Human `title`, code only in `type`/`code`. Correct, but breaks clients
  that read `title`.
- **C. Leave as is.**

## Decision

Option A.

- `UseExceptionHandler` and `UseStatusCodePages` with `AddProblemDetails`: every 4xx and 5xx is
  `application/problem+json` with a `traceId`. Unexpected exceptions become a 500 without exception
  text and are logged on the server.
- Errors the service decides carry `code` (stable, PascalCase) and a `type` URL of the form
  `https://docs.contoso.com/ingestion/errors/<kebab-code>` (placeholder base). `title` equals `code`
  for compatibility; clients should read `code`.
- Rejections carry their numbers: `ageSeconds`/`maxAgeSeconds` and `skewSeconds`/`maxSkewSeconds`.
- Codes: `InvalidRequest` (400), `SensorNotFound` (404), `ConflictingReading` (409),
  `NonFiniteValue`, `StaleReading`, `FutureTimestamp` (422), `EventStoreUnavailable`,
  `DependencyTimeout` (503). Framework errors (401, 403, 415, malformed JSON, 500) carry the
  RFC 9110 `type` and no `code`.
- Problem bodies never echo request values or tokens.

## Consequences

- The OpenAPI document matches the wire for every documented status.
- `title` can move to human text later as a separate, announced breaking change.
- Tests: the error-contract tests in `tests/Ingestion.IntegrationTests/ErrorContractTests.cs`.

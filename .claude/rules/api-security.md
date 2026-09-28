---
paths:
  - "src/**/Features/**/*.cs"
---
# API security and compliance rules
- Every endpoint requires authorization through a named policy. No anonymous
  endpoint without an ADR.
- Never log request bodies, tokens, or fields classified PII or CUI. Log IDs, not values.
- Validate and bound all input at the boundary: sizes, ranges, formats, enums.
- Errors are RFC 9457 problem details. Never return exception messages or stack traces.
- Outbound calls have explicit timeouts, jittered retries, and a circuit breaker.
- Every state change emits an audit event: who, what, when, correlation ID.
- New packages need an approved license and a clean vulnerability audit.

# ClaudeArticle: mission-critical ingestion service

Companion code for the article **Mission-Critical Microservices with Claude Code**. A .NET 10
microservice that classifies sensor readings against calibrated limits, built with a pure
functional core, layered test batteries, an Azure DevOps pipeline, and Claude Code guardrails.

## Prerequisites

- .NET SDK 10.0.100 or later (see `global.json`)
- Access to nuget.org (see `nuget.config`)

## Build and test

```powershell
dotnet build ClaudeArticle.slnx
dotnet test ClaudeArticle.slnx
dotnet format ClaudeArticle.slnx --verify-no-changes
```

Mutation testing (the pipeline's 80% gate):

```powershell
dotnet tool restore
cd tests/Ingestion.UnitTests
dotnet stryker --break-at 80
```

The first restore creates a `packages.lock.json` per project. Commit them: CI restores with
`--locked-mode` and fails if they are missing or stale.

The commit hook decides which Bash commands are commits with `.claude/hooks/runs-git-commit.awk`. Its
tests run in the pipeline and locally with Git Bash (on Windows) or any POSIX shell:

```bash
bash .claude/hooks/runs-git-commit.test.sh
```

## Run the API and explore it in Swagger UI

```powershell
dotnet watch --project src/Ingestion.Api
```

`dotnet watch`, Visual Studio (F5), and Rider open the browser at Swagger UI
(`http://localhost:5099/swagger`). With plain `dotnet run`, browse to `http://localhost:5099`; the root
redirects to Swagger UI. The OpenAPI document is at `http://localhost:5099/openapi/v1.json`.

The document and the UI exist only in the Development environment; other environments expose neither.
`appsettings.Development.json` opts in to the in-memory store; without that opt-in the service refuses
to start (see [Configuration](#configuration)).

### Call the secured endpoint from Swagger UI

Writes require a bearer token with the `readings:write` scope (people) or the `Readings.Write` app role
(services). Scopes are read from `scp` or `scope`, space-separated, the way Microsoft Entra ID and
`dotnet user-jwts` issue them. For local development, create a signed dev token:

```powershell
cd src/Ingestion.Api
dotnet user-jwts create --scope "readings:write"
```

`user-jwts` stores the signing key in user secrets, adds the dev issuer to `appsettings.Development.json`,
and prints a token. Restart the API, then in Swagger UI:

1. Select **Authorize**, paste the token (without the `Bearer ` prefix), and select **Authorize**.
2. Expand **POST /sensors/{sensorId}/readings** and select **Try it out**.
3. Enter `TMP-07` as `sensorId` and set `value` to `125.0`. The example body's `observedAt` is already
   the current UTC time; readings older than 5 minutes or more than 2 seconds ahead are rejected with 422.
   `observedAt` must end in `Z` or a UTC offset such as `-05:00`.
4. Select **Execute**. Try `"NaN"` (quoted) for 422 `NonFiniteValue`, remove `value` for 400, send the
   same timestamp with another value for 409, or remove the token for 401.

The token is remembered across page reloads. Swagger UI shows 401 and 403 on the operation because the
`BearerSecurityOperationTransformer` documents security for every endpoint that requires authorization.

Every error is RFC 9457 problem details (ADR 0002). Errors the service decides carry a stable `code`:

| Status | `code` | When |
| --- | --- | --- |
| 400 | `InvalidRequest` | A field is missing or null, or `observedAt` has no UTC offset (`errors` names each field) |
| 404 | `SensorNotFound` | The sensor is not registered |
| 409 | `ConflictingReading` | A different value is already stored for this sensor and timestamp |
| 422 | `NonFiniteValue`, `StaleReading`, `FutureTimestamp` | The reading is rejected; stale and future carry `ageSeconds` or `skewSeconds` and the limit |
| 503 | `EventStoreUnavailable`, `DependencyTimeout` | Retry after the `Retry-After` interval; retries are idempotent |

`title` repeats the code for older clients; read `code`.

From PowerShell instead:

```powershell
$token = "<token from user-jwts>"
$body = @{ value = 125.0; observedAt = (Get-Date).ToUniversalTime().ToString("o") } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5099/sensors/TMP-07/readings `
  -Headers @{ Authorization = "Bearer $token" } -ContentType application/json -Body $body
```

## Configuration

| Setting | Where | Notes |
| --- | --- | --- |
| `Storage:Provider` | Environment | `InMemory` is the only store today and loses readings on restart. Without the setting the service refuses to start, so production cannot run until a durable adapter exists (ADR 0004). Development and staging opt in. |
| `Ingestion:RetryAfter`, `Ingestion:DependencyBudget` | `appsettings.json` | Operational values, validated at startup (5 s and 2 s). The freshness window is not a setting: it is AC-5, in `FreshnessRequirements`. |
| `Sensors` | `appsettings.json` | Each entry needs `Id`, `Lower`, and `Upper`. Missing limits, misspelled keys, duplicate IDs, or an empty list stop startup. |
| `Authentication:Schemes:Bearer` | Environment | `Authority` (or signing keys) and `ValidAudiences`. Outside Development, missing values stop startup (ADR 0003). |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Environment | Enables OTLP export of logs, metrics, and traces (ADR 0005). Unset means nothing is exported. |

## Operations

- `GET /health/live` and `GET /health/ready` answer anonymously with a status word only (ADR 0005).
- Meter `Ingestion.Api`: `ingestion.readings.accepted` (by classification), `ingestion.readings.rejected`
  (by code), and `ingestion.store.append.duration` (by outcome).
- Logs carry the sensor ID and error code, never reading values or tokens.
- Each accepted reading has one audit record: the caller's token IDs (`oid`/`sub`, `azp`, `tid`), the
  action, the sensor and observation time, the server time, and the trace ID (ADR 0008). Tokens without
  a `sub` claim cannot write.

## Where each article section lives

| Article section | Files |
| --- | --- |
| 1. Teach Claude to ask before it assumes | `CLAUDE.md` (When to stop and ask), `docs/adr/` |
| 2. Planning: user stories | `docs/stories/1234-sensor-range.md`, `docs/stories/1234-sensor-range.feature` |
| 3. Design: functional core | `src/Ingestion.Domain`, `CLAUDE.md` (Engineering rules), `docs/adr/0007-domain-model.md` |
| 4. Skills and enterprise guardrails | `.claude/skills`, `.claude/rules`, `.claude/settings.json`, `.claude/hooks`, `managed/` |
| 5. Branches and check-ins | `.claude/settings.json`, `.claude/hooks/verify-before-commit.sh`, `.claude/skills/start-story` |
| 6. Code that is functionally complete | `src/Ingestion.Domain/*.cs`, `src/Ingestion.Api/Features/Ingest/`, `src/Ingestion.Api/Security/`, `src/Ingestion.Api/Infrastructure/`, `src/Ingestion.Api/Program.cs`, `src/Ingestion.Api/OpenApi/` |
| 7. Test batteries | `tests/Ingestion.UnitTests`, `tests/Ingestion.IntegrationTests`, `tests/Ingestion.AcceptanceTests`, `tests/load` |
| 8. Azure DevOps pipelines | `azure-pipelines.yml`, `pipeline-templates/` (example of the pinned template repository) |
| 9. Code reviews | `.claude/agents/reviewer.md` |

## Test suites

| Project | What it proves | Runs against |
| --- | --- | --- |
| `Ingestion.UnitTests` | Every rule and boundary in the core; FsCheck properties for NaN and monotonicity; mutation score 100% | Pure functions |
| `Ingestion.IntegrationTests` | Validation, problem details for every status, authorization by scope and app role, idempotency and conflicts, the dependency budget, the audit trail, startup refusals, health and metrics, OpenAPI document and Swagger UI | In-memory host (`Ingestion.Testing`) |
| `Ingestion.AcceptanceTests` | The Gherkin story (`docs/stories/`, linked in), scenario by scenario | In-memory host, or a deployed environment when `INGESTION_BASE_URL` is set |
| `tests/load` | p99 under 50 ms at 2,000 requests per second | Staging, through Azure Load Testing |

Scenarios tagged `@in-process` inject faults, move the clock, or inspect the store, so staging runs
skip them: `dotnet test --project tests/Ingestion.AcceptanceTests --filter-not-trait "Category=in-process"`,
or, from the build output the pipeline promotes, `dotnet Ingestion.AcceptanceTests.dll -trait- "Category=in-process"`.

All suites run on Microsoft Testing Platform (opted in through `global.json`). Stryker needs it: its
default VSTest runner cannot activate mutants in xUnit v3 test processes and scores every mutant as
survived. `tests/Ingestion.UnitTests/stryker-config.json` selects the MTP runner, so the command stays
`dotnet stryker --break-at 80`.

## Differences from the article

- The article's test-battery table lists Testcontainers, contract, and resilience tests. The
  integration tests here use the in-memory host with a fake store; a durable store adapter would add
  Testcontainers-based tests against the real database.
- `pipeline-templates/` shows what the separate, tag-pinned template repository contains. Move it to
  its own repository, release it as `v3.3.0`, and keep `resources.repositories` in
  `azure-pipelines.yml` pointing at that tag.
- `managed/managed-settings.json` is an example for IT to deploy. Claude Code does not read it from
  the repository.
- The commit hook is a bash script. On Windows, Claude Code runs it with Git Bash.
- The article's composition-root sample stops at the adapters; `Program.cs` also registers
  observability, OpenAPI, Swagger UI for local development, and the middleware.

## Known gaps

- **No durable store.** Production refuses to start by design until a database adapter with an outbox
  exists (ADR 0004). That work item also decides timestamp precision (see the story's open questions).
- **Audit delivery.** Every accepted reading already produces an audit record in the same write as the
  reading (ADR 0008, AC-13). Shipping records onward (outbox relay, Event Hubs, immutable Blob Storage
  for 30 months, the Sentinel workspace for 2-minute search) arrives with the durable store. Confirm
  the 30-month retention with compliance before locking the immutability policy: a locked policy can
  be extended but never shortened.
- **Placeholders.** Commits reference `AB#TBD`, and error `type` URLs use
  `https://docs.contoso.com/ingestion/errors/` until a docs site exists.

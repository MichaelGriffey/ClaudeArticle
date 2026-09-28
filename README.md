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

## Run the API and explore it in Swagger UI

```powershell
dotnet watch --project src/Ingestion.Api
```

`dotnet watch`, Visual Studio (F5), and Rider open the browser at Swagger UI
(`http://localhost:5099/swagger`). With plain `dotnet run`, browse to `http://localhost:5099`; the root
redirects to Swagger UI. The OpenAPI document is at `http://localhost:5099/openapi/v1.json`.

The document and the UI exist only in the Development environment; other environments expose neither.

### Call the secured endpoint from Swagger UI

Writes require a bearer token with the `readings:write` scope. With no authentication configuration the
service rejects every token (it fails closed). For local development, create a signed dev token:

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
4. Select **Execute**. Try `"NaN"` (quoted) for 422 `NonFiniteValue`, or remove the token for 401.

The token is remembered across page reloads. Swagger UI shows 401 and 403 on the operation because the
`BearerSecurityOperationTransformer` documents security for every endpoint that requires authorization.

From PowerShell instead:

```powershell
$token = "<token from user-jwts>"
$body = @{ value = 125.0; observedAt = (Get-Date).ToUniversalTime().ToString("o") } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5099/sensors/TMP-07/readings `
  -Headers @{ Authorization = "Bearer $token" } -ContentType application/json -Body $body
```

## Where each article section lives

| Article section | Files |
| --- | --- |
| 1. Teach Claude to ask before it assumes | `CLAUDE.md` (When to stop and ask), `docs/adr/0001-atomic-write-with-outbox.md` |
| 2. Planning: user stories | `docs/stories/1234-sensor-range.md`, `docs/stories/1234-sensor-range.feature` |
| 3. Design: functional core | `src/Ingestion.Domain`, `CLAUDE.md` (Engineering rules) |
| 4. Skills and enterprise guardrails | `.claude/skills`, `.claude/rules`, `.claude/settings.json`, `.claude/hooks`, `managed/` |
| 5. Branches and check-ins | `.claude/settings.json`, `.claude/hooks/verify-before-commit.sh`, `.claude/skills/start-story` |
| 6. Code that is functionally complete | `src/Ingestion.Domain/*.cs`, `src/Ingestion.Api/Features/Ingest/IngestEndpoint.cs`, `src/Ingestion.Api/Program.cs`, `src/Ingestion.Api/OpenApi/` |
| 7. Test batteries | `tests/Ingestion.UnitTests`, `tests/Ingestion.IntegrationTests`, `tests/Ingestion.AcceptanceTests` |
| 8. Azure DevOps pipelines | `azure-pipelines.yml`, `pipeline-templates/` (example of the pinned template repository) |
| 9. Code reviews | `.claude/agents/reviewer.md` |

## Test suites

| Project | What it proves | Runs against |
| --- | --- | --- |
| `Ingestion.UnitTests` | Every rule and boundary in the core; FsCheck properties for NaN and monotonicity | Pure functions |
| `Ingestion.IntegrationTests` | Routing, 401/403, JSON edge cases, 404/422/503, idempotency, OpenAPI document and Swagger UI | In-memory host (`Ingestion.Testing`) |
| `Ingestion.AcceptanceTests` | The Gherkin story, scenario by scenario | In-memory host, or a deployed environment when `INGESTION_BASE_URL` is set |

Scenarios tagged `@in-process` inject faults or inspect the store, so the staging run filters them
out with `--filter-not-trait "Category=in-process"`.

All suites run on Microsoft Testing Platform (opted in through `global.json`). Stryker needs it: its
default VSTest runner cannot activate mutants in xUnit v3 test processes and scores every mutant as
survived. `tests/Ingestion.UnitTests/stryker-config.json` selects the MTP runner, so the command stays
`dotnet stryker --break-at 80`.

## Differences from the article

- The pipeline's integration step also runs the acceptance suite in-process on every PR, and the
  staging acceptance step passes the tag filter and an access token.
- The integration tests use the in-memory host with a fake store. A real database adapter would add
  Testcontainers-based tests against the actual store.
- `pipeline-templates/` shows what the separate, tag-pinned template repository contains. Move it to
  its own repository and update `resources.repositories` in `azure-pipelines.yml`.
- `managed/managed-settings.json` is an example for IT to deploy. Claude Code does not read it from
  the repository.
- The commit hook is a bash script. On Windows, Claude Code runs it with Git Bash.
- The endpoint carries OpenAPI metadata (summary, tags, documented responses) and the API adds Swagger UI
  for local development. The article's code samples omit these for brevity.

# ADR 0006: Test gates that actually test

- Status: Accepted
- Work item: AB#TBD (architecture hardening)

## Context

- The mutation gate had never worked. Stryker's VSTest runner cannot activate mutants inside
  xUnit v3 test processes: every mutant survived and the score was 0%, with Stryker 4.14 and 5.0.
- The p99 criterion (50 ms at 2,000 requests per second) had no test.
- Against staging, the AC-5 scenarios tested a 1-second margin across two clocks plus network
  latency, and AC-1 failed whenever the test agent's clock ran more than 2 seconds ahead.
- The story's Gherkin file existed twice, and the generated `.feature.cs` was committed.

## Decision

- **Microsoft Testing Platform.** `global.json` opts `dotnet test` into MTP and every suite uses
  the MTP-enabled `xunit.v3` package. `tests/Ingestion.UnitTests/stryker-config.json` selects
  Stryker's MTP runner (preview in Stryker 5), so `dotnet stryker --break-at 80` measures real
  kills. Coverage comes from `Microsoft.Testing.Extensions.CodeCoverage`; `coverlet.collector` is a
  VSTest data collector and is removed.
- **Load test.** Azure Load Testing runs `tests/load/locustfile.py` in the staging stage and fails
  the run when p99 exceeds 50 ms or errors exceed 1%.
- **Acceptance margins.** Exact freshness boundaries stay in unit, integration, and in-process
  acceptance scenarios (`@in-process`). Scenarios that also run against staging use readings far
  outside the window (10 minutes old, 60 seconds ahead) and readings timestamped 30 seconds in the
  past when time is not under test.
- **One Gherkin file.** `docs/stories/1234-sensor-range.feature` is linked into the acceptance test
  project; generated code-behind is ignored by git.
- **Staging runs the promoted bits.** The Build stage ships the acceptance suite's Release build
  output as an artifact, and the staging job runs it directly with xUnit's own options
  (`-trait- "Category=in-process"`). `dotnet publish` is not used for the test project: it copies
  netstandard assemblies from the test SDK that redefine `IAsyncDisposable` and break xUnit
  discovery with a `TypeLoadException`.

## Consequences

- Stryker's MTP runner is in preview; the pipeline records the score and the README documents the
  runner choice.
- Visual Studio Test Explorer and Live Unit Testing keep working through `Microsoft.NET.Test.Sdk`
  and `xunit.runner.visualstudio`.
- Azure Load Testing needs a resource and a service connection (pipeline variables).

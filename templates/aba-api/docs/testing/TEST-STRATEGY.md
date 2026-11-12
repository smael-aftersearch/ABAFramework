# Required tests in the generated project

## Implemented tests

- `tests/Aba.Starter.Api.Tests/ApiSmokeTests.cs`: generated OpenAPI, Scalar documentation route, health and correlation header using WebApplicationFactory.
- `tests/Aba.Starter.Api.Tests/SecurityGuardTests.cs`: required OIDC config for non-development runtime, explicit local exception.

## Commands

```bash
dotnet restore Aba.Starter.slnx
dotnet build Aba.Starter.slnx -c Release --no-restore
dotnet test Aba.Starter.slnx -c Release --no-build
```

## Optional real-service integration suite

A separate **integration test project outside the default solution** lives in `tests/Aba.Starter.Platform.IntegrationTests`. It contains actual PostgreSQL, SQL Server, Redis and Wolverine tests. Running ordinary `dotnet test Aba.Starter.slnx` stays fast and needs no external services; running the dedicated integration test project **requires all external connections** and fails closed if any are absent, rather than falsely recording a PASS. Set `ABA_TEST_POSTGRESQL`, `ABA_TEST_SQLSERVER` and `ABA_TEST_REDIS`, then execute `dotnet test tests/Aba.Starter.Platform.IntegrationTests/Aba.Starter.Platform.IntegrationTests.csproj`. Repo-level `scripts/verify-platform-services.sh` builds and tests this isolated suite on a fresh generated API and CI provisions actual containers.

## New changes

Each feature needs relevant success, negative and invariant tests, and updated API/contract docs. For a new context, implement unit and integration tests before adding broader end-to-end tests. Add real PostgreSQL, Redis, Wolverine, tenant isolation and provider failure tests only when their adapters exist; never claim they already run.

## Tenant identity baseline

`AbaPlatformTests` verifies tenant claim extraction requires an authenticated principal, rejects missing/malformed values, keeps tenancy opt-in, and evaluates the default policy. This does **not** demonstrate OIDC signature verification against an actual identity provider nor database row isolation. Add those end-to-end tests before enabling real multitenant storage.

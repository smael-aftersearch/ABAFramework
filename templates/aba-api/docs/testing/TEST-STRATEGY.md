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

The generated project additionally includes `AbaExternalInfrastructureTests.cs`, verifying schema migration, unique idempotency claims and Redis scope/TTL/invalidation against **real service connections**. Set `ABA_TEST_POSTGRESQL`, `ABA_TEST_SQLSERVER`, `ABA_TEST_REDIS` to run all three. A missing env var leaves that case unexecuted; **offline test totals must not be described as real database proof**. The repo-level `scripts/verify-platform-services.sh` refuses missing env vars and executes these tests on a fresh generated API. GitHub Actions provisions actual isolated service containers.

## New changes

Each feature needs relevant success, negative and invariant tests, and updated API/contract docs. For a new context, implement unit and integration tests before adding broader end-to-end tests. Add real PostgreSQL, Redis, Wolverine, tenant isolation and provider failure tests only when their adapters exist; never claim they already run.

## Tenant identity baseline

`AbaPlatformTests` verifies tenant claim extraction requires an authenticated principal, rejects missing/malformed values, keeps tenancy opt-in, and evaluates the default policy. This does **not** demonstrate OIDC signature verification against an actual identity provider nor database row isolation. Add those end-to-end tests before enabling real multitenant storage.

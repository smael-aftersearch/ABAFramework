# Required tests in the generated project

## Implemented tests

- `tests/Aba.Starter.Api.Tests/ApiSmokeTests.cs`: generated OpenAPI, Scalar documentation route, health and correlation header using WebApplicationFactory.
- `tests/Aba.Starter.Api.Tests/SecurityGuardTests.cs`: required OIDC config for non-development runtime, explicit local exception.
- `tests/Aba.Starter.Api.Tests/SignedJwtAuthenticationTests.cs`: uses real ASP.NET JWT bearer validation for a signed valid token, invalid signature, incorrect issuer/audience, expired and malformed tokens. Uses a disposable test key; no false claims of a real IdP.

## Commands

```bash
dotnet restore Aba.Starter.slnx
dotnet build Aba.Starter.slnx -c Release --no-restore
dotnet test Aba.Starter.slnx -c Release --no-build
```

## Optional real-service integration suite

A separate **integration test project outside the default solution** lives in `tests/Aba.Starter.Platform.IntegrationTests`. It contains actual PostgreSQL, SQL Server, Redis and Wolverine tests. Running ordinary `dotnet test Aba.Starter.slnx` stays fast and needs no external services; running the dedicated integration test project **requires all external connections** and fails closed if any are absent, rather than falsely recording a PASS. Set `ABA_TEST_POSTGRESQL`, `ABA_TEST_SQLSERVER` and `ABA_TEST_REDIS`, then execute `dotnet test tests/Aba.Starter.Platform.IntegrationTests/Aba.Starter.Platform.IntegrationTests.csproj`. Repo-level `scripts/verify-platform-services.sh` builds and tests this isolated suite on a fresh generated API and CI provisions actual containers.

## Transactional Outbox acceptance gate

The real-service integration suite invokes `AbaTransactionalOutboxProbe` against **both** database engines and checks:
- success: one EF row and one downstream handler receipt;
- injected fault after publishing, before commit: neither row nor downstream receipt exists;
- default domain-neutral quick start remains external-service-free.

This is stronger than a connectivity test, but it is still not a crash/restart or broker outage test.

## New changes

Each feature needs relevant success, negative and invariant tests, and updated API/contract docs. For a new context, implement unit and integration tests before adding broader end-to-end tests. Add real PostgreSQL, Redis, Wolverine, tenant isolation and provider failure tests only when their adapters exist; never claim they already run.

## Tenant identity baseline

`AbaPlatformTests` verifies tenant claim extraction requires an authenticated principal, rejects missing/malformed values, keeps tenancy opt-in, and evaluates the default policy. This does **not** demonstrate OIDC signature verification against an actual identity provider nor database row isolation. Add those end-to-end tests before enabling real multitenant storage.

## Tenant persistence negative cases

`TenantPersistenceTests.cs` exercises `AbaTenantDbContext` with relational SQLite: per-instance query filtering, automatic owner stamping, malicious `IgnoreQueryFilters` mutation, forged tenant insert and missing authenticated tenant. These are application-level unit/integration tests; independent PostgreSQL and SQL Server store-level tenant tests and RLS are still required for a public multi-tenant service.

## Restart-and-recover for durable scheduled messages

The external integration suite includes `DurabilityRecoveryTests`: with PostgreSQL and SQL Server, a scheduled technical Wolverine message must survive a clean host shutdown and be processed by a newly started host using the same relational store. Do not confuse this with process-kill fault-injection, poison/dead-letter replay or external broker tests, which still need separate evidence.

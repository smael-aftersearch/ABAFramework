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

## Same-transaction idempotency

`IdempotencyReceiptRollsBackWithSameDbContextWork` runs on **both** SQL engines and asserts that an exception rolls back both the technical receipt and same-DbContext state, a successful transaction persists both, and a duplicate callback does not invoke work. This intentionally does not promise exactly-once semantics for external providers or multiple independent DbContexts.

## Executable generated-context governance

`python3 tools/new_context.py check` inspects declared ownership, owner-local metadata, all project references and pure Domain/Contracts imports. The root CI script scaffolds two temporary contexts, verifies successful build/xUnit tests, deliberately injects a cross-context Infrastructure import into Domain, deliberately injects EF Core into Domain, and confirms that each illegal change is **rejected**, then restores the fixture before compilation. Static checks are a guardrail, not a substitute for architecture/IL/behavior tests.

## Real dual-engine tenant defense test

`TenantDatabaseBoundaryTests` runs in the **external integration suite** against actual PostgreSQL and Microsoft SQL Server, asserting scope-based read filtering, automatic tenant ownership stamping, blocked `IgnoreQueryFilters` cross-tenant update, rejected forged insert, and rejected anonymous write. It never claims database RLS or defense against raw SQL/`ExecuteUpdate` bypass.

The tenant multi-engine fixture now **provisions its own EF platform database first**, rather than assuming migration tests run before it. External integration suites disable xUnit class parallelization to eliminate schema/bootstrap races against a shared CI database. These are test isolation controls, not production migration shortcuts.

## Real Identity Provider and HTTP metadata guard

Fast tests require HTTPS discovery by default even in Development, allow an explicit test-only local HTTP discovery opt-in, and prove that Production ignores that flag. An independent Keycloak CI job exercises public OIDC discovery and bearer JWT response codes. No real production tenant memberships or login UI are bundled.

## Optional per-context database scaffolding

The generator supports `--with-db` to add an empty owner-specific Infrastructure DbContext and dual-provider selector. CI generates a second context in this mode and proves an xUnit test can resolve both EF Core providers without a database; provider integration and business migrations must be tested independently when actual persistence is added.

## Read-through cache outage behavior

`ReadThroughCacheTests` checks default fail-closed on cache outage, explicit fresh authoritative-source fallback on read/write Redis timeouts, never swallowing loader exceptions, rejecting unsafe cache scopes before loading, honoring cancellation and avoiding source reads on cache hits. These deterministic fault-injection tests run with the default generated API solution; a separate live Redis shutdown/reconnect test remains a release gate.

## Real concurrent callback claims

`ConcurrentSameKeyClaimsHaveOneWinnerAndNoDuplicateWork` starts four concurrent isolated EF scopes with the same idempotency key on both PostgreSQL and SQL Server. Acceptance: exactly one transaction commits the receipt and its associated technical row, the other three are duplicates, and the database contains exactly one of each. This exercises actual unique constraints and transaction overlap, not a single-threaded repeat. A failure must trigger a code fix, not removal of the assertion.

## Configurable rate-limiting and health resilience

`RateLimitTests` verifies invalid limits fail configuration, JWT subject/tenant isolation is unaffected by untrusted tenant headers, anonymous IP partitioning ignores forwarded spoofing, and a tiny 2-request quota returns 429 on the next API request while Health remains available. Production multi-instance quotas still require gateway or distributed rate enforcement.

## Redis driver failure with actual network outage

`ActualRedisClientCanBypassUnreachableServerForAuthorizedQueriesOnly` uses the real `StackExchangeRedisCache` adapter pointed at an intentionally unreachable loopback TCP port. With explicit `UseAuthoritativeSource` a non-authoritative read-through query must call its source exactly once. The identical real Redis failure in default `FailClosed` must propagate and **must not invoke the source**. The suite does not yet prove recovery after a previously connected live Redis service restarts.

## Database transport security baseline

`AbaPlatformTests.ProductionDatabaseRequiresVerifiedEncryptedTransport` checks fail-closed behavior for insecure PostgreSQL and SQL Server connections, including encrypted-without-identity-verification cases, and accepts PostgreSQL `SSL Mode=VerifyFull` or SQL Server `Encrypt=True/Strict;TrustServerCertificate=False`. Development-only self-signed local integration services remain supported. This validates startup configuration rather than real production certificate deployment; TLS/mTLS, CA rotation, DNS and secure secrets still need ops integration tests.

# Required tests in the generated project

## Implemented tests

- `tests/Aba.Starter.Api.Tests/ApiSmokeTests.cs`: generated OpenAPI, Scalar documentation route, health and correlation header using WebApplicationFactory.
- `tests/Aba.Starter.Api.Tests/SecurityGuardTests.cs`: required OIDC config for non-development runtime, explicit local exception.
- `tests/Aba.Starter.Api.Tests/SignedJwtAuthenticationTests.cs`: uses real ASP.NET JWT bearer validation for a signed valid token, invalid signature, incorrect issuer/audience, expired and malformed tokens. Uses a disposable test key; no false claims of a real IdP.

## Commands

```bash
dotnet restore Aba.Starter.slnx
dotnet build Aba.Starter.slnx -c Release --no-restore
dotnet test tests/Aba.Starter.Api.Tests/Aba.Starter.Api.Tests.csproj -c Release --no-build
```

Solution Explorer groups the API, Framework and test projects under `/API/`,
`/Framework/` and `/Tests/`. The real-service integration project is visible
under `/Tests/`, while the default test command targets only unit-test projects
that do not require external services.

## Optional real-service integration suite

A separate integration test project lives in `tests/Aba.Starter.Platform.IntegrationTests`. It contains actual PostgreSQL, SQL Server, Redis, RabbitMQ, Kafka and Wolverine tests. The project is listed in the solution for visibility and builds with the solution, while the default test command stays fast and service-free. Running the dedicated integration project requires the relevant external connections and fails closed if they are absent, rather than falsely recording a PASS. Set `ABA_TEST_POSTGRESQL`, `ABA_TEST_SQLSERVER`, `ABA_TEST_REDIS`, `ABA_TEST_RABBITMQ` and `ABA_TEST_KAFKA`, then execute `dotnet test tests/Aba.Starter.Platform.IntegrationTests/Aba.Starter.Platform.IntegrationTests.csproj`. Repo-level `scripts/verify-platform-services.sh` builds and tests this isolated suite on a fresh generated API and CI provisions actual containers.

## Transactional Outbox acceptance gate

The real-service integration suite invokes `AbaTransactionalOutboxProbe` against **both** database engines and checks:
- success: one EF row and one downstream handler receipt;
- injected fault after publishing, before commit: neither row nor downstream receipt exists;
- default domain-neutral quick start remains external-service-free.

This is stronger than a connectivity test, but it is still not a crash/restart or broker outage test.

## Optional RabbitMQ transport and outage tests

`RabbitMqIntegrationTests` routes only the technical `AbaDurabilityProbe`
through a test-owned queue. It publishes the same payload twice and checks
that both envelopes reached the durable inbox while the effect receipt remains
unique, once against each SQL provider. `BrokerOutageDrill` pauses only the
exact labeled `rabbitmq:4.4.0-management-alpine` CI service on loopback, checks
that the transactional outbox state commits without delivery during the
outage, resumes the broker and requires eventual delivery. The fixture always
unpauses its verified disposable container. It is excluded from `--level full`
and invoked by an explicit CI step. It is technical evidence only; actual
product contracts, handler compensation, retry limits, DLQ policy and
production cluster recovery stay with their owners.

## Optional Kafka durable delivery test

`KafkaIntegrationTests.cs` sends a synthetic technical probe to a random topic
using the durable relational outbox and inbox, once with PostgreSQL and once
with SQL Server. It publishes the same payload twice and asserts two delivery
attempts but one database effect. The manually dispatched integration workflow
provisions the official Apache Kafka 4.4.0 single-node image. The case is
authored until exact-head CI executes it; it does not certify production Kafka
security, topology, failover, retry policy or business consumer behavior.

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

## Real Production-startup TLS fail-closed check

`SecurityGuardTests.ProductionSecurityPreflightRejectsUnverifiedDatabaseTls` validates both provider-specific security decisions directly. `scripts/verify-container-starter.sh` then launches the **actual generated non-root API image** in Production, with a syntactically valid HTTPS OIDC Authority/Audience and intentionally insecure PostgreSQL/SQL Server strings. Each process must exit nonzero promptly and report the **correct** TLS verification guard before listening. This avoids a false-positive WebApplicationFactory override of early Minimal API startup configuration.

## Safe RFC 7807 HTTP failure and correlation checks

`ErrorHandlingTests.cs` creates a self-contained Production `TestServer` using the **same** `AddAbaErrorHandling` / `UseAbaErrorHandling` extensions as the generated host. A route that throws an exception is registered **only within this test host**. Positive/negative cases assert HTTP 500, `application/problem+json`, a 500 status property, matching body/header correlation IDs, rejected unsafe caller IDs, and no leakage of an exception's private message or class name. The real generated API has no exception-triggering test route.

## Tagged readiness failure without liveness failure

`ReadinessTests.cs` injects a failed `ready`-tagged dependency into the real WebApplicationFactory host and verifies `/health/ready` returns HTTP 503 while `/health/live` remains HTTP 200. This is deterministic in-memory failure injection, **not** a claim of a live PostgreSQL or Redis outage/recovery test. Provider disconnect and load behavior remain a release gate.

## NuGet advisory and SPDX test gate

`tools/test_supply_chain.py` has offline positive/negative tests for
direct and transitive advisories, absent HTTPS advisory sources, missing
project coverage, missing restore graphs, unsafe project paths, malformed
scans and generation of an SPDX 2.3 NuGet package inventory.
The default generated workflow runs the validator using the actual
`dotnet package list --vulnerable --include-transitive --format json`
output after restore/build and uploads audit JSON plus SPDX inventory.
The optional external integration test project is outside the
default solution and requires separate scanning; release signing,
image verification and attestations remain undone.

## Provider-native disaster recovery on isolated CI databases

The real-service suite includes `BackupRestoreDrillTests.cs`, tagged
`Category=RecoveryDrill`. Negative cases forbid running without both
`CI=true` and `ABA_ENABLE_DESTRUCTIVE_CI_DRILLS=yes`, remote hosts, or
a non-`aba_ci` target. Positive PostgreSQL and SQL Server cases each create
a unique disposable database, backup a marker, mutate it, restore, and check
that the original data is recovered. The PostgreSQL tools are sourced from
the same `postgres:16-alpine` major as the disposable database service.

The fast `Aba.Starter.slnx` test command and the normal external suite
**exclude** recovery drills. Repository CI and the generated integration
workflow run them with explicit disposable infrastructure and opt-in.
Passing this suite does **not** imply PITR, actual business data restore,
migration rollback, secure backup retention or RTO/RPO compliance.
Read `docs/disaster-recovery.md` before any operational use.

## Database Row-Level Security — negative provider tests

`TenantRlsPolicyTests` verifies provider-specific policy DDL
and validation of SQL identifiers and absent tenant identities.
`RowLevelSecurityTests` runs on real PostgreSQL and SQL Server
with a technical test table, demonstrating raw SQL and EF bulk
cross-tenant read/write denial, forged inserts and connection
context reset. PostgreSQL uses a restricted non-superuser fixture;
SQL Server's disposable CI admin fixture must not be treated
as production least-privilege evidence. More detail is in
`docs/decisions/ADR-005-tenant-row-security.md`.

The generated build/test CI also builds and vulnerability-scans
its actual final runtime container image; fixable CRITICAL
findings fail the separate `container-security` job, with JSON
evidence retained. Generated projects are not automatically
published or signed under the upstream ABAFramework identity.

## Audit ledger and dead-letter test contract

Default xUnit cases cover key validation, disabled defaults, key
rotation and malformed audit fields. Real PostgreSQL and SQL Server
tests assert HMAC integrity, changed-row detection, external
checkpoint mismatch, missing old signing keys and concurrent
append sequencing. The Wolverine integration suite uses a
synthetic poison message to verify quarantine, denied anonymous
or type-mismatched requests, operator-authorized replay after
repair and chain verification. The operator suite also proves the one-time
inspection token is stored only as a hash, bound to the envelope, types,
subject and ticket, expires, and can be consumed by at most one of two
concurrent replay requests on each SQL provider. These are technical tests,
not business workflow or live operator identity approval.

## Real SIGKILL and externally signed audit prefix cases

The separate CrashDrill category runs a nested dotnet test
worker, synchronizes via an ephemeral marker and SIGKILLs
only the child process tree. It tests two windows per real
SQL provider: EF SaveChanges flushed but not committed
must roll back, and Wolverine scheduled-envelope persistence
must survive process death and recover in a new host.
CI=true, explicit drill opt-in and exact loopback aba_ci
fixture are mandatory, with negative rejection tests.
Regular external tests and task runner exclude CrashDrill.

AuditCheckpointTests plus AuditAnchoredCheckpointIntegrationTests
validate an independently signed HMAC checkpoint, wrong-key
or modified signatures, historical audit prefix binding,
prefix deletion, missing records and append-after-checkpoint
on PostgreSQL and SQL Server. No offsite WORM/SIEM backend,
consumer ACK fault injection or real business effects are
covered by these technical fixtures.

## New ACK-window SIGKILL case

AbruptCrashWindowTests now also includes a third controlled
pre-ACK mode. A technical Wolverine handler in a separate EF
scope commits an idempotent effect receipt, records a delivery
attempt and blocks before acknowledging. The parent kills its
child process, restarts Wolverine on the same SQL store, requires
at least one redelivery attempt and exactly one effect. This
covers the ACK-loss idempotency invariant for a synthetic
technical handler, not a domain consumer or external payment.

**Verified execution:** [root CI 43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289) passed 26/26 standard real-service tests, 7/7 provider-native recovery cases, and 8/8 separate SIGKILL fault-injection tests. Both SQL engines proved one committed technical effect after an actual pre-ACK crash and subsequent redelivery. The later safety preflight tests require separate CI evidence.

## Broker outage plus producer SIGKILL

`BrokerCrashDrill` combines the synthetic transactional outbox with a real
process kill. A child starts its RabbitMQ producer host, then the parent
pauses only the exact labeled loopback CI broker before allowing a technical
outbox command to commit in PostgreSQL. The parent verifies one committed
command receipt and no consumer effect, kills the child, restores the broker,
and starts a fresh host. Acceptance requires recovered delivery, one
idempotent effect receipt, and another delivery attempt from an intentional
duplicate logical message without a second effect.

This category is excluded from ordinary integration runs and requires both
destructive-drill and broker-outage opt-ins. It is authored but unverified
until exact-head CI executes it. It covers only generic technical recovery;
it does not certify a business Outbox, business consumer ACK handling,
external provider side effects, live broker failover or Production behavior.

## Real Redis restart and host-wide cache stampede

CacheSingleFlightTests checks 24 concurrent request scopes coalescing
to one loader, independent tenant/key lanes, cancellation cleanup,
fault cleanup and TTL expiry. The default cache tests additionally
exercise valid CLR zero and cached JSON null versus cache miss.

The ordinary external suite tests 12 simultaneous misses on the
**actual Redis adapter**, requiring one loader and no leaked gates.
RedisRestartDrillTests runs separately, using a CI-owned Redis
container ID validated before any Docker stop, and requires
fail-closed strict outage, explicit authoritative-source fallback,
real restart/reconnect, healthy readiness and scope isolation.
The normal integration suite excludes RedisRestartDrill; the
generated manually dispatched workflow and root CI give it a
dedicated disposable container. No production Redis restart or
distributed lock is claimed by the default mode.

`AbaCacheDistributedCoordinationTests` covers opt-in configuration and
the read-after-lock, shared-permit and fail-closed invariants with a fake
coordinator. `AbaDistributedCacheCoordinationIntegrationTests` creates
separate service providers against one Redis backend to prove same-key
miss collapse, shared concurrent-source limits and a shared serial-load
budget, and readiness reports the separate coordination connection.
The authored shared-cache invalidation case writes the same key under two
isolated scopes through one provider, removes one scoped entry, and requires
the second independent provider to observe that miss while the other scope
remains available. This does not prove a versioned invalidation-event
backplane or tenant authorization.
Its unreachable-Redis case verifies that an explicitly permitted
authoritative-source fallback still does not call the source without a
shared coordinator permit; a configuration-drift case rejects replicas
that share a namespace but disagree on the budgets. These authored cases are not live-provider PASS
evidence until the integration project executes them against the current
Redis fixture.

## Immutable Docker base and attested scanned archive

The generated CI executes tools/test_oci_base_lock.py to deliberately
reject digest changes, floating tags, untrusted registries and
root processes; tools/oci_base_lock.py checks the actual
Dockerfile and reviewed JSON manifest before Docker building.
Root main-branch CI additionally signs the exact Docker-save
archive of the Trivy-scanned image after verifying docker load
reproduces its image ID and rejects tampered archive bytes.
The generated downstream repo inherits the pin check and scan,
not the upstream signature.

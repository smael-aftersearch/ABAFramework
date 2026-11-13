# ABA platform integrations — .NET 10

These are **real opt-in adapters** for a domain-neutral API; no Academy or sample business contexts are generated.

## Provider selector: PostgreSQL **and** SQL Server

EF Core 10 is wired in `src/Aba.Starter.Platform`. Choose **one provider for a deployment** through configuration (not hardcoded product code):

| Configuration key | PostgreSQL | SQL Server / Azure SQL |
| --- | --- | --- |
| `Platform:Database:Provider` | `PostgreSql` | `SqlServer` |
| `ConnectionStrings:AbaDatabase` | `Host=...;Database=...;Username=...;Password=...` | `Server=...;Database=...;User Id=...;Password=...;Encrypt=True` |

The provider name defaults to `None`, and **no connection string is shipped**. **New production startup guard:** the Platform refuses PostgreSQL connections unless `SSL Mode=VerifyFull` and a trusted certificate are configured, and refuses SQL Server connections unless encryption is enabled (`Encrypt=True`/`Encrypt=Strict`) with `TrustServerCertificate=False`. This is checked **before the API listens**, and disposable development connections remain permitted only in the Development environment. This verifies TLS configuration, not that a production database certificate, DNS, trust chain, failover or backup actually works. Database readiness is checked at `/health/ready` when enabled. EF Core mappings and an initial provider-neutral migration for `aba_idempotency_receipts` are included. That table is technical infrastructure; real bounded contexts define their own owned tables and migrations, never shared domain tables.

Run migrations **as a deployment step**; never grant automatic DDL or broad database privileges to an ordinary production web process. Example after installing `dotnet-ef` (matching the EF Core 10 version used):

```bash
ABA_DB_PROVIDER=PostgreSql ABA_DB_CONNECTION='Host=localhost;Database=aba;Username=...;Password=...' \
dotnet ef database update --project src/Your.Project.Platform --context AbaPlatformDbContext

ABA_DB_PROVIDER=SqlServer ABA_DB_CONNECTION='Server=localhost;Database=aba;User Id=...;Password=...;Encrypt=True' \
dotnet ef database update --project src/Your.Project.Platform --context AbaPlatformDbContext
```

A single platform migration path deliberately uses *provider-neutral CLR types*; both provider SQL generators are exercised by database integration tests. Independently version/migrate every business context's storage.

The included `EfIdempotencyStore` uses a unique compound primary key `(Scope, Key)`. Prefer `ExecuteOnceInTransactionAsync(scope,key,work)` to persist the receipt **and technical work performed on the same DbContext** atomically; real tests on both SQL engines cover rollback, success and duplicate suppression. Do not perform HTTP calls, publish to an unrelated bus, or mutate another context's DbContext inside this callback. The older `TryClaimAsync` persists a receipt immediately and is unsuitable by itself for a business operation that can fail later. The business owner remains responsible for scope, response semantics, tenant authorization and any cross-context outbox.

## Redis caching

Default: `Platform:Cache:Provider=Memory`; no Redis server is necessary for local quick start.

To enable real Redis, set `Platform__Cache__Provider=Redis` and `ConnectionStrings__AbaRedis=localhost:6379`, using environment variables, workload identity, or a secret store. The `ScopedJsonCache` wrapper supports JSON values, TTLs, removal/invalidation and explicit scope hashing. The Redis provider has a real read/write/remove health check at `/health/ready`.

**Tenant boundary:** a cache scope is only a namespace. You must authenticate/authorize tenant ownership **before** constructing and using that scope. The starter does not yet implement multitenant authorization, cache stampede protection or distributed invalidation events.

### Bounded opt-in query source loads

The optional `ReadThroughQueryCache` `UseAuthoritativeSource` policy
uses a process-wide, fail-fast source-load budget (default 8 concurrent
queries, configured with `Platform__Cache__OptInSourceLoadMaxConcurrent`
between 1 and 256). This also caps opted-in cache misses when Redis
reads succeed but writes may fail. A saturated host throws
`AbaCacheSourceLoadLimitExceededException` **before** invoking the
authoritative query loader. Failure and cancellation release permits,
and the default `FailClosed` policy is unchanged.

This is only a **per-replica** concurrency limit for already-authorized
non-authoritative queries. It does not coordinate tenants or processes,
provide a global rate quota, protect serial request bursts or authorize
fallback for payment/idempotency/permissions. Deployments must test
multi-node cache failures and the actual backend budget independently.
See [ADR-010](decisions/ADR-010-cache-singleflight-redis-restart.md).

## Wolverine durable messaging / Outbox–Inbox

Enable with `Platform__Messaging__Enabled=true` **only when a PostgreSQL or SQL Server database is configured**. The platform uses WolverineFx 6 and selects `PersistMessagesWithPostgresql` or `PersistMessagesWithSqlServer`, EF Core transactional middleware and durable local queues. These are genuine relational message persistence integrations, not the old Academy file outbox.

Wolverine envelope tables are separately managed from EF Core domain migrations. Provision Wolverine's resources according to the version-matched Wolverine CLI in deployment, with the same database provider. Use `resources setup`/resource migrations in a controlled deployment step; do not enable destructive auto-purge or auto-create in production. Each handler that writes domain state and publishes messages must enroll its own owning `DbContext` in Wolverine's transaction; a registered store alone is **not proof of a transactional domain workflow**.

Wolverine 6 requires the explicit `WolverineFx.RuntimeCompilation` package for dynamic handler code generation, and the template now includes it. For **isolated Development integration tests only**, set `Platform__Messaging__AutoProvisionInDevelopment=true` to allow Wolverine's native `AutoBuildMessageStorageOnStartup` policy to create required envelope tables. Production startup explicitly keeps `AutoCreate.None` and requires reviewed provisioning before deployment. A technical probe handler is included to verify durable local message processing against each real SQL engine.

No RabbitMQ/Kafka or business handler is silently installed. CI now also exercises a **technical EF + Wolverine transactional outbox probe** on both SQL engines: a successful handler writes state and dispatches a message together; an injected pre-commit exception after `PublishAsync` must leave neither the state row nor the downstream receipt. This tests rollback at a real transactional boundary. **CI-verified synthetic dead-letter replay and clean restart are available; abrupt SIGKILL drills are separately authored and CI-gated. Production process supervision, business Outbox commit/publish/ACK and consumer idempotency are still incomplete.** Do not equate the probe with complete production message reliability.

## Local development integration services

From the repository root with Docker Compose:

```bash
docker compose -f infra/compose.platform.yaml up -d
```

PostgreSQL: `127.0.0.1:54329`; SQL Server: `127.0.0.1:14339`; Redis: `127.0.0.1:6389`. Credentials in Compose are **throwaway development-only fixtures**, not production secrets.

Set `ABA_TEST_POSTGRESQL`, `ABA_TEST_SQLSERVER` and `ABA_TEST_REDIS` and run `bash scripts/verify-platform-services.sh`. GitHub Actions runs an equivalent integration job against actual service containers. Without those env vars, the xUnit external-service cases are opt-in and do not attempt remote connections; check CI artifacts before claiming both live database engines were tested.

## Failure and rollback policy

- A configured provider with a missing connection string **fails fast**.
- Redis is optional: explicitly choosing Redis without a connection **fails fast**, rather than silently switching to local memory.
- Wolverine durability is opt-in: enabling it without a relational store **fails fast**, rather than switching to non-durable messaging.
- Per-context rollback uses reviewed migrations, not `EnsureCreated` or destructive reinitialization.
- Do not commit real connection strings. Rotate any credentials that accidentally enter repository history.

## Observability: OpenTelemetry

Set `Platform__Observability__Enabled=true` to enable ASP.NET Core and outgoing HTTP traces/metrics. Set `Platform__Observability__ServiceName=MyApi` and optionally `Platform__Observability__OtlpEndpoint=https://collector.example:4317` to export via OTLP. An unsecured HTTP collector is accepted only in local Development. No external collector, credentials, public metrics endpoint or telemetry backend is bundled. Review tracing sampling, PII filters, service identity and collector trust in production.


## Optional tenant claim safety

`Platform__Tenancy__Enabled=true` requires a nonempty authenticated `tenant_id` claim for protected API routes; customize `Platform__Tenancy__ClaimType` when your trusted IdP has a different claim. `IRequestTenant` reads only validated JWT principals and never trusts `X-Tenant-ID` from an untrusted caller. This is a technical guardrail, **not** a complete multitenancy authorization or EF Core row-level data isolation system.

## EF tenant guards for new real bounded contexts

In the owning context's Infrastructure project, derive its EF `DbContext` from `AbaTenantDbContext`, inject `IRequestTenant`, and mark **infrastructure row models** with `ITenantOwnedRow`. Do **not** put EF-specific concerns on pure Domain aggregates. Its query filter prevents accidental cross-tenant reads and SaveChanges guards updates/deletes. Requests without authenticated tenant context fail writes. This is a defense-in-depth convention, not database row-level security, global `ExecuteUpdate` protection, or proof of Identity membership; the owning context must enforce those separately.

## Durable message scheduling across clean host restart

`DurabilityRecoveryTests` schedules an actual durable technical message into a PostgreSQL or SQL Server Wolverine queue, stops the first host **before** its due time, starts a new host on the same database and requires the message to be processed. It is a real restart-and-recovery test; it is not a simulation of an ungraceful process crash at an arbitrary database commit stage.

The CI suite now also verifies `AbaTenantDbContext` using **both real relational engines**, in addition to SQLite. Each engine must suppress reads from a foreign tenant, prevent cross-tenant `SaveChanges` even after `IgnoreQueryFilters`, reject forged tenant inserts and require a validated tenant ID. The test fixture uses the separate technical table `aba_ci_tenant_rows`. Untrusted direct SQL and EF bulk updates remain outside this protection.

## Real OIDC/JWKS integration boundary

The default starter requires HTTPS identity-provider discovery. For a **local test-only** identity provider on loopback HTTP, explicitly set `Authentication__AllowHttpMetadataInDevelopment=true` while `ASPNETCORE_ENVIRONMENT=Development`; the default is `false`, the flag is ignored for HTTP authorities outside the loopback interface, and it cannot disable HTTPS metadata in Production. The separate `scripts/verify-keycloak-oidc.sh` CI harness uses a disposable Keycloak realm and exercises discovery/JWKS, signed access tokens, audience and Tenant authorization. It does NOT implement the login UI or issue tokens itself. For real users, choose standards-compliant authorization-code/PKCE or backend client-credentials flows, enable TLS and configure issuer/audience, memberships, roles, key rotation and expiry policies at your identity provider.

## Cache-outage resilience for non-authoritative query data

`ReadThroughQueryCache.GetOrLoadAsync` is now available as a scoped technical service. **Default is `CacheOutagePolicy.FailClosed`**: a Redis timeout/connection error propagates; the source loader is not executed. Explicit `CacheOutagePolicy.UseAuthoritativeSource` permits a carefully scoped *read-through query* to load fresh data from its authoritative source when Redis is unavailable, or to return the freshly loaded value when writing the cache fails. This does **not** provide cached fallback authentication or stale results. Connection/timeouts are caught narrowly; loader, authorization, data-deserialization, invalid scope and cancellation errors are never hidden. The caller must authorize tenant access **before** looking up a cache hit; the cache key namespace alone is not permission. Never use this bypass for security decisions, idempotency, payments or other source-of-truth invariants.

Example inside an already authorized application query:

```csharp
var result = await readThroughCache.GetOrLoadAsync(
    authorizedTenantScope, "product-list-v1", TimeSpan.FromSeconds(30),
    ct => queryRepository.ReadProductsAsync(ct),
    CacheOutagePolicy.UseAuthoritativeSource, cancellationToken);
```

This resilience behavior has positive/negative unit tests with an injected connection-timeout cache. It is **not** a substitute for a real Redis kill/recovery, distributed stampede or load test.

Concurrent idempotency is exercised by four overlapping `ExecuteOnceInTransactionAsync` calls against **both** real SQL engines. Exactly one worker may commit the receipt and same-transaction work; the others must report duplicate, with no additional work persisted. This does not make external calls exactly-once or promise automatic retries on unrelated SQL deadlocks.

The external CI suite also tests the **actual Redis client** against a deliberately unreachable loopback TCP port: opted-in read-through queries return fresh authoritative values on connection failure while the default strict policy fails. This complements the deterministic fake-cache unit tests; reconnect/backpressure under a real cluster restart remains future hardening.

For end-to-end assurance, the Docker smoke suite starts the **real Production binary** with trusted-looking OIDC authority parameters and a deliberately insecure PostgreSQL or SQL Server connection string. The process must terminate with the provider-specific TLS validation error before opening an HTTP port; a unit-only configuration test is insufficient for this claim. Actual external database CA and key-rotation operations are still a deployment acceptance gate.

## Verified native disaster-recovery drill boundary (CI-only)

The isolated `BackupRestoreDrillTests` class uses provider-native tooling,
not EF `EnsureCreated`, to restore a disposable marker after mutation. The
PostgreSQL backup tooling runs from `postgres:16-alpine` in Docker to match the
server major exactly; the SQL Server drill uses `BACKUP DATABASE ... CHECKSUM`,
`RESTORE VERIFYONLY` and `RESTORE DATABASE` against a new disposable DB.
Only tests explicitly marked `Category=RecoveryDrill` perform these actions
and require a strict CI-only opt-in. Ordinary tests never drop databases.
Read `docs/disaster-recovery.md` for acceptance cases, protected execution,
retention limits and a **separate** production operational checklist.

## Provider Row-Level Security (context-opt-in; not automatic)

`AbaTenantRlsPolicies.Install(provider, schema, table)` yields migration
SQL, not a runtime start-up operation. PostgreSQL uses ENABLE + FORCE RLS
and both USING/WITH CHECK against `aba.tenant_id`; SQL Server uses a
schema-bound function and FILTER plus AFTER INSERT/UPDATE BLOCK predicates
against `SESSION_CONTEXT(N'aba_tenant_id')`. No tenant business table is
created by Platform.

`AbaTenantSqlScope.RunAsync` starts a transaction on a previously closed
connection, binds the **authenticated** `IRequestTenant` with a SQL
parameter, executes the supplied raw SQL/EF bulk delegate in that
transaction, then clears the SQL Server session tenant before pooling.
PostgreSQL uses transaction-local `set_config(..., true)`.
The current isolated integration suite tests absence of a tenant, malicious
cross-tenant raw SQL and EF ExecuteUpdate/ExecuteDelete, and forged writes
against both live SQL engines.

An application MUST validate tenant membership before creating the scope,
use a role without DDL/policy privileges, protect against SQL injection
(including commands capable of changing session context), and install
policies in separately approved owner migrations. PostgreSQL superusers
bypass RLS. The SQL Server CI fixture uses a disposable admin login:
it is not least-privilege production proof. See
[ADR-005](decisions/ADR-005-tenant-row-security.md) for rollback policy
and trust boundaries.

## Provider-backed audit and Wolverine poison recovery

The 202611130001_AuditLedger migration adds a technical audit table on
both PostgreSQL and SQL Server. The HMAC-SHA256 chain includes stable
UTC timestamps, actor/tenant/action/outcome IDs and key version,
and is written through SERIALIZABLE per-tenant transactions. Missing
or weak keys fail at enabled startup; old keys are needed after
rotation; external checkpoints detect terminal deletion.

The operator service wraps Wolverine's own durable dead-letter API
with explicit inspect/replay permission claims and audit-first
processing. Inspection does not return bodies or exception messages.
A replay mark is not evidence of successful consumption. See the
operations guide for detailed incident checks.

## SIGKILL fault boundaries and offline audit anchoring

CrashDrill (external test suite) runs a real child .NET host
with PostgreSQL or SQL Server, deliberately kills that child
after an uncommitted EF flush or after a durable Wolverine
ScheduleAsync, then validates rollback and restarted-host
delivery. It must run only with CI=true, the disposable-drill
opt-in and the exact local aba_ci fixture. Ordinary xUnit
verification excludes this category. Read ADR-008.

The audit checkpoint signer is intentionally NOT in ordinary
runtime dependency injection. Use CheckpointCandidateAsync
to obtain an unsigned, previously verified tenant digest and
sequence; have an independent custodian sign with a separate
secret; preserve the signed record off-database. A later
VerifyAnchoredPrefixAsync call verifies the signed prefix even
if the ledger contains newer valid rows. Read ADR-009.

## Before consumer ACK, after technical effect commit

The technical AbaCrashAckProbe writes an attempt receipt and
atomically claimed effect receipt outside Wolverine's handler
acknowledgement path. Only when the explicit CI process-crash
marker is configured will that synthetic handler park before
returning. Its test's parent then kills the worker and requires
new-host re-delivery with an unchanged single effect receipt.
The template does not expose this probe through an HTTP endpoint
or invoke a real business provider. Actual multi-context,
broker ACK and external side-effect guarantees remain open.

## Host-wide single-flight cache misses and real Redis restart

AbaCacheLoadGate is a singleton per generated application host. It
uses a reference-counted per-key SemaphoreSlim keyed by the existing
hashed ScopedJsonCache scope/key and removes the entry on final
release, including cancellation/failure. ReadThroughQueryCache
takes this gate and re-reads the cache *inside* the lock; healthy
concurrent misses across scoped service instances share one
authoritative query load instead of producing a stampede.
TryGetAsync<T> distinguishes an absent cache entry from valid
default(T) or JSON null data.

The isolated RedisRestartDrill CI job provisions its own official
Redis 7 container, stops it, demands default FailClosed without
a source read and opt-in UseAuthoritativeSource with exactly one
fresh read, then restarts it and proves reconnect, cache hit and
tenant-scoped isolation with the same Redis client. Docker stops
are never permitted without CI=true, a separately enabled drill,
one loopback Redis endpoint, an exact service-container ID and
an official redis:7-alpine image check. See ADR-010.

The gate is in-process and does not prevent cross-replica cache
stampedes or simultaneous source reads while Redis is down.
Never depend on cache fallback for authorization, financial
decisions, idempotent commands or tenant membership.

## Reproducible reviewed OCI base digests

The multi-stage Dockerfile uses immutable .NET 10 Noble build and
runtime digest pins. tools/oci_base_lock.py and its negative tests
guard the Dockerfile/oci-base-lock.json pair; the root CI attests
the exact Docker-save archive after real Trivy scanning and a
Docker load identity check. This is not a published OCI manifest
signature. Read ADR-011 and upstream docs/release-integrity.md.

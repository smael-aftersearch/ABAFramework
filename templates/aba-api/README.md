# Aba.Starter — a domain-neutral ABA API

A reusable .NET 10 starting point. **No example business domain is included.**

## Requirements

- .NET 10 SDK.
- NuGet access for ASP.NET Core OpenAPI, Scalar, JWT bearer, xUnit, EF Core 10, PostgreSQL/SQL Server provider packages, Wolverine 6, Redis and OpenTelemetry (optional services remain off by default).

## Start locally

With Docker installed, the quickest launch is `docker compose up --build` in the generated project directory. The API and Scalar UI are bound to **localhost:5174 only**; no database or Redis is required unless explicitly enabled. The generated multi-stage Dockerfile uses the non-root .NET runtime user. The image is designed for trusted configurable deployments, but does **not** turn Development mode into a secure publicly exposed server.

You can also run the .NET SDK directly:

```bash
dotnet restore Aba.Starter.slnx
dotnet run --project src/Aba.Starter.Api
```

In a local Development launch profile, visit `http://127.0.0.1:5174/scalar` for an interactive API reference, `/openapi/v1.json` for the generated OpenAPI document, and `/health/live` for a liveness check.

If running from a shell that doesn't choose a launch profile, set `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://127.0.0.1:5174` explicitly.

## Test

```bash
dotnet build Aba.Starter.slnx -c Release
dotnet test Aba.Starter.slnx -c Release --no-build
```

To run **real external-service tests**, configure the three `ABA_TEST_*` connection strings and run `dotnet test tests/Aba.Starter.Platform.IntegrationTests/Aba.Starter.Platform.IntegrationTests.csproj`. This suite is intentionally excluded from the default solution; it fails if an external connection is missing.

## NuGet dependency audit and SPDX inventory

The generated default CI checks the .NET 10 machine-readable NuGet
`--vulnerable --include-transitive` report and fails on any advisory,
unavailable HTTPS advisory sources, malformed results or missing restore
graphs. It also exports an SPDX 2.3 inventory of **resolved NuGet packages**
from `project.assets.json`; CI retains both artifacts.

After a normal restore and build, run:

```bash
python3 tools/test_supply_chain.py
mkdir -p artifacts/supply-chain
dotnet package list --project Aba.Starter.slnx --vulnerable --include-transitive --format json --output-version 1 --no-restore > artifacts/supply-chain/audit.json
python3 tools/supply_chain.py --root . --solution Aba.Starter.slnx --audit-json artifacts/supply-chain/audit.json --output artifacts/supply-chain/sbom.spdx.json
```

The optional real-service integration test project is outside the
default solution but is now **restored and audited separately in the same CI**,
with its own advisory JSON and SPDX 2.3 package inventory.
This does **not** scan container images, pin image digests, sign packages,
verify provenance or authorize deployment.

## Installable NuGet template package

Repository CI packs this template as `ABAFramework.GenericApi.Template.0.3.0-preview.2.nupkg` and verifies installation by generating a new project. Download the package artifact from the successful GitHub Actions run and install it with `dotnet new install ./ABAFramework.GenericApi.Template.0.3.0-preview.2.nupkg`. This does not require Academy or any business sample. The package is not yet published to NuGet.org.

## Real PostgreSQL / SQL Server recovery drills

The repository CI and the generated project's manually triggered integration
workflow exercise **native database backups and restores** against fresh
disposable test databases. Framework source [CI 43313417455](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455)
passed 7/7 recovery-and-safety cases; new products must verify their own runs. PostgreSQL uses `pg_dump` and `pg_restore` from a
matching `postgres:16-alpine` Docker image, and SQL Server uses
`BACKUP DATABASE`, `RESTORE VERIFYONLY` and `RESTORE DATABASE`. The test
checks that a backup recovers the original value after an intentional mutation.

These operations are excluded from regular xUnit runs and require
`CI=true` plus `ABA_ENABLE_DESTRUCTIVE_CI_DRILLS=yes` and exact local
`aba_ci` test fixtures. Do not enable these flags on a production host.
See [the disaster-recovery runbook](docs/disaster-recovery.md) for limitations,
run commands and the independent production operations checklist.

## PostgreSQL / SQL Server Row-Level Security

The Platform includes migration-time `AbaTenantRlsPolicies.Install` DDL
and a `AbaTenantSqlScope.RunAsync` transaction wrapper. In real SQL tests
the installed policies protect direct SQL and EF bulk mutations, not just
tracked EF entities. No default business table or RLS migration is
auto-installed. An authorized membership claim, reviewed migrations and
least-privilege production roles are still required. See
[ADR-005](docs/decisions/ADR-005-tenant-row-security.md) and
[platform infrastructure](docs/platform-infrastructure.md).

## Generated CI runtime security

The [verified framework CI run](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43317295032) passed the upstream image scan and NuGet provenance gates. The included `.github/workflows/ci.yml` now builds and scans the real
runtime Docker image using commit-pinned Trivy. Fixable CRITICAL findings
fail its job and JSON evidence is retained. A generated product does not
inherit the source repository's NuGet template artifact signer or
publication permissions. Independent product release identity and
base image digest pinning remain separate tasks.


## HMAC audit and explicit dead-letter operations

The optional technical audit ledger requires PostgreSQL or SQL Server,
an external HMAC keyring, and an explicitly enabled audit configuration.
Its dedicated migration creates the technical audit table. AppendAsync
records a sequence, opaque actor/action/outcome references and a
predecessor HMAC digest. VerifyAsync checks historical integrity and,
with an externally anchored expected digest, detects tail truncation.
Signing keys must never be committed to Git or image layers.

AbaDeadLetterAdministration is an opt-in service for Wolverine durable
local dead letters. It refuses anonymous operators and tenant mode,
shows metadata only, and marks only an explicitly approved envelope for
asynchronous replay with pre-committed audit evidence. No public HTTP
replay routes, wildcard replay or arbitrary payload exposure are added.
The [runbook](docs/operations/audit-deadletters.md) documents all
configuration, permissions, key rotation and rollback limits.

## Optional multi-replica Redis API rate limits

By default limits are per-process. To opt in set
`Platform__RateLimit__Shared__Enabled=true`,
`Platform__RateLimit__Shared__Namespace=your-app` (same value on every replica),
and a secure `ConnectionStrings__AbaRedis`. Outside Development the opted-in
limiter **refuses plaintext Redis** unless its connection string specifies
`ssl=true`; require independent certificate/ACL/proxy policy approval before
production. Development-only disposable Redis may remain plaintext.
The local limiter remains active.
Redis failure returns 503 instead of silently relaxing a cross-replica quota;
quota denial is 429, liveness is exempt, and readiness probes Redis.
Only the authenticated JWT subject and tenant or the actual anonymous TCP peer
define a quota partition; client forwarding and tenant headers are untrusted.
An owner-reviewed proxy/Redis security policy is still required for production.
This does not implement membership or business authorization. See
[ADR-012](docs/decisions/ADR-012-shared-redis-rate-limits.md).

## Security

All endpoints are authenticated by default (fallback authorization policy). `/health/live` and Development-only documentation are anonymous; the system status endpoint is also anonymous **only in Development**. In other environments, the host **refuses to start without a trusted HTTPS OIDC Authority and Audience**. It has no bundled signing key. Never treat Development mode as public-hosting configuration.

## HTTP failure handling and health probes

Unexpected server-side failures return RFC 7807 `application/problem+json` without exposing exception messages in Production. The validated `X-Correlation-ID` response header is also included as `correlationId` in the error body. `/health/live` remains independent from tagged dependencies, while `/health/ready` reports a failing configured database/Redis dependency as unavailable (503). The included tests exercise a synthetic 500 and readiness failure; they do not certify production dependency recovery or a deployed monitoring setup.

## Add your actual business context (no sample domain)

When the real owner is known, run `python3 tools/new_context.py create --name Inventory` from the **generated project root**. This creates Domain, Application, Infrastructure, Contracts, metadata, xUnit tests, and API composition wiring. Verify with `python3 tools/new_context.py check`, `dotnet build`, `dotnet test`. Python is only needed for the generator, not for running the compiled API. Read `docs/context-generator.md`.

## Architecture

Start with `AGENTS.md`, `docs/ai/INDEX.md` and the included `.agents/skills/aba-engineering/SKILL.md`. Add bounded contexts **only for your actual business domains** and record ownership in `metadata/contexts.json`. Avoid premature shared Domain models. EF Core 10 provider selection (PostgreSQL and SQL Server), optional Redis, and Wolverine relational durability are now implemented as technical adapters in the generated Platform project. They are disabled by default to preserve the zero-dependency first run; read `docs/platform-infrastructure.md` before enabling them. Domain migrations, production runbooks and real fault-injection/outbox integration tests remain future work.

## Optional real database, cache, messaging and telemetry

The `src/Aba.Starter.Platform` technical library supports **both** PostgreSQL and Microsoft SQL Server with EF Core 10. Enable exactly one provider per deployment by setting `Platform__Database__Provider=PostgreSql` or `SqlServer` and supply `ConnectionStrings__AbaDatabase` securely. EF Core migration is included for the technical idempotency table. Toggle `Platform__Cache__Provider=Redis` with `ConnectionStrings__AbaRedis`; enable Wolverine relational durability with `Platform__Messaging__Enabled=true`. Enable OpenTelemetry with `Platform__Observability__Enabled=true` and optional OTLP endpoint. The `Platform__Tenancy__Enabled=true` flag requires an authenticated tenant claim, but **full tenant data isolation is not implemented**.

Read [docs/platform-infrastructure.md](docs/platform-infrastructure.md) for configuration details and [docs/architecture.md](docs/architecture.md) for boundaries. The host remains domain-neutral; no Academy context is generated.

**Actual integration evidence:** [GitHub Actions real containers](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43290885904) passed PostgreSQL and SQL Server EF migrations/idempotency, Redis and Wolverine technical message delivery for both engines. Do not infer production-level durability/security from those baseline tests.

## OIDC test-only local discovery

`Authentication__AllowHttpMetadataInDevelopment=true` permits a loopback HTTP identity provider **only in Development**, for the separate real Keycloak/JWKS integration test. Production HTTPS discovery remains mandatory; never use this flag to bypass TLS outside isolated local tests.

**If your actual context needs its own EF Core persistence**, use `python3 tools/new_context.py create --name Inventory --with-db` instead. It registers a context-owned empty DbContext for either PostgreSQL or SQL Server, plus provider-selection tests. No database tables or migrations are fabricated.

## Cache outage handling

For non-authoritative, already-authorized queries you may use `ReadThroughQueryCache` with `CacheOutagePolicy.UseAuthoritativeSource` to fetch fresh source data when Redis connectivity fails; default remains strict `FailClosed`. Never use this for authentication, tenant authorization, accounting, financial idempotency or other business invariants. See `docs/platform-infrastructure.md`.

## Request rate limits

`Platform:RateLimit:PermitLimit` (default 120) and `Platform:RateLimit:WindowSeconds` (default 60) configure per-instance fixed-window limits. Authenticated requests partition on verified JWT subject plus tenant claim; anonymous traffic is grouped by the actual TCP peer IP, never by untrusted `X-Forwarded-For` or `X-Tenant-ID`. The health endpoints are exempt from request throttling so load cannot make readiness/liveness accidentally fail. This is **per-process**, not a distributed quota across replicas; production needs an API gateway/shared limiter. Test in `RateLimitTests` covers 429, anonymous and JWT partition keys, and health bypass.

## Built-in coding-agent workflow

`tools/aba_task.py` gives a deterministic architecture-aware document reading plan and fixed-command verification with JSON evidence; start with `docs/ai/TASK-PROTOCOL.md`. The `plan` command **never invents business ownership**, and `verify` reports PASS/FAIL/NOT_RUN rather than pretending unavailable database tests were executed. Python 3.10+ is needed for these developer tools only, not for the running API.

## CI is included in every generated repository

The generated project includes `.github/workflows/ci.yml`, which automatically checks agent documentation/architecture governance, .NET 10 restore/build, and xUnit on every Push and PR. A second workflow, `.github/workflows/integration.yml`, is started explicitly with **Run workflow** in GitHub Actions and provisions isolated PostgreSQL, SQL Server and Redis services for the fail-closed Wolverine/EF/Redis integration suite. Both workflows upload JSON test evidence with no production secrets. They do not publish images or deploy without your authorization.

**Latest source verification:** [all 8 GitHub Actions jobs passed](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43320400760),
including per-provider HMAC audit and Wolverine poison quarantine/replay
negative and positive tests. Generated products must independently validate
their own operator policies, HMAC key custody and event processing.

## Explicit crash tests and historical checkpoint signatures

The optional real-service workflow runs an independently gated
CrashDrill category with actual child-process SIGKILL on both
PostgreSQL and SQL Server CI fixture databases. The tests prove
that an uncommitted SQL write rolls back when its process dies,
and that a persisted, time-scheduled Wolverine envelope survives
an abrupt host death. Ordinary build/test startup is unaffected;
do not enable these tests on real customer databases.

The audit service offers CheckpointCandidateAsync and
VerifyAnchoredPrefixAsync. A separate offline signer uses
AbaAuditCheckpointCodec.Sign to bind tenant, sequence and digest
with a wholly independent HMAC key stored OUTSIDE the API.
No automatic external immutability sink or Production key
manager is supplied. See ADR-008, ADR-009 and the runbook.

**Additional technical crash test:** the SIGKILL drill now checks
the consumer-ACK gap using a synthetic receipt written before
handler acknowledgement. Restart must produce a second delivery
attempt while the idempotent effect remains unique. This
probe is CI-only and is not a Production business-handler
guarantee; check the latest run's success before relying on it.

**Verified technical crash evidence:** [CI 43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289) passed all eight CI jobs, including 8 actual SIGKILL cases across both SQL providers and signed historical audit checkpoint checks. The new tests do not authorize Production process-kill, business side effects or shipping signing keys in appsettings.

## Single-flight read-through cache and live Redis failure drill

ReadThroughQueryCache now coalesces simultaneous misses for the SAME
authorized tenant/module scope and cache key **within one host**.
AbaCacheLoadGate releases per-key entries after completion, failure
and cancelled waiters; valid 0/false and JSON null entries are
distinguished from absent keys. Redis is still optional.
The generated Run workflow integration suite now includes an
**isolated redis-restart job** that stops/restarts only its own CI
redis:7-alpine container, checks strict failure and explicit fresh
authoritative query fallback, and proves reconnection. Default
Fast tests never stop Redis. Neither cross-replica locks nor
cache-backed authorization are supplied.

## Opt-in source-load backpressure during cache outages

The `cache-stampede` hardening path includes a fail-fast, shared
per-host limit for source reads using `CacheOutagePolicy.UseAuthoritativeSource`.
`Platform__Cache__OptInSourceLoadMaxConcurrent` defaults to **8** and
accepts **1–256**. When all slots are in use, the query throws
`AbaCacheSourceLoadLimitExceededException` without invoking its loader;
it never returns stale or fabricated data. Permits are released after
success, failure and cancellation, and strict `FailClosed` remains
unchanged. The scope must already have been authorized by its business
owner. This is **not** distributed miss coordination, a global database
budget or production-readiness evidence. See [ADR-010](docs/decisions/ADR-010-cache-singleflight-redis-restart.md)
and `CacheSourceBudgetTests`.

## Immutable OCI base images and signed upstream test archive

The generated Dockerfile uses reviewed immutable multiarch
manifest digests for the Microsoft .NET 10 Noble SDK and runtime,
declared in oci-base-lock.json. Both workflows enforce the
lock via tools/oci_base_lock.py plus negative Python tests.
The upstream framework additionally attests the Docker-save
archive of its scanned image on main pushes. Downstream
products MUST sign their own published OCI digest; this
template does not publish an image or commit registry credentials.
See docs/decisions/ADR-010-cache-singleflight-redis-restart.md
and ADR-011-oci-base-and-archive-attestation.md.

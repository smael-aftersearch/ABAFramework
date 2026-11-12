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

The included `.github/workflows/ci.yml` now builds and scans the real
runtime Docker image using commit-pinned Trivy. Fixable CRITICAL findings
fail its job and JSON evidence is retained. A generated product does not
inherit the source repository's NuGet template artifact signer or
publication permissions. Independent product release identity and
base image digest pinning remain separate tasks.

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

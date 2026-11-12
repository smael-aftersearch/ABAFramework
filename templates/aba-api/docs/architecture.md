# Domain-neutral ABA starter

This is a **starter** for a new API product, not a sample academy/enrollment domain. The API host is the composition root, not a business owner.

## Current implemented baseline

- .NET 10 minimal API
- generated OpenAPI and Development-only interactive Scalar API reference
- separate liveness/readiness checks (tagged dependencies), validated correlation ID and safe RFC 7807 Problem Details for unhandled errors
- configurable fixed-window API rate limiting per verified JWT subject/tenant (anonymous peer IP only), with Health bypass; a gateway is required for shared multi-replica limits
- fail-closed non-Development OIDC/JWT configuration and authorization fallback policy
- xUnit integration and security-configuration tests, plus opt-in real PostgreSQL/SQL Server/Redis tests
- AI agent and test documentation

## Adding your first real context

Name the context using actual business language. Record its owning data, rules, decisions and versioned contracts in `metadata/contexts.json`. Then create appropriate Domain, Application, Infrastructure and Contracts projects, register their services in the Host, and add dependency-ownership tests. Do not create shared domain entities.

## Delivered technical adapters versus missing release gates

EF Core 10 for **PostgreSQL and SQL Server**, provider-neutral technical migrations, Redis distributed caching, Wolverine 6 durable relational messaging, JWT claims, optional EF tenant query/write guards, and an injectable read-through query cache are implemented and exercised in automated tests. An additional production-startup connection-string TLS guard is newly authored and must pass its own CI. **Delivered as optional technical reference only**: provider-specific PostgreSQL/SQL Server RLS policy generators and trusted tenant-scoped transaction work with cross-tenant direct SQL/EF bulk negative tests. **Not delivered for a real business**: owner-approved RLS/DBA roles, real handlers/contracts/migrations, live IdP memberships, arbitrary crash/dead-letter replay, business authorization, production backup/restore, collector validation, load/security certification and public deployment. The authoritative task/status catalog is in the source ABAFramework repository at `docs/IMPLEMENTATION-STATUS.md`; a generated customer project must maintain its own business-specific roadmap.

## Security

The host does not ship a JWT signing key or an identity provider. For non-Development startup, provide `Authentication:Authority` (a trusted HTTPS OIDC issuer URL) and `Authentication:Audience`. Signature, issuer and audience validation then use the JWT bearer middleware. Verify provider-specific configuration and authorization policies before exposing public APIs. All non-explicitly-anonymous routes require authentication.

## Authentication gate verification

The generated API includes a deliberately protected `GET /api/v1/system/authorization-check` endpoint. A request without a valid bearer token returns `401` **even in Development**. This does not replace full OIDC integration, tenant-isolation or signed-token claim tests. Documentation UI is Development-only, and production startup still requires HTTPS authority/audience.

## Optional tenancy-awareness (not tenant data isolation)

Set `Platform:Tenancy:Enabled=true` to require a trusted tenant claim on every authenticated API endpoint, via both the default and fallback JWT authorization policies. `Platform:Tenancy:ClaimType` defaults to `tenant_id`. `IRequestTenant` reads only the principal populated by authentication middleware; a caller's `X-Tenant-ID` header is never accepted as proof of membership. The option is **disabled** for the clean Single-Tenant quickstart. EF `AbaTenantDbContext` query/write safeguards and cross-tenant negative tests now run on PostgreSQL and SQL Server. But actual organization membership, production database RLS deployment, secure raw/bulk SQL command boundaries and context-specific migrations still belong to real owners; **do not advertise production tenant isolation** yet.

## Signed JWT validation tests

The default xUnit suite injects a disposable test-only key into the actual ASP.NET Core bearer middleware and exercises signature, issuer, audience and lifetime checks. These fast tests intentionally do not connect to a remote identity provider; the separate repository CI additionally exercises **real Keycloak OIDC/JWKS** with positive/negative tenant cases. Neither suite certifies production token key rotation, actual organization membership or permission claims. Read `docs/decisions/ADR-002-verified-token-test-boundary.md`.

## Container build and runtime boundary

`Dockerfile` is multi-stage (.NET 10 SDK build and ASP.NET runtime) and uses the non-root `APP_UID`. `compose.yaml` provides an ergonomic **loopback-only Development** API, not a public deployment. The root repository's `scripts/verify-container-starter.sh` generates a fresh named API, builds the OCI image, confirms Production rejects missing OIDC configuration, verifies a non-root process and tests Health, OpenAPI and Scalar through the actual container. A production release must still configure trusted HTTPS OIDC, TLS termination, secrets, service accounts, firewall, migrations, resource limits and an appropriately secured deployment target.

## Reusable EF tenant-owned model guard

`AbaTenantDbContext` can be used as the base class for context-owned EF Infrastructure models implementing `ITenantOwnedRow`. It applies per-context-instance tenant query filtering, populates the tenant column for new records, and rejects cross-tenant inserts, modifications and deletions even if an administrative EF query bypasses the read filter. The tenant source is `IRequestTenant`, which is derived only from a validated authenticated JWT claim in HTTP requests. Tests use isolated SQLite to verify tenant A cannot read tenant B's data or mutate it through `IgnoreQueryFilters`.

**EF protection alone is not full tenant isolation:** this abstraction still permits bulk SQL and administrative access. The separate opt-in `AbaTenantRlsPolicies` + `AbaTenantSqlScope` can enforce provider-native row predicates on actual SQL/EF bulk operations; it is not automatically installed for a real owner. Identity memberships, policy migrations, privileged database roles, SQL-injection control and administrative bypass remain separate release gates. Each real business context must opt in and perform its own tenant validation.

## Per-identity API rate limiting

`AbaRateLimitOptions` validates runtime limit configuration. Authentication middleware runs **before** the rate limiter so JWT partition keys derive from a validated subject, optionally combined with a verified tenant claim; unknown/anonymous callers are partitioned by peer IP. `/health/live` and `/health/ready` use a no-limiter partition. Headers forwarded by unknown clients are not trusted. This fixed-window limiter is per ASP.NET process; a multi-node deployment must use a shared gateway/limit store, trusted proxy settings and rate/abuse budgets.

## HTTP errors and dependency health

The Host registers `AddAbaErrorHandling` and `UseAbaErrorHandling` so unexpected exceptions become RFC 7807 `application/problem+json` (HTTP 500), with a validated `X-Correlation-ID` response header and matching `correlationId` property. Test fixtures assert that Production responses omit the exception's private message. No diagnostics/fault endpoint is exposed in generated APIs. The correlation ID helps troubleshoot failures but is not authorization evidence.

`/health/live` excludes dependency checks while `/health/ready` includes checks tagged `ready`; opted-in EF database and Redis adapters register such checks. An injected failing readiness check is covered by a deterministic HTTP test. Actual service outages, degraded networks and load still need independent deployment testing.

## Dependency provenance and supply-chain boundary

The default generated-project and framework CI run a fail-closed direct and
transitive NuGet advisory check and export an SPDX 2.3 NuGet-only inventory
from the restored default solution. Offline negative test fixtures reject
missing/invalid audits, vulnerabilities and missing package resolution. The
integration-only project, OCI image digests, signed releases, provenance
verification and production deployment remain out of scope.

## Recovery and release governance

The Platform owns only generic technical persistence; it does not own an
organization's recovery policy or business schema. CI exercises native
PostgreSQL/SQL Server backup and restore against disposable fixtures with a
negative safety guard, but does not replace owner-specific production DB
restore tests or an approved DBA runbook. The NuGet supply-chain gate also
separately audits the optional real-infrastructure test project's packages;
publicly signed OCI releases and immutable base-image pins remain open; template CI now has an actual runtime OCI scan and signed NuGet package provenance.

## Database tenant enforcement / release trust boundary

The Platform owns only provider-specific tenant policy SQL and trusted
transaction binding, never Identity membership or a context's own tables.
PostgreSQL FORCE RLS and SQL Server filter/block policy syntax are tested
with actual provider engines against raw and EF bulk mutations. Separate
business migrations and least-privilege runtime access remain required.
The generated CI adds a Trivy runtime image security gate; release
signing of the template package is handled by the upstream repository,
not automatically by each new product. Read
[ADR-005](decisions/ADR-005-tenant-row-security.md).

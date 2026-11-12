# Domain-neutral ABA starter

This is a **starter** for a new API product, not a sample academy/enrollment domain. The API host is the composition root, not a business owner.

## Current implemented baseline

- .NET 10 minimal API
- generated OpenAPI and Development-only interactive Scalar API reference
- liveness health check, correlation ID response header, Problem Details exception handling
- built-in fixed-window global request rate limiting (per process and remote IP; gateway controls are still needed at scale)
- fail-closed non-Development OIDC/JWT configuration and authorization fallback policy
- xUnit integration and security-configuration tests, plus opt-in real PostgreSQL/SQL Server/Redis tests
- AI agent and test documentation

## Adding your first real context

Name the context using actual business language. Record its owning data, rules, decisions and versioned contracts in `metadata/contexts.json`. Then create appropriate Domain, Application, Infrastructure and Contracts projects, register their services in the Host, and add dependency-ownership tests. Do not create shared domain entities.

## Not delivered by this starter yet

PostgreSQL/EF Core 10, SQL Server/EF Core 10, Wolverine relational persistence wiring and Redis distributed caching are now implemented as **optional** technical modules. A checked-in technical idempotency migration is present. End-to-end transactional business messaging, tenant isolation, load testing, automatic deployment and production hardening remain unimplemented. These remain separate platform milestones and must not be asserted as installed.

## Security

The host does not ship a JWT signing key or an identity provider. For non-Development startup, provide `Authentication:Authority` (a trusted HTTPS OIDC issuer URL) and `Authentication:Audience`. Signature, issuer and audience validation then use the JWT bearer middleware. Verify provider-specific configuration and authorization policies before exposing public APIs. All non-explicitly-anonymous routes require authentication.

## Authentication gate verification

The generated API includes a deliberately protected `GET /api/v1/system/authorization-check` endpoint. A request without a valid bearer token returns `401` **even in Development**. This does not replace full OIDC integration, tenant-isolation or signed-token claim tests. Documentation UI is Development-only, and production startup still requires HTTPS authority/audience.

## Optional tenancy-awareness (not tenant data isolation)

Set `Platform:Tenancy:Enabled=true` to require a trusted tenant claim on every authenticated API endpoint, via both the default and fallback JWT authorization policies. `Platform:Tenancy:ClaimType` defaults to `tenant_id`. `IRequestTenant` reads only the principal populated by authentication middleware; a caller's `X-Tenant-ID` header is never accepted as proof of membership. The option is **disabled** for the clean Single-Tenant quickstart. Actual tenant authorization, record filtering, migrations, per-tenant storage, cross-tenant negative database tests, and account membership belong to the real owning business contexts/provider; **do not advertise production tenant isolation** yet.

## Signed JWT validation tests

The default xUnit suite injects a disposable test-only key into the actual ASP.NET Core bearer middleware and exercises signature, issuer, audience and lifetime checks. These tests do **not** connect to a real Keycloak/Entra/Okta OIDC provider or certify token key rotation, per-tenant membership or permission claims. Read `docs/decisions/ADR-002-verified-token-test-boundary.md`.

## Container build and runtime boundary

`Dockerfile` is multi-stage (.NET 10 SDK build and ASP.NET runtime) and uses the non-root `APP_UID`. `compose.yaml` provides an ergonomic **loopback-only Development** API, not a public deployment. The root repository's `scripts/verify-container-starter.sh` generates a fresh named API, builds the OCI image, confirms Production rejects missing OIDC configuration, verifies a non-root process and tests Health, OpenAPI and Scalar through the actual container. A production release must still configure trusted HTTPS OIDC, TLS termination, secrets, service accounts, firewall, migrations, resource limits and an appropriately secured deployment target.

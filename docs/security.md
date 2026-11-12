# Phase 1 security boundary

**Never treat this preview as an authenticated API.** OAuth 2.0, OpenID Connect, access-token validation, tenant-aware policies, distributed secrets, webhook signature validation and audit/security telemetry are not implemented. The Host explicitly throws when `ASPNETCORE_ENVIRONMENT` is not `Development`. Bind HTTP to `127.0.0.1` and use only synthetic demo users and fake payments.

Before any production deployment, require standards-based OIDC/JWT validation, granular authorization, tenant identity/isolation, CSRF and CORS decisions, request limits and rate limits, secure secret loading, migrations, backup/recovery, threat modeling, monitoring, vulnerability checks, real provider idempotency and webhook signature verification, and a deployment security review. Follow the next milestone acceptance gates.

## Security of the new generic API template (not Academy)

The generated API implements ASP.NET JWT bearer middleware, fails closed outside Development without an HTTPS Authority/Audience, requires authentication by default, and includes an explicit protected route with a negative `401` test. Optional `Platform:Tenancy:Enabled` checks a JWT `tenant_id` claim in the default/fallback authorization policies; it never accepts a raw tenant header as authority. A real disposable Keycloak OIDC/JWKS CI test verifies signed-token acceptance and audience/tenant-claim denial, and EF tenant query/write guards are tested on both SQL providers. These checks are **not full production identity/membership management, key rotation, SQL Row-Level Security or protection against raw/bulk SQL operations**. Do not deploy multi-tenant data without the owning business context implementing and testing tenant membership and row-level access.

## Generic API supply-chain boundary

The framework and new default generated-project CI now have verified
fail-closed NuGet direct/transitive vulnerability checks and SPDX 2.3
resolved-package inventories. Missing advisory sources and missing
project/restore evidence must fail. The optional external integration
test project, OCI image digest pinning, container scanning, release
signature/provenance verification and public deployment are **not**
covered by this new gate. These remain independent P0 release tasks.

## Safe recovery-drill execution

Database-native recovery tests perform create/drop/restore operations and must
never be used against a customer or production database. They refuse execution
without `CI=true`, `ABA_ENABLE_DESTRUCTIVE_CI_DRILLS=yes` and a loopback
`aba_ci` fixture, then create uniquely named throwaway databases only.
The generated project's normal tests exclude the `RecoveryDrill` category.
Production backup/restore requires its own approved runbook, least-privilege
credentials, encrypted isolated staging and human change control. See
[generated-project recovery guide](../templates/aba-api/docs/disaster-recovery.md).

**CI verification:** [run 43313417455](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455) confirms both disposable database recovery drills and independent NuGet scans; no real production restore, image trust or release attestation was certified.

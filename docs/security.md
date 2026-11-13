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

## Generic template: database-level tenant protection

Opt-in provider RLS and trusted transaction-bound tenant propagation are
now implemented as technical Platform components. A policy with verified
DB row predicates can block cross-tenant raw SQL and EF bulk writes even
when EF query filters are ignored. **This does not implement user-to-tenant
membership, prevent SQL that maliciously changes session context or
authorize migrations on business tables.** Production requires limited
DB roles and owning-context identity policies. See
[ADR-005](../templates/aba-api/docs/decisions/ADR-005-tenant-row-security.md).

## Container and release provenance

Repository CI now defines a commit-pinned fixable-CRITICAL Trivy runtime
image check and a main-only GitHub/Sigstore provenance attestation for
the previously tested NuGet package with a modified-artifact rejection
test. Publication identity, base-image digest pins and Production
deployment/secret rotation are not delivered. See
[release-integrity.md](release-integrity.md) and the latest execution run.

**Latest executed release and database-security evidence:** [8-job successful CI run 43317295032](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43317295032) verifies both provider-level RLS fixtures and SQL bulk denial, a Trivy final-runtime vulnerability gate and signed NuGet template provenance. Real business tenant membership, privileged database bypass review, published OCI signatures and approved Production deployment remain incomplete.

## Audit signing and poison-message operator boundary

The generic technical audit system is off by default and requires
externally provided HMAC signing keys and relational persistence.
Opaque identifiers limit payload exposure, but the system still
needs separate legal retention, production key custody and an
immutable external digest anchor.

Dead-letter management has no public endpoint or default admin
identity; replay requires an authenticated operator permission,
change ticket and exact envelope identity and is pre-audited.
Tenant-scoped operator access is blocked until independently
authorized message stores are implemented. Replaying does not
guarantee business idempotency, payment safety or compensation.

## Separately signed audit prefix anchors (new technical primitive)

The platform now exposes an UNSIGNED audit head/sequence candidate
after validating the full HMAC chain. A separate offline custodian can
sign the candidate with a dedicated key unrelated to the ledger HMAC
ring; VerifyAnchoredPrefixAsync checks that signed historical digest
under a repeatable-read SQL snapshot even after new legitimate
events arrive. This protects an archived prefix against database
tail truncation or older row changes only when the signed checkpoint
is stored in independent immutable custody. No HSM, WORM archive,
external checkpoint store or offline signing credentials are bundled.
Keep the external key out of the application and ordinary CI logs.

**Real fail-closed fault execution:** [CI 43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289) confirmed that the technical process-kill fixture cannot target a remote/production database and that a separately committed synthetic effect survives lost ACK redelivery without duplication. This does not establish live operator identities, authenticated business memberships or KMS/WORM custody.

## OCI base locking and scanned artifact provenance

Immutable multi-platform .NET 10 SDK/runtime base digests now have a
reviewed lock and fail-closed source/generated CI validation.
The root main-branch pipeline signs and verifies the **Docker-save
archive of the scanned runtime image**, including an intentionally
modified archive that MUST fail provenance validation. This is not an
OCI registry signature or Production approval, and does not replace
fresh vulnerability scans as the pinned base ages. Generated products
must individually sign their actual published digest. See
docs/release-integrity.md and the template's ADR-011.

**Signed artifact gate:** signing the scanned Docker-save image archive is now deferred to the final provenance job. All eight upstream CI jobs (including the isolated actual Redis restart and full SQL/Wolverine tests) must pass before that artifact receives SLSA provenance. A partial/passing image-scan job alone cannot issue a signed archive. This still is not a published registry OCI signature.

## Shared Redis API quotas (generic template)

`api-shared-limits` is opt-in, alongside the local HTTP limiter. When enabled
outside Development, the API now refuses a Redis connection configuration that
omits `ssl=true` or explicitly sets `ssl=false`; its unit regression tests also
require forged tenant headers and unauthenticated identity claims to leave the
anonymous TCP-peer partition unchanged. Real Redis two-replica 429/503 and
spoofed-header tests remain isolated in CI. This is a transport configuration
guard, **not** certificate-chain verification, authenticated Redis ACL approval,
reverse-proxy identity policy, business membership or production readiness.
Those security-owner/deployment gates remain open under
[ADR-012](../templates/aba-api/docs/decisions/ADR-012-shared-redis-rate-limits.md).

## Validated-principal composition in tenant and quota guards (Queue C)

The generic tenant-claim resolver now reads only the single authenticated
`ClaimsIdentity` supplied by a verified authentication middleware pipeline;
it must never pair a trusted authentication flag with a tenant claim from
an unauthenticated secondary identity. Multiple authenticated identities are
treated as ambiguous and rejected for tenant resolution. Even inside one
validated identity, two claims of the configured tenant type (including two
identical values) are rejected rather than selecting the first; signed tenant
membership is still a separate owner-approved policy. The local and
shared Redis rate limit partitioner uses the same rule for subject and tenant,
falling back to the actual TCP peer if no unambiguous subject exists. The peer address is canonicalized for IPv4-mapped IPv6 so dual-stack presentation cannot double anonymous request budgets; native IPv6 and unambiguous authenticated JWT identities stay independent. This
avoids a forged secondary identity creating extra quota partitions. A genuine
signed JWT with its tenant claim remains valid; unsigned forwarded/tenant
headers and unrelated claims are not authority. A single authenticated identity must also have exactly one subject claim across `sub` and the mapped name-identifier claim to receive a JWT-partitioned quota. Duplicate or conflicting identifiers use the actual TCP peer for both local and shared Redis quotas, not an arbitrary first subject. This is conservative rate limiting, not membership authorization.

[The focused tenant-policy and quota tests](../templates/aba-api/tests/Aba.Starter.Api.Tests/AbaPlatformTests.cs)
and [quota partition tests](../templates/aba-api/tests/Aba.Starter.Api.Tests/RateLimitTests.cs)
include successful valid identities, forged split-identity denials, ambiguous
authenticated identities and unchanged trusted/peer partitions. Exact PR
head checks must pass before calling this revision verified. **This does not
grant roles or prove organization membership, production RLS, SQL session
spoofing prevention or Redis gateway/ACL approval.**

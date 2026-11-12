# ABA .NET 10 — generic platform hardening and verified boundaries

**Date:** 2026-11-12  
**Reference successful CI:** https://github.com/smael-aftersearch/ABAFramework/actions/runs/43298754432  
**Source commit in that run:** `1807a115aa7781eb421a89dc406d8315842fe2da`  
**Verification scope:** domain-neutral template. `samples/Academy` is a separate optional application.

## Tested against actual services

- 44 default API xUnit tests PASS, 0 failed. An ephemeral generated project was compiled in .NET 10 Release.
- 13 real-service integration tests PASS, 0 failed: PostgreSQL and SQL Server EF schema/idempotency, Redis scoped cache, Wolverine handler, EF + Wolverine transaction/outbox with injected pre-commit rollback, scheduled delivery across a clean host restart, and actual EF tenant query/write denial on both databases.
- Real isolated Keycloak OIDC discovery and JWKS PASS. HTTP assertions PASS: signed tenant-bearing token 204; missing, malformed or wrong-audience token 401; valid token with no tenant claim 403.
- Real non-root .NET 10 OCI image startup PASS; Health/Scalar/OpenAPI work; Production without trusted OIDC configuration fails closed.
- Versioned `ABAFramework.GenericApi.Template.0.2.0-preview.1.nupkg` CI package installation PASS; generated API compiles and its default suite passes. Package is attached to GitHub Actions, not published to NuGet.org.
- Root ABA governance, AI metadata validation, Academy reference smoke and template generation checks PASS.

## Continued generator improvement

The independently tested `tools/new_context.py create --name ActualContext` scaffolds owned Domain, Application, Contracts and Infrastructure projects, tests, metadata and host wiring without inventing business data. A later `--with-db` mode scaffolds an empty owning DbContext with PostgreSQL/SQL Server provider wiring, and has passed generated-project Build and provider-choice tests. Full CI for those later commits must be evaluated by its exact run status, not assumed from the earlier successful five-job baseline.

## NOT YET proven or delivered

- Actual customer-specific domain contexts or full CRUD business APIs: requirements and ownership have not been provided.
- Abrupt process crashes at each Outbox stage, poison/dead-letter replay, concurrent delivery, external message broker and real business handler invariants.
- Production identity-provider organization memberships/roles, key rotation, MFA policy, tenant database RLS and raw/bulk SQL isolation.
- Safe migration/rollback and data backup restoration on both supported SQL providers, versioned release/upgrade compatibility, performance/chaos/stress audits.
- Production observability collector ingestion, operating dashboards, cloud deployment, secret/KMS operations, vulnerability/threat-model review and service-level readiness.

Passing technical probes is meaningful evidence but **not a declaration of production readiness**.

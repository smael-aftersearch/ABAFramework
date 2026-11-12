# ABA API platform: real PostgreSQL, SQL Server, Redis and Wolverine verification

**Date:** 2026-11-12  
**GitHub Actions:** https://github.com/smael-aftersearch/ABAFramework/actions/runs/43290885904  
**Verified source commit:** `ad4e74ea284d3a65bbc6585ec9c003d6c1317ec3`

## Concrete checked behavior

The CI generated a fresh domain-neutral project from `templates/aba-api`, built it with .NET 10 and ran an independent integration job against actual Docker service containers. The integration job included:

| Platform | Test | Result |
| --- | --- | --- |
| EF Core 10 PostgreSQL | Migration and unique idempotency receipt with distinct scopes | PASS |
| EF Core 10 Microsoft SQL Server | Same migration and uniqueness assertions | PASS |
| Redis | Scoped JSON write, read, namespace independence and invalidation | PASS |
| Wolverine 6 + PostgreSQL | Development-provisioned relational persistence, durable local technical message handler writes EF receipt | PASS |
| Wolverine 6 + SQL Server | Same real message handler and EF receipt flow | PASS |
| Generic API | Fresh named template, Release build, xUnit, local HTTP health, generated OpenAPI, interactive Scalar | PASS |
| ABA governance and AI | Python structural checks and negative tests | PASS |

A later commit added an optional authenticated JWT tenant-claim guard and its xUnit tests. Verify that commit's corresponding Actions run before treating that additional test as PASS.

## What these tests do NOT prove

- Wolverine behavior after a crash in the precise window between domain commit and message send
- Duplicate callback/replay/dead-letter correctness or migration rollback across each SQL provider
- Real business context EF models, end-to-end per-context ownership, or concurrent transaction anomaly handling
- Signed OIDC identity-provider integration, membership validation and tenant **data row** isolation
- Redis outage recovery, cache stampede protection, multi-instance invalidation, or production OTLP collector
- Production deployment, security audit or live traffic capacity

Every adapter is **optional**; `Platform:Database:Provider=None`, in-memory cache, Wolverine off, OpenTelemetry off and tenancy off remain quick-start defaults.

## Reproduction

```bash
dotnet new install ./templates/aba-api
dotnet new aba-api -n Example.Api -o ./out
bash scripts/verify-starter.sh

# For real integration, provision services:
docker compose -f infra/compose.platform.yaml up -d
# Set ABA_TEST_POSTGRESQL, ABA_TEST_SQLSERVER, ABA_TEST_REDIS
bash scripts/verify-platform-services.sh
```

Credentials in Compose are throwaway localhost-only development fixtures and not for production.

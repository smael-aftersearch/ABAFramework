# ABA generic API — independent NuGet audit and provider-native recovery evidence

Date: 2026-11-13 (Europe/Istanbul)
GitHub Actions: https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455
Verified commit: `3e8f18552d9b64bc9da21af1433b62d51b1f6c44`

## CI results actually observed

All six GitHub Actions jobs passed.

| Gate | Exact evidence |
| --- | --- |
| Default generated API | 73 xUnit tests, Release build, HTTP checks |
| Real services | 16/16 PostgreSQL, SQL Server, Redis and Wolverine integration tests |
| Provider-native recovery | 7/7 tests: PostgreSQL 16 `pg_dump` + `pg_restore`, SQL Server `BACKUP DATABASE` + `RESTORE VERIFYONLY` + `RESTORE DATABASE`, plus CI-only connection safety guards |
| NuGet vulnerability/SBOM gate | 16 offline positive/negative tests; 161 resolved default solution packages and 104 resolved external integration-project packages, each with its own SPDX 2.3 manifest |
| OIDC | Real Keycloak JWKS and bearer JWT authorization checks |
| Distribution and container | Non-root container, versioned NuGet template package, and ABA governance |

The recovery fixture creates and removes disposable databases. Both providers
back up a marker (42), mutate it to 99, restore their native backup, and
assert recovered 42; SQL Server additionally verifies its backup checksum.
The first CI execution exposed a mismatch between a newer `pg_restore`
client and the PostgreSQL 16 server. The final passing implementation uses
the matching `postgres:16-alpine` client image inside CI Docker.

## Security and operational boundaries

This verification does **not** demonstrate production business-schema backup,
point-in-time restoration, full migrations rollback, encryption and offsite
retention, operator RPO/RTO, tenant-safe cross-context recovery, release
attestation, OCI image scanning, or external secret/key rotation.
The recovery drill refuses non-loopback or non-`aba_ci` connections and
requires `CI=true` with `ABA_ENABLE_DESTRUCTIVE_CI_DRILLS=yes`.
No Production database was targeted.

Authoritative status: `docs/implementation-status.json` and the generated
`docs/IMPLEMENTATION-STATUS.md`. Operator guide:
`templates/aba-api/docs/disaster-recovery.md`.

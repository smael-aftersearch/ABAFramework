# ABA technical database RLS + signed package provenance — CI evidence

Reviewed on 2026-11-13 (Europe/Istanbul).
Source commit: `bffe4efc341a9c34a2472b686c81e5033aa8f2e8`.
Verified [GitHub Actions run 43317295032](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43317295032).

## Actual CI execution

All **eight** jobs completed successfully. In the generated `dotnet new aba-api`
starter, **81/81 API xUnit tests** passed, alongside **18/18 live
PostgreSQL/SQL Server/Redis/Wolverine integration tests** and **7/7
isolated database native recovery/safety cases**.

PostgreSQL RLS used a **non-superuser role**, enforced `FORCE ROW LEVEL
SECURITY`, and rejected reads/writes outside the transaction tenant.
SQL Server FILTER + INSERT/UPDATE BLOCK policies similarly prevented
unbound reads, raw cross-tenant updates, forged inserts and
`ExecuteUpdate`/`ExecuteDelete` on a technical fixture table.
The SQL Server test user was the disposable CI administrator, not
a production least-privileged workload identity.

The container-security job built the actual final `aspnet:10.0` image,
ran commit-pinned Trivy, and passed the **fixable CRITICAL** vulnerability
gate. Evidence includes JSON vulnerability results and the local image ID.

The package-provenance job downloaded the tested
`ABAFramework.GenericApi.Template.0.3.0-preview.2.nupkg`, recorded a
SHA256 digest, signed an SLSA provenance statement with a GitHub Actions
OIDC/Sigstore certificate, published it to the Rekor transparency log,
and verified it with the GitHub CLI. A deliberately modified package
with the same filename failed the verification gate as required.
[Attestation record 66918425](https://github.com/smael-aftersearch/ABAFramework/attestations/66918425).

## ABA impact and unresolved acceptance checks

- **Owner:** Platform supplies technical DDL/session-scoping and verified
  test infrastructure. Business/Identity owners are still responsible for
  tenant membership, public contracts and actual data ownership; DBA
  owns SQL migrations and least-privilege grants.
- **Contracts:** no business V1/V2 published contract was created/changed.
- **Test boundaries:** raw SQL denial is proven for the verified
  tenant-session value; an SQL-injection attacker that can change
  `SESSION_CONTEXT` or `set_config` is **not** contained by these
  generic predicates. Real product roles and injected membership checks
  must still be tested.
- **Release boundaries:** no OCI base digest pins, HIGH/unfixed vulnerability
  escalation approval, published signed OCI/NuGet release, public hosting
  credentials, real customer database or Production deployment.
- **Rollback:** the DBA removes owner-specific RLS policies through
  `AbaTenantRlsPolicies.Remove`, not the shared SQL Server predicate
  function without checking other consumers.

The source of truth for status remains `docs/implementation-status.json`
and generated README/`docs/IMPLEMENTATION-STATUS.md` tables.

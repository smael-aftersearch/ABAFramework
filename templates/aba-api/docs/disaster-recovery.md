# Provider-native recovery drills and production boundary

This is a **technical starter runbook**, not approval to back up, overwrite,
replay or restore production/customer data. The generic API template owns only
technical infrastructure. Business data, migration plans and Recovery Point /
Recovery Time Objectives (RPO/RTO) must be approved by the owning context and DBA.

**Framework CI verification:** [run 43313417455](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455)
passed all 7 native recovery/safety tests and 16 normal real-service tests.
A new generated product must still run its own infrastructure and recovery
workflows before claiming equivalent evidence.

## What the automated drill proves

The external real-service xUnit suite contains
`tests/Aba.Starter.Platform.IntegrationTests/BackupRestoreDrillTests.cs`.
It performs provider-native operations only on *new disposable databases*:

| Provider | Native recovery evidence | Expected invariant |
| --- | --- | --- |
| PostgreSQL 16 | matched-version `pg_dump -Fc` and `pg_restore` inside `postgres:16-alpine` | a marker backed up as 42, mutated to 99, is recovered as 42 in a different disposable database |
| SQL Server 2022 | `BACKUP DATABASE ... WITH CHECKSUM`, `RESTORE VERIFYONLY`, then `RESTORE DATABASE ... WITH REPLACE` | original 42 returns after source mutation to 99 |

The SQL Server `WITH REPLACE` operation targets **only the newly created CI
fixture**, not the pre-existing CI database, and is never a production command.
Both tests remove their disposable databases; backups remain transient to CI.
This deliberately does **not** prove real business schema migrations, binary
backup integrity at scale, WAL/log shipping, partial recovery, durability
across a cluster, backup encryption or operational restoration times.

## Safety: do not reuse CI credentials or bypass guardrails

Recovery cases require all conditions below; if one is absent, the tests
reject execution rather than modifying a database:

1. `CI=true` and `ABA_ENABLE_DESTRUCTIVE_CI_DRILLS=yes` are both set
2. The PostgreSQL connection targets `localhost:5432`, database `aba_ci`,
   user `aba_ci`; SQL Server targets `localhost,1433`, database
   `aba_ci`, user `sa`
3. Names for create/drop/restore are generated afresh with a restricted
   `aba_ci_recovery_` or `aba_ci_restored_` prefix, never from HTTP input
4. Test execution is intentionally restricted to ephemeral service containers

The ordinary default `dotnet test` and the external integration test runner
exclude the `RecoveryDrill` category. In the main repository, the real
service CI job invokes the category **only** after provisioning disposable
PostgreSQL and SQL Server. A generated application's manual integration
GitHub Actions workflow does the same.

For a disposable CI clone with all three `ABA_TEST_*` connections configured:

```bash
# Standard non-destructive external-service suite:
dotnet test tests/Aba.Starter.Platform.IntegrationTests/Aba.Starter.Platform.IntegrationTests.csproj --filter 'Category!=RecoveryDrill'

# Run ONLY in the GitHub Actions disposable service-container fixture:
CI=true ABA_ENABLE_DESTRUCTIVE_CI_DRILLS=yes \
dotnet test tests/Aba.Starter.Platform.IntegrationTests/Aba.Starter.Platform.IntegrationTests.csproj --filter 'Category=RecoveryDrill'
```

The recovery command requires Docker access for a PostgreSQL 16 client image,
an ephemeral file-mount directory and permission to create/drop isolated
databases. It must never run on a production or shared administrative host.

## Production recovery checklist — deliberately NOT automated

1. **Define business ownership:** enumerate each context's database/schema,
   outbox, inbox, idempotency and operational secrets; agree on RPO/RTO,
   retention, legal holds and disaster classifications.
2. **Secure backups:** use a managed backup service or a DBA-reviewed native
   schedule, isolated service identity, encrypted transfer/storage, immutable
   offsite retention, tested key restore and dual-control deletion permissions.
3. **Preflight restore:** use a completely separate destination, independently
   approved secrets/network policy and a documented restore point; **never**
   restore over the active production instance while triaging an incident.
4. **Verify logical integrity:** check migrations/schema version, row-count
   or checksums for context-owned tables, idempotency receipts, pending and
   dead-letter Wolverine envelopes and tenant boundaries.
5. **Reconcile messages and external side effects:** restore does not reverse
   real external charges or HTTP side effects; replay needs owner-approved
   idempotency/compensation and backpressure controls.
6. **Exercise failure cases:** corrupted/missing backup, expired keys, failed
   migrations, rollback, broken WAL/log chain, delayed replicas, partial
   restore and cold startup. Record actual RPO/RTO and fail the gate if missed.
7. **Document approval and evidence:** record operator identity, exact source
   and target, restore timestamp, hash/checksum, measured timings, post-restore
   smoke tests, and a rollback decision. Keep secrets out of reports.

The production checklist and provider-native commands must be reviewed for
the chosen hosting and database versions; none of the above is delivered as
a production automation or a signed release.

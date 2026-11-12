# Safe SIGKILL fault-injection drill

Applies to the domain-neutral .NET 10 generic API technical
test suite only. This guide does NOT approve Production testing.
Read ADR-008 before changing the fault-injection boundaries.

## Accepted CI fixture

Use the dedicated PostgreSQL 16 and SQL Server 2022 containers,
each with an aba_ci database on loopback, with CI=true and
ABA_ENABLE_DESTRUCTIVE_CI_DRILLS=yes. PostgreSQL must be
localhost:5432 with Username=aba_ci; SQL Server must be
localhost,1433 with User Id=sa for the ephemeral CI image.
No external address, other database, user-supplied PID or
unsupervised host process is allowed.

The generated project's manually dispatched integration
workflow provisions these containers. The root framework CI
uses scripts/verify-platform-services.sh after creating and
building a fresh generated starter. Ordinary tests exclude
both RecoveryDrill and CrashDrill.

## Phases

1. The parent xUnit test starts a new nested dotnet test
   process and verifies its unique marker under the OS
   temporary directory.
2. Pre-commit phase: child writes a technical row with EF
   SaveChangesAsync within an uncommitted SQL transaction.
   Parent SIGKILLs this exact child. A separate DB context
   checks that the record was rolled back.
3. Durable scheduling phase: child ScheduleAsyncs a
   technical Wolverine message due in 12 seconds, waits
   for that call to complete, then records its marker.
   Parent SIGKILLs the child. A new independent host
   must process the persisted envelope and create a
   technical idempotency receipt. The test checks exactly
   one matching receipt.
4. Negative test rejects missing explicit CI flag, wrong
   provider, remote host, and production database names.

These tests do not modify arbitrary product state or
create a business API endpoint. The restored hosts use
only disposable CI services.

## Not yet proven

- Process kill immediately between the SAME business
  EF transaction COMMIT, Outbox flush, broker ACK and
  consumed Inbox acknowledgement.
- Redelivery of one already-effected message after
  losing the consumer ACK and true external side-effect
  idempotency.
- Live broker partitions, multi-node fencing, Production
  failover or contractual RTO/RPO.

No output from this drill is proof of these properties.

## New pre-ACK redelivery case

A third explicit child mode sends a test-only AbaCrashAckProbe to
the durable local queue. Its handler commits two independently
scoped technical records: a unique execution-attempt ID and a
deduplicated effect ID. A marker appears only AFTER the effect
has committed; the handler then blocks BEFORE Wolverine can ACK.
The parent SIGKILLs the exact worker process, starts another host
and requires at least two attempt records with exactly one effect.
This distinguishes genuine re-delivery from a mere restart.

The marker is gated by CI=true, the drill opt-in and the temp
directory restriction. The test remains outside normal xUnit.
No real business side effect or callback can be invoked. A failure
in this case must be investigated, not reclassified as a pass.

## Actual reviewed execution

[Source GitHub run 43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289) passed all 8 process-kill cases (two providers, three crash boundaries, plus explicit safety and sentinel tests). The child was killed after a verified fault marker and a restarted host recovered the persisted message. In the ACK case, a second delivery attempt appeared while only one idempotent effect remained. No actual business payment, published domain event or broker was invoked.

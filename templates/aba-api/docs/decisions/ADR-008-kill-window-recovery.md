# ADR-008: Real process-kill recovery gates on both SQL providers

Status: Technical CI acceptance (not full business transaction certification)
Owners: Platform / Operations, never a generic business context
Date: 2026-11-13

## Decision

A graceful ASP.NET shutdown or injected exception is NOT evidence that
PostgreSQL, SQL Server, EF and Wolverine survive abrupt process death.
The independent CrashDrill category launches an actual child dotnet testhost
process and kills that process tree after a per-case marker confirms a
controlled fault boundary.

1. **SQL flushed but uncommitted:** child starts a real SQL transaction,
   inserts an idempotency receipt and calls SaveChangesAsync, then
   records a marker without committing. Parent SIGKILLs the process
   and checks there is NO receipt after the database drops the
   connection. This is the pre-COMMIT negative case.
2. **Scheduled Wolverine envelope persisted:** child starts a
   real durable Wolverine host, ScheduleAsync returns for an
   AbaDurabilityProbe due 12 seconds later, then records a marker.
   Parent SIGKILLs the entire worker tree before delivery. An
   independently started host using the same relational store
   must process the scheduled envelope into one technical receipt.
   This is the post-schedule-durability recovery case.

3. **Committed effect but consumer ACK withheld:** a synthetic
   AbaCrashAckProbe writes one externally committed technical effect
   receipt and a unique delivery-attempt receipt in a separate EF
   scope. The handler signals the parent then deliberately parks
   **before returning to Wolverine**, preventing its ACK. The parent
   SIGKILLs the worker, starts a new host, requires at least a second
   recorded attempt from persisted redelivery and checks the
   idempotency effect still has exactly one receipt. This models
   a lost consumer ACK after an independently committed effect,
   without using any real business side effects or public route.

4. **Committed transactional outbox while RabbitMQ is unavailable:** an
   explicitly gated producer child starts against PostgreSQL and the exact
   labeled disposable RabbitMQ container. After the parent pauses that
   container, the child commits a synthetic technical receipt and
   outbound message together through Wolverine's scoped EF Core
   IDbContextOutbox (rather than an inline handler invocation). Wolverine
   may still await a broker ACK when flushing *after* the SQL COMMIT, so the
   child starts SaveChangesAndFlushMessagesAsync without awaiting its
   transport flush and independently queries PostgreSQL for the committed
   technical receipt. Only after verifying the receipt and absent consumer
   effect does it signal the parent for SIGKILL. The parent SIGKILLs that producer, unpauses RabbitMQ, starts a new
   Wolverine host against the same SQL store and queue, and requires the
   persisted outbox message to produce one idempotent technical effect. A
   second envelope with the same logical key is then published to assert that
   delivery does not duplicate that effect.

The original three CrashDrill boundaries are verified separately on real
PostgreSQL 16 and SQL Server 2022 containers. The broker case uses the
PostgreSQL fixture only. Tests use unique GUID receipts and marker paths and
never register a new public HTTP fault route.

## Fail-closed safety constraints

Child and parent require CI=true AND
ABA_ENABLE_DESTRUCTIVE_CI_DRILLS=yes. The connection must target
localhost/127.0.0.1, database aba_ci, provider-specific CI user
(aba_ci for PostgreSQL; sa for the disposable SQL Server), and expected
ports 5432/1433. The marker lives under the system temp directory.
Only an exact child process created by the test is killed; no
external or user-provided PID is accepted. Negative cases reject
remote/production targets, wrong databases and missing opt-in.

The broker crash-window case additionally requires
ABA_ENABLE_BROKER_OUTAGE_DRILL=yes, a loopback RabbitMQ endpoint, the
workflow-provided container ID, exact `rabbitmq:4.4.0-management-alpine`
image and `aba.test.disposable=true` label. It uses only PostgreSQL's
disposable `aba_ci` database. The drill always unpauses the verified broker
and kills the child if cleanup begins while it is still running.

Normal external tests explicitly exclude CrashDrill and BrokerCrashDrill.
Root CI runs the isolated categories after regular infrastructure and
native backup/restore tests; generated products expose a manually dispatched
integration workflow with the same fail-closed gates. No auto-killing
ordinary developer or Production hosts.

## Boundaries and next steps

This verifies process death before a SQL commit, after Wolverine
durable scheduling, and after a technical effect commits before the
handler ACK. It is NOT an instrumented SIGKILL between the **same
business transaction's** commit, Outbox flush, broker publish and
consumer ACK, nor is the effect receipt a real external payment. It does not
prove double-delivery under ACK loss, exactly-once external effects,
network partitions, broker failover or a business consumer's
idempotency. Those acceptance gates remain open. A process-kill test
that fails must be repaired in the implementation, not suppressed by
disabling or downgrading the category.

A generated project must run this on its own explicitly authorized
ephemeral SQL services before promising equivalent reliability.

## CI-only crash-marker path safety (Queue B execution 8)

The synthetic SIGKILL worker now accepts only a fresh canonical
`child-ready.marker` directly inside an existing `aba-crash-<GUID-N>`
directory under the system temp root. Both CI/explicit-drill gates are
checked before SQL effects. Symlinked directories and pre-existing marker
files are refused; FileMode.CreateNew prevents clobbering sentinel files,
and the pre-ACK marker uses an exclusive staged file with a no-overwrite
move. The independent regression checks a legitimate atomic signal,
missing opt-in, arbitrary paths, repeated writes and symlink redirects,
while preserving an unrelated sentinel. This is an authored CI-fixture
hardening change requiring exact PR-head CI; it adds no production
message guarantee, business handler or external ACK coverage.

## First proven CI run

[Run 43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289) passed all 8 CrashDrill cases and 8/8 jobs. The SQL Server ACK-recovery case completed in 77 seconds; PostgreSQL in 79 seconds. Neither exercised Production privileges nor a context-owned real message contract.

## Broker outage plus producer SIGKILL (authored, awaiting exact-head CI)

`BrokerCrashDrill` is separate from both the regular integration suite and
the existing `BrokerOutageDrill`. It starts a RabbitMQ-connected synthetic
producer, synchronizes over a private local named pipe, pauses only the
verified disposable broker, and lets the child commit the technical
transactional-outbox command. The parent observes one command receipt and no
consumer effect, kills the exact producer child, unpauses RabbitMQ, and starts
a fresh host. Recovery must create one effect; a deliberately repeated
logical message must produce another attempt while leaving that effect count
at one. Both destructive-drill and broker-outage opt-ins are required.

This extends only technical persistence and recovery evidence. It does not
claim the same outcome for a business Outbox, a live handler, a consumer ACK
crash window, or an external provider side effect. Code and tests are authored;
the drill remains unverified until the exact change runs in isolated CI.

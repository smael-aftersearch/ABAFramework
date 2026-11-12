# Process-kill Outbox/Inbox fault windows and offline audit checkpoints

**Reviewed:** 2026-11-13 (Europe/Istanbul).
**Full baseline:** [GitHub Actions run 43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289)
**Executed commit:** `dd77a1c1b0f23c168d3353bcfc0112a186d11364`.
**Result:** all eight GitHub Actions jobs succeeded.

## Actual executable tests

- Generated default API: **99/99 xUnit tests passed**. The later
  fail-before-DB-change ACK safety check adds two further xUnit
  cases; this baseline predates those two tests.
- Real SQL Server, PostgreSQL, Redis and Wolverine: **26/26** tests.
  Signed audit checkpoint integration checks passed on both SQL engines.
- Destructive native backup/restore and safety: **7/7** tests.
- Real SIGKILL CrashDrill: **8/8 tests**. Six child-crash cases
  exercise two providers times three boundaries, plus a negative
  gate test and a child test runner that exits immediately outside
  explicitly injected CI mode.

The actual fault boundaries, observed separately for each provider:

1. **Uncommitted EF flush:** parent SIGKILLs its own child after
   SaveChangesAsync but before SQL COMMIT; a fresh EF context sees
   **zero** matching technical receipts.
2. **Persisted delayed Wolverine message:** parent kills a fresh
   child after ScheduleAsync returns but before the message is due;
   a newly launched Wolverine host consumes the original durable
   envelope and creates **exactly one** technical receipt.
3. **Committed effect before ACK:** the test-only handler commits
   one idempotent effect in an independent scope and records an
   attempt, then blocks before returning/ACK. Parent SIGKILLs
   the process; a fresh host records another delivery attempt.
   After verified re-delivery, the effect receipt count remains
   **exactly one**. The SQL Server case took about 77 seconds
   and the PostgreSQL case about 79 seconds.

No arbitrary PID or production database is accepted: the exact
loopback aba_ci and CI=true / explicitly enabled drill gates are
mandatory. The tests use synthetic technical messages only,
not customer payments, production handlers or public fault APIs.

## Offline checkpoint evidence

The separately signable checkpoint binds the verified tenant,
historical sequence and HMAC digest to a distinct offline key
and UTC timestamp. Fast tests reject wrong signing keys, modified
tenant/digest/sequence and forged signatures. Both real SQL engines
prove that later valid appends are permitted, but deleting or
rewriting an anchored historical prefix fails verification.
No external WORM/KMS account or immutable offsite sink was deployed.

## ABA architecture and release boundaries

- **Owner:** Platform only owns testing/technical mechanisms;
  real business contexts own event contracts, authorization,
  idempotency and compensation. No business V1 contracts changed.
- **Outbox vs Inbox:** the ACK-loss test is deliberately a
  *separately committed synthetic effect*, not a real customer
  transaction that atomically commits business state plus
  published event. Actual producer COMMIT/publish race and
  cross-context/broker failures remain uncovered.
- **Production:** signed operator identities, tenant-specific
  message stores, immutable checkpoint custody, audit retention,
  encryption/secrets, business recovery plans and release approval
  remain required. Offline checkpoint signing keys must be
  separated from the live app's ledger HMAC keys.
- **Rollback:** never drop the audit technical table or its
  forensic evidence without documented retention approval.

A later commit additionally changes ACK fault-marker publication
to an atomic file move and moves marker security validation before
all DB effects. Confirm that commit's own CI separately; do not
silently attribute later tests to this earlier run.

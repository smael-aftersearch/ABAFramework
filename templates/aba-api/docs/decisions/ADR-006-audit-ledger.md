# ADR-006 — HMAC linked technical audit ledger

**Status:** Technical implementation; Production auditing requires owner approval.
**Owner:** Platform owns the integrity mechanism only. Business contexts own the
actor-to-tenant authorization, auditable operations, retention and privacy.

## Decision

An opt-in SQL audit ledger stores opaque tenant, actor, action, outcome and
correlation IDs, UTC timestamp, HMAC key ID, sequence, previous digest and
current digest. The technical table is created by the separate
\`202611130001_AuditLedger\` migration on PostgreSQL or SQL Server.

\`AbaAuditLedger.AppendAsync\` uses a SERIALIZABLE transaction per append.
The per-tenant record sequence and HMAC-SHA256 predecessor digest form
a tamper-evident chain. UTC is normalized to microsecond precision before
signing because PostgreSQL timestamp(tz) truncates .NET 100ns ticks.
The service retries only known concurrent insertion, serialization and
deadlock failures, not arbitrary database exceptions.

\`VerifyAsync(tenant)\` detects changed/forged/missing intermediate records.
The overload with \`expectedHeadDigest\` also detects deletion of the
suffix **when the expected digest is independently stored outside this DB**.
Without this anchor, deleting a final suffix is undetectable. These
mechanisms are tamper-evident, not WORM or proof against a compromised
signing key.

## Keys and startup

The default generated API has auditing OFF and needs neither DB nor secret.
When opted in, it requires a relational database, an external base64
HMAC keyring (minimum 32 bytes per key), and a \`PrimaryKeyId\`.
Invalid/missing key material fails startup. Key rotation changes the
primary signer while retaining old key IDs for historic verification.
Never put real keys into repository, build artifacts or logs.

## Acceptance and deployment risks

Unit tests cover required startup secrets, operator field validation and
rotation. Real PostgreSQL and SQL Server tests verify chain creation,
concurrent monotonic writes, key-rotation verification and detects a
modified row or independent-head mismatch.

The framework does not decide which business actions are recorded; the
caller must supply trusted authenticated actor and authorized tenant.
The narrow event fields intentionally exclude PII-rich free text.
Real customer environments still need immutable external head storage,
role-based append-only DB grants, audit alerting, retention, data subject
handling and operational disaster drills.

Rollback must retain audit evidence for the legal period. Dropping this
technical migration deletes evidence; require explicit DBA/legal approval,
not an emergency shortcut.

## Streaming full-chain and signed-prefix verification (incremental hardening)

Both `VerifyAsync` and `VerifyAnchoredPrefixAsync` now enumerate the
ordered, per-tenant EF Core query asynchronously instead of materializing
all records using `ToListAsync`. The prefix verifier retains its
repeatable-read snapshot; the HMAC format, sequence checks, independent
checkpoint signature and fail-closed mismatch results are unchanged.
Verification still processes **every** relevant row and needs an index
and bounded operational scheduling for long histories; streaming is not a
Production performance/SLA or immutable-retention guarantee.

The synthetic PostgreSQL/SQL Server regression verifies a multi-row
chain, independently signed prefix followed by legitimate appends,
negative mid-chain modification, and the unaffected neighbor tenant.
The test deletes only its own random-tenant audit rows. Exact-HEAD CI
must execute it successfully before claiming PASS. Real off-DB
checkpoint custody, key rotation, DB append-only grants and legally
approved retention remain Security/Operations/business-owner gates.

# ADR-007 — Permission-gated Wolverine dead-letter operator

**Status:** Opt-in single-tenant technical reference, not Production DLQ authorization.

Wolverine durable local queues already persist rejected envelopes into
the database-backed dead-letter store. The Platform introduces
\`AbaDeadLetterAdministration\`, a **service-only** inspection and
exact-envelope replay API; no HTTP administrative endpoint, default
operator account, or automatic replay is registered.

The operator is disabled unless **all** are configured: a relational
database, durable messaging, tamper-evident audit ledger and explicit
\`Platform:Messaging:DeadLetterOperatorEnabled=true\`. Multi-tenant
operation is rejected until an Identity owner implements tenant-aware
message-store permissions and tests them.

Only **exactly one authenticated claims identity** with both the subject and
operation permission **and exactly one `sub` claim** is allowed. Anonymous claims cannot augment an otherwise
authenticated principal, and multiple authenticated identities fail closed
rather than ambiguously authorizing a replay. The upstream verified issuer
must normalize principal identity; this does **not** itself issue IdP roles or
approve Production operators.

Only an authenticated principal with an exact
\`aba_permission=deadletters:inspect\` or
\`aba_permission=deadletters:replay\` claim, opaque valid \`sub\`, a
change-ticket ID and an explicit envelope UUID can use the service.
Inspection returns only ID, message type, exception type and replay
state, without message body or exception text. When the envelope is not
already replayable, it also returns a cryptographically random, one-time
replay token. The technical store keeps only its SHA-256 hash, bound to that
envelope, the observed message/exception type pair, operator subject and
change ticket. The token expires after five minutes. A new inspection
replaces any earlier unused token for that envelope. Treat the token as a
transient capability: never log it or paste it into the change ticket.
`AbaDeadLetterSummary.ToString()` redacts the token for ordinary formatting,
but callers must not add it to structured logs, telemetry, exception context
or other diagnostic output.

Replay checks the envelope still has the operator-reviewed
message/exception type and is not already replayable, stores a signed
HMAC request record **before** calling Wolverine's
\`IDeadLetters.ReplayAsync\` for that exact envelope ID, and audits
the outcome. This merely marks a message for later inbox recovery by
Wolverine's durability agent. Processing might fail again, and the
business consumer remains responsible for idempotency and compensation.
There is no bulk replay, automatic discard or permission escalation.

Replay requires that one-time inspection token and atomically consumes the
matching unexpired receipt before touching Wolverine. Missing, expired,
reused, wrong-operator or wrong-ticket tokens are rejected. Concurrent uses
of one receipt can queue at most one replay request. If a replay operation
fails and Wolverine confirms the envelope stayed non-replayable, the receipt
is released for a fresh inspection; an uncertain outcome stays consumed
for a five-minute safety window after consumption. After that window, a new
inspection may replace it only after successfully re-reading a
non-replayable envelope; if Wolverine state cannot be read, no new token is
issued. These technical controls do not implement a production issuer,
tenant-aware message-store policy or business compensation.

The synthetic internal \`AbaPoisonProbe\` lets PostgreSQL/SQL Server
tests cause a deterministic error, inspect the quarantine entry, verify
denied anonymous/mismatched requests, verify receipt binding, expiry and
concurrent single use, repair a dependency and explicitly replay. Test-only
claims are not proof of a real IdP operator role:
Production requires signed permission issuer, human two-person change
approval, rate controls, tenant isolation, retention and monitoring.

## Additional subject-claim ambiguity denial (Queue B run 6)

Even a single authenticated claims identity can contain two `sub` claims
(with different or repeated values). Dead-letter inspection and replay now
reject that ambiguity **before reading the message store or appending audit**;
selecting the first claim could misattribute the operator who requested a
signed replay. The existing signed single-subject happy path is unchanged.
The disposable PostgreSQL and SQL Server poison-replay integration test covers
conflicting and repeated subject claims, denies both inspect and replay,
asserts the dead-letter envelope stays non-replayable and verifies the audit
ledger record count is unchanged. These tests require the exact new PR-head
CI before being called executed. No real operator IdP issuance, tenant-store
permissions, bulk replay or Production approval is implied.

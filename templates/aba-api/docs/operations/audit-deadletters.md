# HMAC audit and dead-letter replay — technical operator runbook

**Do not run against Production or customer databases without an approved
Security/Operations change request.** These opt-in services have no HTTP
management endpoint and are disabled in a clean generated API.

**Verified technical scope:** [source GitHub Actions run 43320400760](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43320400760)
proved on both SQL engines that poisoned technical Wolverine messages
are quarantined, inspected with permission checks, then replayed
successfully only after a synthetic dependency repair. HMAC audit-chain
integrity was independently verified. This does not approve Production
operator identities or tenant-specific message stores.

## Signing configuration

A relational provider must be enabled and its technical migrations
approved/applied. Inject externally managed secrets through the
configuration provider:

- \`Platform:Audit:Enabled=true\`
- \`Platform:Audit:PrimaryKeyId=key-v1\`
- \`Platform:Audit:Keys:key-v1\` — base64 encoding of at least 32 bytes
  of cryptographically random key material from a managed secret store

Never commit a production HMAC key to appsettings, a shell script,
an environment sample, CI logs or a Docker layer. Store old key IDs
and material for the approved historical retention window after rotation.

An authorized business handler can call
\`AbaAuditLedger.AppendAsync(new AbaAuditWrite(tenant, actor,
action, outcome, correlation))\`. Only opaque IDs and allowed/denied/failed
outcomes are accepted; do not pass raw request payloads or email addresses.
Regularly save the \`VerifyAsync\` head digest in independent immutable
storage. Otherwise a truncated tail cannot be detected.

## Operational poison-message procedure

Enable durable PostgreSQL/SQL Server Wolverine messaging, the audit
ledger, and \`Platform:Messaging:DeadLetterOperatorEnabled=true\`.
Multi-tenant mode is rejected. Have a trusted Identity issuer create
an authenticated operator with explicit permission claims
\`deadletters:inspect\` and separately \`deadletters:replay\`,
under claim name \`aba_permission\`. The framework never grants them.

1. Investigate and fix the original failure before considering replay.
2. Use \`InspectAsync(envelopeId, principal, changeTicket)\` to inspect
   **metadata only**, and record the type/exception in the change ticket.
3. Check the owning business context's idempotency and external
   side-effect/compensation policy; obtain documented operator approval.
4. Execute \`RequestReplayAsync(envelopeId, reviewedMessageType,
   reviewedExceptionType, principal, changeTicket)\` for one exact ID.
   A signed request audit entry is committed *before* replay marking.
5. Observe the Wolverine durability agent, subsequent handler result,
   and independently verified business state. A replay request is not
   proof of successful execution; do not retry indefinitely.
6. Verify the HMAC chain against an off-DB head checkpoint and keep
   incident/approval records according to policy.

No arbitrary payload exposure, native-broker DLQ management, bulk replay,
default authorization, public HTTP surface or exactly-once external
effect guarantee is included.

Architecture decisions: ADR-006 and ADR-007 under \`docs/decisions/\`.

## Independently signed audit prefix checkpoints

The database ledger's current digest is only an UNSIGNED
candidate. Obtain one with CheckpointCandidateAsync after
its chain check. A separate offline custodian, NOT the API's
HMAC key ring, uses AbaAuditCheckpointCodec.Sign with an
independent 32-byte-or-larger key and an immutable external
archive to sign tenant, sequence and head digest. Keep the
offline signing key outside appsettings and running API
configuration. Persist the signed checkpoint plus its
key ID/version in a separately access-controlled archive.

VerifyAnchoredPrefixAsync(checkpoint, offlineKey) can then
detect a tampered or truncated historical prefix even after
later valid records arrive. Without a real external checkpoint
archive and separately protected signing key, this is only
a technical primitive. Do not claim production immutability.

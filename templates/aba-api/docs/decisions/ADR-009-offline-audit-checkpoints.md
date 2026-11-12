# ADR-009: Separately signed historical audit checkpoints

Status: Technical checkpoint primitive; immutable external custody not provisioned
Owners: Platform implements the codec / verifier. Security and Operations
own the *offline* signing identity, rotation, audit export and custody.
Date: 2026-11-13

## Why

A per-tenant HMAC hash chain already detects middle-record tampering,
but deleting a valid suffix changes no remaining record's link. Passing
the DB's current head digest back to the same DB is not independent
tamper evidence. A separate signing authority must attest an immutable
historical prefix outside the application and its database.

## Decision

AbaAuditLedger.CheckpointCandidateAsync(tenant) first verifies the
entire HMAC chain and returns only the tenant, positive sequence and
current head digest. The returned candidate is UNTRUSTED / unsigned.

A separate offline signer invokes
AbaAuditCheckpointCodec.Sign(candidate, keyId, signingKey, issuedUtc).
The key is 32-128 bytes, distinct from all ledger HMAC keys, and
must NOT be registered in the ordinary API configuration. The V1
signature canonically covers purpose, tenant, sequence, lowercase
SHA-256 digest, UTC issue ticks and offline signer key ID, with HMAC-SHA256.
Verify uses constant-time comparison of raw digests and refuses a
malformed version, key ID, timestamp, sequence or signature.

The operator stores the signed checkpoint and signing-key rotation
metadata in an *independently permissioned immutable archive*.
This repository deliberately does not choose a cloud vendor, commit
a signing key or claim that an offsite/WORM sink has been deployed.

AbaAuditLedger.VerifyAnchoredPrefixAsync(checkpoint, offlineKey)
validates the offline signature first, then reads the entire tenant
chain inside one repeatable-read SQL snapshot. It verifies each HMAC
link and exactly matches the independently signed digest at the
historical sequence. Later legitimate appends remain valid, while
removing or rewriting an anchored record fails. Deleting only events
AFTER the most recent signed checkpoint cannot be detected by that
checkpoint; frequent independent anchors are an operational requirement.

## Tests and remaining risks

Unit tests mutate tenant, sequence, signature, digest, key ID and
timestamp, reject short signing keys, and exercise signing key
rotation. Actual PostgreSQL and SQL Server integration tests prove
that two new events do not invalidate an existing signed prefix,
while deletion of a signed prefix or a wrong independent key fails.

An attacker holding BOTH the ledger HMAC keys and checkpoint signer
key can forge a consistent history. Managed HSM/KMS, two-person
approval, immutable signed exports, monitoring, key revocation,
retention and legal deletion processes remain Product/Security gates.

No customer business model, endpoint or automatic publication is added.

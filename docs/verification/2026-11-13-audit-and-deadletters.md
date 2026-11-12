# HMAC audit and Wolverine dead-letter recovery — reproducible CI evidence

**Date:** 2026-11-13 (Europe/Istanbul).
**Verified commit:** `976c21325b85190ef29c8d239b05cc50f39557be`.
**GitHub Actions:** https://github.com/smael-aftersearch/ABAFramework/actions/runs/43320400760

## Executed gates

All **eight** CI jobs passed. The generic API xUnit runner passed
**96/96** tests; the real provider test suite passed **24/24** tests;
the isolated native recovery/safety category passed **7/7** tests.
The other jobs passed actual Keycloak/JWKS authorization, runtime Docker,
NuGet direct/transitive vulnerability+SPDX inventories, Trivy fixable-CRITICAL
image scanning, and signed/verified SLSA NuGet template provenance with
an intentionally corrupted package rejected.

The live SQL suite specifically verified each provider's:

- **HMAC ledger:** valid contiguous per-tenant signatures, older-key
  verification after rotation, invalidated chain after mutation, refusal
  without historic key material, independent expected-head mismatch
  and five simultaneous writes retaining unique monotonic sequences.
- **Wolverine poison quarantine:** a technical probe fails while a
  disposable dependency receipt is missing, is stored in the relational
  dead-letter queue and cannot be replayed by an anonymous principal
  or with an unreviewed message type.
- **Audited exact-id replay:** after the synthetic dependency is
  repaired, an authorized principal requests replay by its envelope
  ID, the expected handler eventually commits a technical receipt,
  and the replay/inspection audit records verify cryptographically.

Negative tests use disposable synthetic data; no customer tenant,
real external payment, published integration event, or Production
queue was accessed.

## ABA ownership and gaps

Technical integrity and operational mechanics live in Platform.
Owning business contexts remain responsible for event/action policy,
actor authority, tenant membership, legal retention, consumer
idempotency, replay compensations and versioned business contracts.
Security/Operations must approve HMAC key distribution/rotation,
independent immutable digest anchors, operator identity and permissions,
database grants, incident ticket governance, quotas and monitoring.

The ledger is **tamper-evident**, not tamper-proof. Without a trusted
off-database head digest, deleting a final suffix is not provable.
The operator API is not an HTTP endpoint; multi-tenant message-store
authorization is refused. Wolverine replay marking is asynchronous
and is not an exactly-once external side-effect guarantee.

This is a technical verification baseline; a generated product must
run its own tests and production approvals. Rollback of the ledger
migration could destroy audit evidence and requires retention approval.
See template ADR-006, ADR-007 and the audit/dead-letter runbook.

# ADR-003 — Verify EF state and Wolverine outgoing event as one transaction

Status: Accepted | Date: 2026-11-12

The first Wolverine integration test checked that one durable message reached a technical handler. That was insufficient to verify the **outbox guarantee** that application data and outgoing messages commit together.

Add `AbaTransactionalOutboxProbe` and handler strictly inside the technical Platform layer. It writes a technical EF idempotency receipt and publishes a technical probe event through Wolverine's auto-applied EF transactional middleware. Inject a failure *after* publication but before commit to test that no EF row and no message side effect survive rollback, on both PostgreSQL and SQL Server.

The probe contains **no business model**. Real business contexts use their own DbContexts and integration contracts; do not reuse technical receipts as business tables. Still required for release: crash at every commit/publish stage, restart recovery, poison/dead-letter replay, duplicate delivery with real consumers, and broker transport when needed.

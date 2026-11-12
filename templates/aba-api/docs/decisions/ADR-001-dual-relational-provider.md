# ADR-001 — Dual relational providers without domain coupling

Date: 2026-11-12 | Status: Accepted

## Context

The framework consumer requires both PostgreSQL and Microsoft SQL Server. Business contexts must remain oblivious to EF Core and any relational provider, and a new empty API must still run without services.

## Decision

Keep EF Core provider selection and migrations in the technical Platform project; use `Platform:Database:Provider` plus `ConnectionStrings:AbaDatabase` to choose exactly one provider at deployment. Retain a provider-neutral technical idempotency receipt model and a checked-in initial migration. Real contexts introduce their own DbContext, ownership and migrations rather than writing into the platform model. Disable persistence and messaging by default for a fresh API.

Wolverine uses the **selected same relational engine** for durable inbox/outbox; Redis uses the independent `AbaRedis` connection string. Do not automatically migrate in production API startup.

## Consequences

The framework supports two vendors without two business applications. Each provider requires the **same behavioral tests**, plus its own actual database integration and migration testing. Vendor-specific types or SQL require separate explicitly tested migrations. Multi-provider support is not proof of identical performance, SQL translation, encryption or recovery properties.

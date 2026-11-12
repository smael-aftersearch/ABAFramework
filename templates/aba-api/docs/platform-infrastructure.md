# ABA platform integrations — .NET 10

These are **real opt-in adapters** for a domain-neutral API; no Academy or sample business contexts are generated.

## Provider selector: PostgreSQL **and** SQL Server

EF Core 10 is wired in `src/Aba.Starter.Platform`. Choose **one provider for a deployment** through configuration (not hardcoded product code):

| Configuration key | PostgreSQL | SQL Server / Azure SQL |
| --- | --- | --- |
| `Platform:Database:Provider` | `PostgreSql` | `SqlServer` |
| `ConnectionStrings:AbaDatabase` | `Host=...;Database=...;Username=...;Password=...` | `Server=...;Database=...;User Id=...;Password=...;Encrypt=True` |

The provider name defaults to `None`, and **no connection string is shipped**. Database readiness is checked at `/health/ready` when enabled. EF Core mappings and an initial provider-neutral migration for `aba_idempotency_receipts` are included. That table is technical infrastructure; real bounded contexts define their own owned tables and migrations, never shared domain tables.

Run migrations **as a deployment step**; never grant automatic DDL or broad database privileges to an ordinary production web process. Example after installing `dotnet-ef` (matching the EF Core 10 version used):

```bash
ABA_DB_PROVIDER=PostgreSql ABA_DB_CONNECTION='Host=localhost;Database=aba;Username=...;Password=...' \
dotnet ef database update --project src/Your.Project.Platform --context AbaPlatformDbContext

ABA_DB_PROVIDER=SqlServer ABA_DB_CONNECTION='Server=localhost;Database=aba;User Id=...;Password=...;Encrypt=True' \
dotnet ef database update --project src/Your.Project.Platform --context AbaPlatformDbContext
```

A single platform migration path deliberately uses *provider-neutral CLR types*; both provider SQL generators are exercised by database integration tests. Independently version/migrate every business context's storage.

The included `EfIdempotencyStore` uses a unique compound primary key `(Scope, Key)`. The business owner is responsible for choosing the correct request scope and response semantics; this low-level store does **not** enforce authorization or tenant ownership.

## Redis caching

Default: `Platform:Cache:Provider=Memory`; no Redis server is necessary for local quick start.

To enable real Redis, set `Platform__Cache__Provider=Redis` and `ConnectionStrings__AbaRedis=localhost:6379`, using environment variables, workload identity, or a secret store. The `ScopedJsonCache` wrapper supports JSON values, TTLs, removal/invalidation and explicit scope hashing. The Redis provider has a real read/write/remove health check at `/health/ready`.

**Tenant boundary:** a cache scope is only a namespace. You must authenticate/authorize tenant ownership **before** constructing and using that scope. The starter does not yet implement multitenant authorization, cache stampede protection or distributed invalidation events.

## Wolverine durable messaging / Outbox–Inbox

Enable with `Platform__Messaging__Enabled=true` **only when a PostgreSQL or SQL Server database is configured**. The platform uses WolverineFx 6 and selects `PersistMessagesWithPostgresql` or `PersistMessagesWithSqlServer`, EF Core transactional middleware and durable local queues. These are genuine relational message persistence integrations, not the old Academy file outbox.

Wolverine envelope tables are separately managed from EF Core domain migrations. Provision Wolverine's resources according to the version-matched Wolverine CLI in deployment, with the same database provider. Use `resources setup`/resource migrations in a controlled deployment step; do not enable destructive auto-purge or auto-create in production. Each handler that writes domain state and publishes messages must enroll its own owning `DbContext` in Wolverine's transaction; a registered store alone is **not proof of a transactional domain workflow**.

Wolverine 6 requires the explicit `WolverineFx.RuntimeCompilation` package for dynamic handler code generation, and the template now includes it. For **isolated Development integration tests only**, set `Platform__Messaging__AutoProvisionInDevelopment=true` to allow Wolverine's native `AutoBuildMessageStorageOnStartup` policy to create required envelope tables. Production startup explicitly keeps `AutoCreate.None` and requires reviewed provisioning before deployment. A technical probe handler is included to verify durable local message processing against each real SQL engine.

No RabbitMQ/Kafka or business handler is silently installed. CI exercises a real durability probe handler against both supported SQL engines, but **crash/restart, outbox atomicity under injected failures, replay/dead-letter and business consumer coverage are still incomplete**. Do not equate the probe with complete production message reliability.

## Local development integration services

From the repository root with Docker Compose:

```bash
docker compose -f infra/compose.platform.yaml up -d
```

PostgreSQL: `127.0.0.1:54329`; SQL Server: `127.0.0.1:14339`; Redis: `127.0.0.1:6389`. Credentials in Compose are **throwaway development-only fixtures**, not production secrets.

Set `ABA_TEST_POSTGRESQL`, `ABA_TEST_SQLSERVER` and `ABA_TEST_REDIS` and run `bash scripts/verify-platform-services.sh`. GitHub Actions runs an equivalent integration job against actual service containers. Without those env vars, the xUnit external-service cases are opt-in and do not attempt remote connections; check CI artifacts before claiming both live database engines were tested.

## Failure and rollback policy

- A configured provider with a missing connection string **fails fast**.
- Redis is optional: explicitly choosing Redis without a connection **fails fast**, rather than silently switching to local memory.
- Wolverine durability is opt-in: enabling it without a relational store **fails fast**, rather than switching to non-durable messaging.
- Per-context rollback uses reviewed migrations, not `EnsureCreated` or destructive reinitialization.
- Do not commit real connection strings. Rotate any credentials that accidentally enter repository history.

## Observability: OpenTelemetry

Set `Platform__Observability__Enabled=true` to enable ASP.NET Core and outgoing HTTP traces/metrics. Set `Platform__Observability__ServiceName=MyApi` and optionally `Platform__Observability__OtlpEndpoint=https://collector.example:4317` to export via OTLP. An unsecured HTTP collector is accepted only in local Development. No external collector, credentials, public metrics endpoint or telemetry backend is bundled. Review tracing sampling, PII filters, service identity and collector trust in production.


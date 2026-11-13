# ADR-005 — Provider-enforced tenant Row-Level Security

**Status:** Technical implementation; real product rollout is not authorized.
**Technical owner:** Platform; **business owners/DBA:** each context's tables,
memberships, policies, migration review and runtime DB role permissions.

## Why this decision

An EF query filter and SaveChanges guard do not protect raw SQL, EF
`ExecuteUpdate`/`ExecuteDelete` or unrelated code holding a connection.
The generic framework has no business membership model and must not invent one.

## Decision

- PostgreSQL: opt-in migration statements `ENABLE ROW LEVEL SECURITY`,
  `FORCE ROW LEVEL SECURITY`, and a policy with both `USING` and
  `WITH CHECK`. The predicate compares the row's owner to the
  transaction-local `current_setting('aba.tenant_id', true)`.
  CI uses a dedicated non-superuser role; the superuser bypasses RLS.
- SQL Server: schema-bound predicate over
  `SESSION_CONTEXT(N'aba_tenant_id')`, FILTER on read/target rows,
  and BLOCK for INSERT/UPDATE. A disposable administrative CI login is
  used for tests; production must use a separate least-privileged login.
- `AbaTenantRlsPolicies.Install` produces reviewable provider SQL with
  strict identifier validation; the owning context must explicitly
  apply those statements in an approved migration.
- `AbaTenantSqlScope.RunAsync` binds a validated `IRequestTenant`
  with a parameter, in a fresh connection and transaction, and executes
  a callback against that same transaction. PostgreSQL uses SET LOCAL.
  Connection closure also covers a provider `OpenAsync` failure after the
  connection has partially opened; the callback must never run in that case.
  A pre-cancelled request is rejected before a provider OpenAsync call even if
  that provider would ignore the cancellation token; no transaction or tenant
  callback may run. This is a technical resource/isolation invariant, not
  membership authorization.
  SQL Server resets SESSION_CONTEXT before pooling; if it cannot reset,
  the connection pool is invalidated.
- The technical request-tenant resolver requires exactly one authenticated identity
  and exactly one valid claim of the configured tenant type. Conflicting or repeated
  same-value claims are ambiguous and rejected before the tenant can reach a SQL
  scope. An untrusted header or secondary identity cannot supply a fallback.
- No customer's tables, membership records, global roles or deployment
  configuration are silently created or modified at runtime.

## Acceptance and rollback

Default xUnit tests check DDL, injection-resistant identifiers, and missing
tenant refusal. Actual PostgreSQL and SQL Server integration tests use a
disposable technical table and prove unrelated tenants cannot read, update,
delete or forge inserts through raw SQL and EF bulk mutations, even without
an EF query filter. They also require no residual tenant context.
Migrations must remove only the relevant table's policy during approved
rollback; do not drop the shared SQL Server predicate function while other
contexts still use it.

## Threats that remain

These predicates do **not** establish identity membership. Caller-controlled
raw SQL that changes PostgreSQL `set_config` or SQL Server
`sp_set_session_context` may spoof tenants; SQL injection prevention,
parameterized commands, trusted tenant resolution, tight DB role grants and
restricted application SQL privileges remain mandatory. Superusers,
policy-altering principals, DB owners and privileged migrations can bypass
or disable controls. EF filters remain defense-in-depth, not authorization.
Production migration/change-control, provider role testing, cross-context
handlers, connection-pool stress and tenant membership verification are open.

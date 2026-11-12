# ABA generic API — feature status and Production database transport validation

Date: 2026-11-13
Verified source: https://github.com/smael-aftersearch/ABAFramework/actions/runs/43305238518
Commit: `31362d6d8595f7e671c3bf753f395b2bd691d3bb`

## Executed verification

- All five GitHub Actions jobs passed: generated .NET 10 API, real PostgreSQL/SQL Server/Redis/Wolverine integration, Keycloak OIDC/JWKS, Docker and versioned template package.
- **70 passing default API xUnit tests**, including PostgreSQL/SQL Server secure/insecure TLS configuration and environment preflight; **16 passing real-service integration tests**, including concurrent idempotency, tenant EF guards and Wolverine technical Outbox/restart checks.
- Non-root Production Docker image with a valid HTTPS OIDC authority/audience fails before serving HTTP when the PostgreSQL connection uses `SSL Mode=Require` instead of `VerifyFull`, or when SQL Server uses `TrustServerCertificate=True`. This is a real process startup test, not a simulation through a development host.
- **54 tracked capabilities** in `docs/implementation-status.json` with executable source/test path checks. Six Python positive/negative validator cases passed, and both published status tables matched.

## Follow-on changes

The next NuGet template artifact version is `0.3.0-preview.2`, which includes these refinements and must be verified by the **subsequent** pipeline. The reader should not infer that the preceding green run builds this newer package.

## Boundaries still open

No live Production database CA rotation/managed secret configuration, tenant SQL RLS, organization membership authorization, domain-owned business migrations, abrupt crash recovery/replay, backup/restore, external OTLP collector or public Production hosting has been delivered. Read `docs/IMPLEMENTATION-STATUS.md` for exact owners, tasks and acceptance gates.

# MASTER AGENT PROMPT: ABA-Compliant .NET 10 Enterprise Framework and Working Reference Application

## 0. Role, objective, and final deliverable

Act as a principal .NET architect, hands-on lead engineer, platform engineer, security engineer, QA engineer, and technical writer. Your assignment is to DESIGN, IMPLEMENT, TEST, DOCUMENT, and PACKAGE a reusable C#/.NET 10 enterprise application framework, plus a real working reference application built on it. Do not respond with a high-level proposal or snippets instead of a repository. Generate the actual source files, project files, tests, migrations, scripts, documentation, Docker configurations, CI workflows, and a downloadable ZIP archive.

The final deliverable MUST be a real archive named `ABA.Net10.Framework-and-Sample.zip` containing a complete, reproducible source repository. Attach or link the archive as a downloadable artifact. The application must be buildable and runnable using documented commands. Include a precise execution report describing commands executed, their results, any blockers, and any unimplemented items. Never assert that a test or build passed unless you executed it and saw the result. Do not provide a mock download link.

## 1. Authoritative source and governing architectural philosophy

Read the entire attached PDF `ABA_Adaptive_Business_Architecture_English_Edition.pdf` BEFORE designing the solution, including the appendices and later deep-dive chapters. Treat its ABA (Adaptive Business Architecture) principles as the PRIMARY architectural requirements, especially:

- Optimize for reducing future cost of change; every abstraction must justify the complexity it adds.
- One clear owner per significant data set, business rule, decision, and contract.
- Business/bounded contexts are AUTHORITY boundaries, not merely directories or deployable services.
- Contracts are the only public cross-context dependency surface. Never share entities, aggregates, DbContexts, repositories, or domain services across contexts.
- Platform provides technical mechanisms; business contexts own business decisions.
- Start as a strongly governed modular monolith with a documented, feasible extraction path to microservices. Do NOT create gratuitous microservices.
- Prefer simple explicit domain models. Apply CQRS, Event Sourcing, sagas, generic repositories, and distributed architecture only where specific value is demonstrated. Event Sourcing is NOT a blanket requirement.
- Turn architecture into executable governance: dependency tests, metadata validation, contract compatibility tests, CI checks, and actionable violations.
- Preserve architectural intent via ADRs, documentation, context maps, and AI-readable metadata. Track architectural health with real metrics, not invented scores.
- Use the PDF's sample domains as teaching/reference choices, not as universal modules the framework always forces upon consumers.

Create `docs/ABA-COMPLIANCE-MATRIX.md`: map each relevant ABA rule to a file, test, validation command, or documented architectural decision. Note exact PDF chapters and/or page numbers wherever possible. Record any deliberate interpretation in an ADR. If another request conflicts with ABA, favor ABA or explicitly explain a documented exception and its tradeoff; never silently violate it.

The term 'API' is ALSO important: provide professional HTTP APIs governed by OpenAPI documentation, versioning, authentication, authorization, and backward-compatibility checks. ABA refers to architecture; API refers to interfaces. Handle both distinctly.

## 2. Stack and compatibility requirements

Target `.NET 10` (`net10.0`) and C# 14, using ASP.NET Core 10, EF Core 10, dependency injection, and current compatible stable NuGet versions. Use up-to-date official documentation for SDK/package compatibility before selecting versions. Keep the target fixed to .NET 10 even if newer SDKs exist. Use `global.json`, Central Package Management (`Directory.Packages.props`), `.editorconfig`, `Directory.Build.props`, nullable reference types, analyzers, and reproducible package restores/locks where appropriate. Avoid obsolete, preview-only, vulnerable, or license-incompatible dependencies.

Interpret the requested 'Wallering' as `Wolverine / WolverineFx`, a .NET messaging and handler framework, unless the attached source or clear evidence suggests otherwise. Implement a working Wolverine integration if currently compatible with .NET 10 and the selected persistence stack. Validate the exact APIs against its official docs; do NOT invent method names. If incompatible, use a proven durable messaging alternative, document the evidence and ADR, and retain the same framework-facing abstractions. Avoid needless duplication of Wolverine and a second mediator library.

Interpret 'OAuth Box' as likely referring to the **Outbox pattern**; implement transactional Outbox AND Inbox. ALSO implement OAuth 2.0 / OpenID Connect properly, since that may have been intended. These are independent requirements. For local development, use a real standards-based identity provider (e.g., Keycloak) via Docker Compose and JWT bearer validation. Do not invent a custom OAuth server or insecure homemade token issuer.

Recommended primary persistence: PostgreSQL + EF Core 10; Redis for distributed cache; Wolverine durable messaging with a compatible PostgreSQL inbox/outbox store; optional RabbitMQ transport if justified. Keep the messaging and cache infrastructure behind narrow interfaces where it demonstrably reduces future change cost. Pin Docker image tags to suitable stable versions. Provide local Docker Compose configuration, health checks, and deterministic setup instructions. Do not make cloud credentials mandatory.

## 3. TWO products inside ONE solution

A. `Framework/`: a reusable, business-agnostic set of building blocks and platform adapters, organized so future applications can consume it without depending on the sample business domains. Provide a clear public API and modular registration extensions. Build it in library projects appropriate for later NuGet packaging. Keep dependencies minimal and avoid framework lock-in.

B. `samples/Academy/`: a complete, nontrivial, runnable reference application that USES the framework rather than bypassing it. Model the PDF's paid course enrollment flow as a vertical slice across bounded contexts. It must demonstrate real EF Core migrations, PostgreSQL persistence, authenticated endpoints, Redis caching, durable events, reporting projections, failure recovery, traceability, architecture governance, and automated tests. A minimal functional user-facing dashboard is desirable if achievable without sacrificing backend completeness; documented API and executable sample are mandatory.

Also provide at least one working `dotnet new` project template for starting a NEW ABA application from the framework, plus a minimal modular-context scaffolding option (`dotnet new` template or a small documented CLI/script). The generated solution must compile; include a test that actually instantiates a template and builds its output.

## 4. Required internal architecture

Follow DDD and Clean Architecture WITHIN each business context. Separate `Contracts`, `Domain`, `Application`, `Infrastructure`, and `_Docs` as appropriate. `Domain` must not reference EF Core, ASP.NET Core, Wolverine, Redis, or infrastructure. `Application` coordinates use cases and owns interfaces/ports; `Infrastructure` implements adapters; HTTP endpoints are delivery concerns. Only other contexts' versioned `Contracts` assemblies may be referenced across business boundaries. Use explicit context registration and a composition root. No 'CommonService', shared domain entity package, general business-logic `Shared` layer, or global cross-context DbContext.

Each context must have its OWN EF Core DbContext, schema/migrations, transactional boundaries, and authorized data access. A shared physical PostgreSQL instance is acceptable for the modular monolith, but no cross-context table writes/joins as business integration. Use contracts, read models, or owned APIs/events. Demonstrate optimistic concurrency, safe migrations, time abstractions, cancellation tokens, and strongly typed domain IDs/value objects where justified.

Implement domain primitives where useful: aggregate root, entity, value object, domain event, invariant checking, domain error, auditing fields, optimistic version, and domain-event dispatch. Avoid excessive inheritance and a universal repository abstraction over EF Core if it merely hides EF Core without value. Explicitly distinguish domain events (internal) from published integration events (versioned public contracts).

## 5. Platform capabilities: real, integrated, tested

Provide the following reusable technical modules with production-minded implementations and at least one real usage from the reference app:

1. Persistence: PostgreSQL/EF Core 10 adapters, DbContext registration, migrations, per-context transactions, retries where safe, concurrency control, and safe startup/migration policy.
2. Messaging: Wolverine handlers, transactional Outbox (business change and event stored atomically), transactional Inbox/deduplication for at-least-once delivery, durable publication, configurable retries/backoff, poison-message/dead-letter handling, replay/recovery procedure, correlation/causation IDs, versioned event serialization, schema evolution, and observability. Never promise exactly-once network delivery; ensure effectively-once business processing using idempotency and atomic deduplication.
3. Caching: Redis-backed distributed caching and optional local+distributed hybrid cache using supported .NET APIs, typed keys, TTL policies, cache invalidation, stampede protection when supported, tenant-safe key scoping, and graceful fallback behavior. Cache must never become a source of business truth. Demonstrate a real cache hit/miss/invalidation test.
4. Security: OAuth 2.0/OIDC standards-based JWT bearer authentication, authorization policies, permission checks, current-user/current-tenant technical abstractions, safe secret/configuration practices, auditing, and safe developer login instructions. Identity business entities/roles/memberships belong to the `Identity` context, NOT Platform.Security. Include negative/tenant-isolation tests.
5. HTTP/API: REST endpoints, OpenAPI 3.1 for .NET 10, an interactive API viewer for development where supported, route/API versioning, stable request/response contracts, consistent RFC 7807 Problem Details, centralized exception handling, validation, pagination/filtering, cancellation, rate limiting, CORS, request correlation IDs, auth requirements, and idempotency keys for sensitive commands. No leaking domain entities directly as public DTOs.
6. Reliability: IHttpClientFactory with timeouts and retry/circuit-breaker policies for outbound integrations (only retry operations that are safe or idempotent), proper cancellation, background processing, and graceful shutdown.
7. Observability: OpenTelemetry traces, metrics, structured logs with trace/correlation context, PII/secrets redaction, health/readiness checks, outbox/inbox/dead-letter metrics, cache metrics, request metrics, and basic dashboards or exported telemetry configuration. Include a documented local OTLP collector setup if feasible.
8. Operational support: configuration validation on startup, feature flags only if justified, safe audit trail, developer health checks, database seed/migration scripts, error troubleshooting, and backup/recovery guidance.

Use extension methods such as `AddAbaPersistence`, `AddAbaMessaging`, `AddAbaCaching`, and `AddAbaObservability` only where their contracts are real and they remain simple. Ensure the sample app actually calls these methods and proves the wiring works.

## 6. Required reference app: paid course enrollment

Implement the following business contexts and preserve each one's ownership:

- `Identity`: users, organizations, memberships, roles, and permissions; publishes versioned identity events; business authorization concepts, but not token-validation plumbing.
- `Enrollment`: offerings, capacity, application/approval status, waitlist, enrollment rules, activation/cancellation; independently decides when a seat is confirmed.
- `Payments`: payment authorization/capture/refund state, provider adapters, duplicate webhook handling, unique external transaction IDs, and `PaymentCapturedV1`. Use a deterministic fake payment provider/simulator for local demonstration; no real charges or real gateway secrets.
- `Accounting`: chart of accounts, fiscal period validation, balanced debit/credit journal posting, immutable posted journals, reversal/correction, and `JournalPostedV1`. Payments NEVER writes journals.
- `Reporting`: event-driven projections/read models and dashboard/query endpoints. Reporting NEVER becomes source of truth and can rebuild projections.
- `Platform.Notifications` (or a deliberately small equivalent): technical notification delivery mechanics; context-owned business rules decide when a notification is needed. A local sink can be used instead of real email credentials.

Mandatory end-to-end flow:
1. Create or identify an authorized user/membership.
2. Create an offering with capacity and request an enrollment.
3. Simulate/authorize/capture a payment in Payments.
4. Payments saves the new state and `PaymentCapturedV1` atomically via Outbox.
5. The event reaches Enrollment, which applies its own approval/capacity/payment rules and publishes `EnrollmentActivatedV1` only when appropriate.
6. Accounting independently consumes `PaymentCapturedV1`, checks financial rules, posts a balanced immutable journal, and publishes `JournalPostedV1`.
7. Reporting consumes versioned events into its own denormalized projection, exposed by a read endpoint.
8. Notifications, if triggered, are delivered by a technical adapter without shifting business decisions to Platform.

Expose useful secured API endpoints to exercise the whole flow. Provide seed data and a `scripts/demo` or equivalent deterministic smoke test. Include negative scenarios: insufficient capacity, missing approval, duplicate callbacks/events, invalid payment transition, refund exceeding capture, unbalanced journal, closed fiscal period, unauthorized access, and outbox retry after transport failure. Verify a duplicate `PaymentCapturedV1` cannot double-activate an enrollment or double-post a journal. Demonstrate business reconciliation or a recovery path for failures and eventual consistency.

## 7. Executable ABA governance and metadata

At repository level and for EACH context, create and validate machine-readable architecture metadata using the PDF vocabulary: `ownership.yaml`, `context-map.yaml`, `ai-rules.yaml`, `contract-catalog.yaml`, `decisions.md`, `evolution.md`, plus ADRs. The metadata MUST be consistent with actual code. Make the repository readable by both humans and AI coding agents (`AGENTS.md` and context-specific agent instructions). Include contract owners, consumers, versions, compatibility and deprecation policy.

Create a governance test/analyzer/CLI tool with rule IDs, clear explanations and remediation. At minimum enforce:

- `ABA001`: no cross-context `Domain` or `Infrastructure` references.
- `ABA002`: `Domain` projects are independent of ASP.NET, EF Core, Redis, and messaging frameworks.
- `ABA003`: only versioned `Contracts` are public across contexts; detect unversioned integration contracts.
- `ABA004`: every context has valid and complete ownership/context-map/AI metadata.
- `ABA005`: no shared business entities or shared business domain models.
- `ABA006`: no forbidden dependency according to context maps.
- `ABA007`: no consumer code directly accesses another context's persistence or writes its owned tables.
- `ABA008`: contract changes follow a tested backwards-compatibility/versioning policy.
- `ABA009`: Platform remains business-agnostic; financial/enrollment business policy cannot be placed there.
- `ABA010`: no forbidden commands that mutate immutable posted journals.
- `ABA011`: ownership and contract catalogs agree with declared public contracts.
- `ABA012`: no cyclic context/project dependencies.

Implement actual tests proving that at least one deliberately introduced forbidden dependency and one malformed metadata/contract case make governance FAIL. Demonstrate successful governance on the clean repository. Generate a dependency graph and an architecture health report from actual analyzable evidence (metadata validity, dependencies, violations, contract changes); no made-up metrics. Provide actionable CI failure output.

## 8. Test suite, quality, security, and delivery automation

Include xUnit/NUnit tests as appropriate: unit tests for domain invariants, application tests, architecture tests, contract/schema compatibility tests, integration tests with real PostgreSQL and Redis (Testcontainers where feasible), messaging/outbox/inbox reliability tests, API/security tests, and a full cross-context scenario. Unit tests must not need Docker; integration tests may require Docker and should say so. Separate test categories, document requirements, and avoid false-positive tests that check only that services are registered.

Add GitHub Actions (or an equivalent runnable CI definition) that restores, builds, formats, tests, validates architecture metadata/contracts, checks template instantiation, performs dependency vulnerability checks, and, where practical, runs container-based integration tests. Supply dependency inventory/licenses, sample `.env.example` without real secrets, pinned dependencies, sensible `.gitignore`, and secure-by-default production configuration. Never turn off security or code quality checks merely to make CI pass. Include a security threat model covering token validation, tenant boundaries, malicious callbacks, replay, data leakage, secrets, and unsafe retries.

Write a useful README with exact prerequisites and commands for Linux/macOS/Windows where practical; add `docs/architecture.md`, Mermaid context/dependency diagrams, `docs/decisions/` ADRs, `docs/operations.md`, `docs/security.md`, `docs/testing.md`, `docs/extension-guide.md`, `docs/ABA-COMPLIANCE-MATRIX.md`, and `docs/KNOWN-LIMITATIONS.md`. Include guidance on creating a new context, new event contract/version, new EF migration, adding Redis caching, swapping messaging adapters, testing a boundary, and extracting a context into a service later.

## 9. Suggested repository layout (adjust for good reasons, document differences)

```
ABA.Net10/
  global.json
  Directory.Build.props
  Directory.Packages.props
  ABA.Net10.slnx
  src/
    Framework/
      Aba.Abstractions/
      Aba.Domain/
      Aba.Application/
      Aba.Persistence.EFCore/
      Aba.Messaging.Wolverine/
      Aba.Caching.Redis/
      Aba.Security/
      Aba.Observability/
      Aba.WebApi/
      Aba.Governance/
    Templates/
  samples/
    Academy/
      Host/
      Business/
        Identity/{Contracts,Domain,Application,Infrastructure,_Docs}/
        Enrollment/{Contracts,Domain,Application,Infrastructure,_Docs}/
        Payments/{Contracts,Domain,Application,Infrastructure,_Docs}/
        Accounting/{Contracts,Domain,Application,Infrastructure,_Docs}/
        Reporting/{Contracts,Domain,Application,Infrastructure,_Docs}/
      Platform/
  tests/
    Unit/
    Integration/
    Architecture/
    Contracts/
    Templates/
  infra/
  scripts/
  docs/
  .github/workflows/
  README.md
```

The real projects may use a more practical naming convention. Do not build every possible project if it adds empty indirection; prioritize correct enforceable boundaries and functioning examples. Avoid claiming unfinished modules are complete.

## 10. Required execution order and acceptance gates

Work autonomously through all stages, not merely planning. Do not ask me to select minor libraries or folder names; make grounded decisions and record them. Only request clarification for a blocker that cannot be reasonably resolved using the attached PDF and these requirements.

1. Inspect the PDF, note requirements and ambiguous terms, confirm package/platform compatibility from reliable current sources, and record ADRs.
2. Create a small implementation plan and acceptance checklist, then immediately build the actual repository.
3. Implement reusable framework and one full vertical slice FIRST; add secondary features only after the slice runs. Prefer genuine working core features over a large tree of empty placeholders.
4. Run `dotnet --info`, restore, build, unit tests, governance, integration tests, and Docker-based smoke tests when tools are available. Fix errors and rerun checks; record outputs.
5. Verify the following gates with evidence:
   - All intended projects target `net10.0` and restore/build successfully.
   - The demo boots from a clean clone using the documented Compose/configuration steps.
   - An authenticated API request can complete paid enrollment and produce an enrollment state, a balanced immutable accounting journal, and a queryable reporting projection.
   - Duplicate payment callbacks/events and at-least-once delivery do not create duplicates.
   - Caching behaves correctly, including invalidation and tenant-safe keys.
   - API documentation is available and protected appropriately.
   - Architecture tests reject forbidden dependencies, metadata errors, and incompatible contract changes.
   - A new project can be created and built from the supplied template.
   - No real secrets are committed, and no critical known vulnerability is ignored without a documented risk decision.
   - README commands were actually tried or are expressly marked unverified.
6. Produce a `VERIFICATION-REPORT.md` listing exact commands, pass/fail status, environment constraints, known issues, and the SHA-256 of the final ZIP (where possible).
7. Create `ABA.Net10.Framework-and-Sample.zip` containing the source, docs, tests, templates, migrations, CI, and developer infrastructure, EXCLUDING binaries, caches, `.git`, secrets, node_modules, and unnecessary build outputs. Verify the archive entries and extraction. Expose it as a downloadable artifact.

If runtime/network/container restrictions prevent a required verification, state exactly what was not tested and why. Do not fabricate successful logs, installed packages, or download paths. If scope must be reduced, finish the working framework + paid-enrollment vertical slice + governance + tests + ZIP first, then transparently enumerate postponed enhancements. The final answer should be SHORT: link to the ZIP, quick-start commands, implemented feature summary, test results, known limitations, and where the ABA compliance matrix lives.

## Final directive

You are being evaluated on executable source code, ABA boundary enforcement, reliability of the example, truthful verification, and the quality of the real downloadable ZIP—not the sophistication of a diagram or the length of your response. Implement the repository and deliver the archive now.

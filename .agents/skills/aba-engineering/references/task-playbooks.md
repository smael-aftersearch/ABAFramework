# Task playbooks: what to read, what to change

First read `docs/testing/TEST-STRATEGY.md` for all material code changes and define test acceptance cases. Choose the relevant row; do not load every document for every task. These are workflow guides, not an authorization to edit unrelated business contexts.

| User task | Read before code | Update with implementation | Verify |
| --- | --- | --- | --- |
| Add/change business rule | Owning `_Docs/{ownership,context-map,ai-rules}.yaml`, Domain files, domain tests | Domain tests; `ownership.yaml` if new rule/decision; `decisions.md` if design changes | Domain test, ABA001/002/004 |
| Add API endpoint | Owning context's Contracts, application handlers, Host endpoints, OpenAPI, security docs | API/DTO specs, handlers, API tests, README/OpenAPI | Route + authorization + contract test |
| Create/change integration event | Publisher + consumers' `_Docs`, contract catalog, Contract records | versioned Contract, publisher+consumer maps, ownership/catalog, migration/deprecation docs | Contract compatibility + ABA003/011 |
| Add new bounded context | Context map, owners, host composition, reusable framework | Contracts/Domain/Application/Infrastructure, all `_Docs`, catalog, ADR, tests | Dependency graph and negative governance |
| Change persistence | Context data ownership, existing store, migration ADRs | Infra code/migrations, `evolution.md`, ADR, ops guide | integration+failure+rollback tests |
| Introduce Wolverine/Redis | Framework adapters, current use sites, ADRs, phase milestones | config, docs, metrics, failure tests, deployment prerequisites | integration/restart/failover tests; never label mock as real |
| Change OAuth/OIDC | `docs/security.md`, Identity context and Host | security tests, configuration guide, threat model; never commit credentials | authn/authz negative cases |
| Fix minor typo | Edited file and obvious context | That file only | quick relevant check; no mandatory ADR |
| Review only | Scope, ownership and tests | Nothing unless explicitly asked | report findings with evidence |
| Change agent policy | `AGENTS.md`, SKILL.md, AI docs and validators | instructions, knowledge map, policy tests | `scripts/ai_check.py check` + negative tests |

## Adding a feature: contract-first path

1. Identify the owning business decision. Do not begin with controller/EF table names.
2. Specify observable acceptance criteria, a successful case, failure/negative cases and invariant tests **before** implementation; check `tests/README.md` for current test capabilities.
3. Decide whether to use an internal command, public versioned contract, or read model.
4. Implement and test in the owner; add consumers through Contracts.
5. Update ownership and context maps exactly where ownership/public dependencies changed.
6. Update contract catalog and compatibility path for public changes.
7. Run governance plus relevant runtime tests; report what was actually executed.

## Anti-drift examples

**Wrong:** Payments directly activates Enrollment after receiving a capture webhook. **Right:** Payments owns capture, publishes `PaymentCapturedV1`; Enrollment consumes evidence and decides activation.

**Wrong:** Reporting fixes an accounting mistake by editing its projection. **Right:** Accounting posts a correction and Reporting rebuilds the projection.

**Wrong:** Add a global `BusinessUtils` project for cross-context entities. **Right:** Keep business models private and publish minimal versioned Contracts.

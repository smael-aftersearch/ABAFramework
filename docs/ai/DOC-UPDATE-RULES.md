# Documentation lifecycle and synchronization contract

**Owner:** the agent or engineer changing behavior owns the corresponding documentation in the same task. Updates should reflect verified behavior, not future plans. Change the knowledge map if agent-readable sources are added, renamed, or removed.

| Change trigger | Update at minimum | Important note |
| --- | --- | --- |
| New/renamed domain data or business rule | Owning `_Docs/ownership.yaml`; domain tests; `decisions.md` if reasoning changed | Preserve one owner |
| New decision/policy or ownership transfer | Ownership metadata, `context-map.yaml`, ADR, affected tests | Do not auto-assign ambiguous ownership |
| Public versioned contract created/changed | Owner's Contracts code, `ownership.yaml`, `context-map.yaml`, `contract-catalog.yaml`, consumers' maps and tests | Incompatible changes require a new version |
| Context dependency introduced/removed | Owning `context-map.yaml`, architecture tests, possible ADR | Metadata cannot legalize direct private references |
| New bounded context | New `_Docs/` files, map, catalog as relevant, docs index/knowledge map, governance tests | Include contracts and isolation tests |
| API route, auth policy, schema or errors changed | OpenAPI + route tests, relevant README and `docs/security.md` | Verify static OpenAPI matches reality |
| Database or messaging semantics changed | `docs/architecture.md`, per-context `evolution.md`, relevant ADR, ops and failure tests | Document recovery/rollback behavior |
| Cache keys/invalidations changed | Cache docs in architecture or context evolution; integration tests | Invalidation correctness matters |
| New operational feature claimed delivered | `docs/implementation-status.json`, README status table, `docs/IMPLEMENTATION-STATUS.md`, `docs/KNOWN-LIMITATIONS.md`, verification report | Do not mark planned work as implemented; attach executable tests and source proof |
| Agent workflow/skill changes | `.agents/skills/aba-engineering/`, `AGENTS.md` if global, knowledge map, agent policy tests | Keep frontmatter triggers precise |
| Test policy or suite changes | `docs/testing/TEST-STRATEGY.md`, `tests/README.md`, knowledge map and CI as applicable | Report executed vs authored/planned; never silently weaken coverage |
| Agent task planner or evidence gates change | Skill scripts + negative tests, `AGENTS.md`, change protocol, CI, verification report | Record exact PASS/FAIL/NOT_RUN; no automatic semantic certification |
| Small typo/refactor with no semantic or contract impact | Edited file, test if applicable | No ADR or broad metadata rewrite needed |

## Always preserve

- `docs/reference/ABA_Adaptive_Business_Architecture_English_Edition.pdf` is a reference source; don't silently rewrite it.
- Keep context YAML files in the **JSON subset of YAML 1.2** until tooling explicitly changes.
- Record why and tradeoffs in an ADR rather than silently overwriting prior decisions.
- Do not delete historical ADRs; supersede them by new records.
- Do **not** run `scripts/generate_metadata.py --force` to update live docs automatically: it uses static seed values and can discard curated ownership. Make targeted changes after inspecting actual code.
- Mark status as **verified**, **authored but not run**, or **planned**.
- Keep `AGENTS.md` short; link detailed reference files here for progressive reading.

## Definition of done for documentation

A material change is document-complete when the published facts, ownership metadata, dependencies, contract catalog, relevant decision history, and actual code agree. Run `python3 scripts/ai_check.py check` plus architecture governance. These scripts check structural consistency, not the full semantic truth of prose; human review and task evidence remain required.

## Machine-readable implementation status (mandatory)

The **generic API template** is tracked independently from the optional Academy example. When a capability's code, verification status, owner, release gate or scope changes, edit `docs/implementation-status.json` first. Mark it Implemented only with evidence from source, focused tests and a reviewed successful CI baseline. Keep partial implementation and unverified deployment tasks explicitly Partial or Not implemented. Then run `python3 scripts/status_matrix.py --write`, `python3 scripts/test_status_matrix.py` and `python3 scripts/status_matrix.py --check`. CI checks both the README table and `docs/IMPLEMENTATION-STATUS.md`. Evidence path existence alone does not prove behavioral correctness; never mark tests as executed until their CI run is successful.

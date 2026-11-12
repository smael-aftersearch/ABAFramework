# AI knowledge index

1. Read `AGENTS.md` first; it is the repository-wide agent contract. For implementation tasks load `.agents/skills/aba-engineering/SKILL.md`.
2. Read `docs/architecture.md` for why the starter is domain neutral and how ownership evolves.
3. Check `metadata/contexts.json` before changing a business capability or publishing a contract.
4. Read `docs/testing/TEST-STRATEGY.md` and update tests with every material behavior change.
5. Locate code and tests in `src/` and `tests/`; don't assume an example application is a context in this repository.
6. When adding a context, create its Domain, Application, Infrastructure and Contracts projects and their ownership/context-map metadata. Keep business policies out of the API host.
7. Include an Architecture impact and Test evidence section in completion reports.

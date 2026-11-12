# Generate a real ABA bounded context

From a generated repository's root with Python 3.10 or newer:

```bash
python3 tools/new_context.py create --name Inventory
python3 tools/new_context.py check
dotnet build
dotnet test
```

Use the **real business name**, never invent example domains just to populate folders. The command adds a pure Domain project, Application orchestration shell, independent public Contracts project, Infrastructure composition, xUnit wiring tests, owner-scoped docs and an entry in `metadata/contexts.json`. It updates the root solution and API composition root so the generated API compiles immediately.

It creates **no domain models, default endpoints, database tables or events** without actual requirements. The public Contracts project initially contains no published API. A context owner must explicitly specify owned data/rules/decisions/contracts and add corresponding positive/negative/invariant tests.

The generator's `check` command validates declared module projects, pure Domain/Contracts dependencies, host wiring and context-specific owner metadata. It rejects unregistered module directories and cross-context project references. This is a structural check, not a complete semantic or IL-level proof.

The generator refuses duplicate names and path-traversal-like identifiers. Avoid editing generated files through external tools without also running `check`. Do not use its output as permission to change another context's private data.

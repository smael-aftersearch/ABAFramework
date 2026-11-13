# Generate a real ABA bounded context

From a generated repository's root with Python 3.10 or newer:

```bash
python3 tools/new_context.py create --name Inventory
python3 tools/new_context.py check
dotnet build
dotnet test tests/Actual.Product.Api.Tests/Actual.Product.Api.Tests.csproj
dotnet test tests/Actual.Product.Inventory.Tests/Actual.Product.Inventory.Tests.csproj
```

Use the **real business name**, never invent example domains just to populate folders. The command adds a pure Domain project, Application orchestration shell, independent public Contracts project, Infrastructure composition, xUnit wiring tests, owner-scoped docs and an entry in `metadata/contexts.json`. It places module projects under the `/Modules/` solution folder and tests under `/Tests/`, then updates the API composition root so the generated API compiles immediately.

It creates **no domain models, default endpoints, database tables or events** without actual requirements. The public Contracts project initially contains no published API. A context owner must explicitly specify owned data/rules/decisions/contracts and add corresponding positive/negative/invariant tests.

The generator's `check` command validates declared module projects, pure Domain/Contracts dependencies, host wiring and context-specific owner metadata. It rejects unregistered module directories, private cross-context project/source imports, framework references or forbidden technical packages in Domain/Contracts, duplicate ownership across contexts, and drift between root and owner-local responsibility maps. CI includes negative cases intentionally breaking two of these rules. This is a structural check, not a complete semantic or IL-level proof.

The generator refuses duplicate names and path-traversal-like identifiers. Avoid editing generated files through external tools without also running `check`. Do not use its output as permission to change another context's private data.

## Optional context-owned EF Core module

When the actual bounded context requires relational storage, use `python3 tools/new_context.py create --name Inventory --with-db`. The generator creates an **empty** `InventoryDbContext` in Infrastructure and registers its provider conditionally based on `Platform:Database:Provider`. The generated xUnit test checks both PostgreSQL and Microsoft SQL Server provider resolution **without needing a database server**, plus startup with Provider=None. The app uses `builder.Services.AddInventoryModule(builder.Configuration)` and still starts with no database in the quickstart.

This only scaffolds an empty context and provider wiring; each real context owns its own mappings, migrations, access authorization, transactional event contracts, and cross-provider real integration tests. Do not add tables or create migrations for imaginary business requirements.

The generated Infrastructure project includes a private EF Core Design reference and a design-time factory that selects the provider from `ABA_DB_PROVIDER` and connection from `ABA_DB_CONNECTION`. This enables owner-approved migrations without shipping a default connection string or adding business entities. Set those values through the local shell/secret manager (never commit them), then from the repository root run:

```bash
ABA_DB_PROVIDER=PostgreSql ABA_DB_CONNECTION='Host=localhost;Database=product;Username=app;Password=...' \
dotnet ef migrations add Initial --project src/Modules/ActualContext/Infrastructure/ActualContext.Infrastructure.csproj \
  --startup-project src/Modules/ActualContext/Infrastructure/ActualContext.Infrastructure.csproj \
  --context ActualContextDbContext --output-dir Persistence/Migrations
```

Generate and apply only after the context owner has approved its model. Verify the resulting migration on both PostgreSQL and SQL Server using disposable databases; the generated factory's provider-selection tests do not connect to either database. Keep business migrations in the owning Infrastructure project and never copy the Platform's technical migration into a business context.

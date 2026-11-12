#!/usr/bin/env python3
"""ABA context generator: no invented business rules, contracts, or entities."""
import argparse
import json
import re
import shutil
import sys
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "metadata/contexts.json"
IDENT = re.compile(r"^[A-Z][A-Za-z0-9]*$")


def identity():
    solutions = list(ROOT.glob("*.slnx"))
    if len(solutions) != 1:
        raise ValueError("Expected exactly one root solution")
    solution = solutions[0]
    tree = ET.parse(solution)
    api_paths = [x.attrib["Path"] for x in tree.getroot().findall("Project")
                 if x.attrib["Path"].endswith(".Api.csproj") and x.attrib["Path"].startswith("src/")]
    if len(api_paths) != 1:
        raise ValueError("Expected one API composition root")
    api = ROOT / api_paths[0]
    ns = api.stem.removesuffix(".Api")
    return solution, tree, api, ns


def proj(refs=(), extra=()):
    body = ['<Project Sdk="Microsoft.NET.Sdk">',
            '  <PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            '<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>']
    if refs or extra:
        body.append('  <ItemGroup>')
        body += [f'    <ProjectReference Include="{x}" />' for x in refs]
        body += [f'    {x}' for x in extra]
        body.append('  </ItemGroup>')
    body.append('</Project>')
    return '\n'.join(body) + '\n'


def files_for(name, ns, with_db=False):
    prefix = f"{ns}.{name}"
    mod = Path("src/Modules") / name
    testdir = Path("tests") / f"{prefix}.Tests"
    domain = f"{prefix}.Domain.csproj"
    app = f"{prefix}.Application.csproj"
    infra = f"{prefix}.Infrastructure.csproj"
    ctxs = f"{prefix}.Contracts.csproj"
    testproj = f"{prefix}.Tests.csproj"
    files = {
        mod / "Domain" / domain: proj(),
        mod / "Contracts" / ctxs: proj(),
        mod / "Application" / app: proj([f"../Domain/{domain}"]),
        mod / "Infrastructure" / infra: proj(
            [f"../Application/{app}",
             f"../../../{ns}.Platform/{ns}.Platform.csproj"],
            ['<FrameworkReference Include="Microsoft.AspNetCore.App" />']),
        testdir / testproj: proj(
            [f"../../src/Modules/{name}/Infrastructure/{infra}"],
            ['<FrameworkReference Include="Microsoft.AspNetCore.App" />',
             '<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />',
             '<PackageReference Include="xunit" Version="2.9.3" />',
             '<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />']),
        mod / "Domain" / "AssemblyMarker.cs":
            f"namespace {prefix}.Domain;\ninternal sealed class AssemblyMarker {{ }}\n",
        mod / "Application" / "AssemblyMarker.cs":
            f"namespace {prefix}.Application;\ninternal sealed class AssemblyMarker {{ }}\n",
        mod / "Infrastructure" / "ModuleRegistration.cs":
            f"using Microsoft.Extensions.DependencyInjection;\n"
            f"namespace {prefix}.Infrastructure;\n"
            f"public sealed class {name}ModuleMarker {{ }}\n"
            f"public static class ModuleRegistration\n{{\n"
            f"    public static IServiceCollection Add{name}Module(this IServiceCollection services)\n"
            f"    {{\n        services.AddSingleton<{name}ModuleMarker>();\n"
            f"        return services;\n    }}\n}}\n",
        mod / "Contracts" / "README.md":
            f"# {name} public contracts\n\nNo business events or DTOs have been published. "
            "Introduce V1 only when owner-approved business requirements exist.\n",
        testdir / "ModuleRegistrationTests.cs":
            f"using {prefix}.Infrastructure;\n"
            f"using Microsoft.Extensions.DependencyInjection;\nusing Xunit;\n"
            f"namespace {prefix}.Tests;\n"
            f"public sealed class ModuleRegistrationTests\n{{\n"
            f"    [Fact]\n    public void ContextRegistersInCompositionRoot()\n"
            f"    {{\n        var services = new ServiceCollection().Add{name}Module();\n"
            f"        using var provider = services.BuildServiceProvider();\n"
            f"        Assert.NotNull(provider.GetRequiredService<{name}ModuleMarker>());\n    }}\n}}\n",
    }
    if with_db:
        # Infrastructure-owned EF model shell; no fictional business entities.
        db_context = mod / "Infrastructure" / "Persistence" / f"{name}DbContext.cs"
        files[db_context] = (
            "using Microsoft.EntityFrameworkCore;\n"
            f"namespace {prefix}.Infrastructure;\n"
            f"public sealed class {name}DbContext(DbContextOptions<{name}DbContext> options)"
            " : DbContext(options)\n{\n"
            "    // Add ONLY context-owned EF mappings after real requirements exist.\n"
            "}\n"
        )
        module_registration = mod / "Infrastructure" / "ModuleRegistration.cs"
        files[module_registration] = (
            f"using {ns}.Platform.Persistence;\n"
            "using Microsoft.EntityFrameworkCore;\n"
            "using Microsoft.Extensions.Configuration;\n"
            "using Microsoft.Extensions.DependencyInjection;\n"
            f"namespace {prefix}.Infrastructure;\n"
            f"public sealed class {name}ModuleMarker {{ }}\n"
            "public static class ModuleRegistration\n{\n"
            f"    public static IServiceCollection Add{name}Module("
            "this IServiceCollection services, IConfiguration config)\n    {\n"
            f"        services.AddSingleton<{name}ModuleMarker>();\n"
            "        var database = AbaDatabaseOptions.FromConfiguration(config);\n"
            "        if (!database.Enabled) return services;\n"
            f"        services.AddDbContext<{name}DbContext>(options =>\n        {{\n"
            "            if (database.Provider == AbaDatabaseProvider.PostgreSql)\n"
            "                options.UseNpgsql(database.ConnectionString);\n"
            "            else if (database.Provider == AbaDatabaseProvider.SqlServer)\n"
            "                options.UseSqlServer(database.ConnectionString);\n"
            "            else\n                throw new InvalidOperationException("
            "\"Unsupported database provider.\");\n"
            "        });\n"
            "        return services;\n    }\n}\n"
        )
        test_path = testdir / "ModuleRegistrationTests.cs"
        files[test_path] = (
            f"using {prefix}.Infrastructure;\n"
            "using Microsoft.Extensions.Configuration;\n"
            "using Microsoft.Extensions.DependencyInjection;\n"
            "using Xunit;\n"
            f"namespace {prefix}.Tests;\n"
            "public sealed class ModuleRegistrationTests\n{\n"
            "    [Fact]\n    public void NoDatabaseIsRequiredForAQuickStart()\n    {\n"
            "        var config = new ConfigurationBuilder().AddInMemoryCollection("
            "new Dictionary<string,string?>()).Build();\n"
            f"        var services = new ServiceCollection().Add{name}Module(config);\n"
            "        using var provider = services.BuildServiceProvider();\n"
            f"        Assert.NotNull(provider.GetRequiredService<{name}ModuleMarker>());\n"
            f"        Assert.Null(provider.GetService<{name}DbContext>());\n"
            "    }\n"
            "    [Theory]\n"
            "    [InlineData(\"PostgreSql\", \"Npgsql.EntityFrameworkCore.PostgreSQL\")]\n"
            "    [InlineData(\"SqlServer\", \"Microsoft.EntityFrameworkCore.SqlServer\")]\n"
            "    public void BothEfProvidersCanBeSelected(string name, string expected)\n    {\n"
            "        var config = new ConfigurationBuilder().AddInMemoryCollection("
            "new Dictionary<string,string?>\n        {\n"
            "            [\"Platform:Database:Provider\"] = name,\n"
            "            [\"ConnectionStrings:AbaDatabase\"] = name == \"PostgreSql\"\n"
            "                ? \"Host=localhost;Database=aba;Username=app;Password=test\"\n"
            "                : \"Server=localhost;Database=aba;User ID=sa;Password=Test!12345;TrustServerCertificate=True\"\n"
            "        }).Build();\n"
            "        var services = new ServiceCollection();\n"
            "        services.AddLogging();\n"
            f"        services.Add{name}Module(config);\n"
            "        using var provider = services.BuildServiceProvider();\n"
            "        using var scope = provider.CreateScope();\n"
            f"        var db = scope.ServiceProvider.GetRequiredService<{name}DbContext>();\n"
            "        Assert.Equal(expected, db.Database.ProviderName);\n"
            "    }\n}\n"
        )
        files[mod / "Infrastructure" / "Persistence" / "README.md"] = (
            f"# {name} owned persistence\n\n"
            "This optional EF Core DbContext has no business entities and no migrations yet. "
            "Create context-owned migrations only after real data/rules are defined. "
            "Use the shared database provider selection but do not share other contexts' tables.\n"
        )
    docs = mod / "_Docs"
    metadata = {
        "ownership.yaml": {"context": name, "owner": name, "owns": {
            "data": [], "businessRules": [], "decisions": [], "publicContracts": []}},
        "context-map.yaml": {"context": name, "publishes": [], "consumes": [],
                             "forbidden": ["Other contexts' private implementations and tables"]},
        "ai-rules.yaml": {"context": name, "rules": [
            "Read ownership and contracts before coding.",
            "Keep Domain free of technical frameworks.",
            "Use versioned public contracts for cross-context communication.",
            "Add positive, negative and invariant tests with every behavior change."]},
    }
    for filename, value in metadata.items():
        files[docs / filename] = json.dumps(value, indent=2) + '\n'
    files[docs / "decisions.md"] = f"# {name} decisions\n\nNo business decision assigned yet.\n"
    files[docs / "evolution.md"] = f"# {name} evolution\n\nScaffold created. No business behavior.\n"
    projects = [mod / layer / p for layer, p in
                (("Domain", domain), ("Contracts", ctxs),
                 ("Application", app), ("Infrastructure", infra))]
    projects.append(testdir / testproj)
    return files, projects


def check():
    solution, xml, api, ns = identity()
    catalog = json.loads(CATALOG.read_text())
    records = catalog["contexts"]
    seen = set()
    # A context's ownership metadata is part of its API. Two owners for the
    # same declared responsibility are not allowed to pass CI.
    responsibility_owners = {}
    declared = {x.attrib["Path"] for x in xml.getroot().findall("Project")}
    for record in records:
        name = record["name"]
        if not IDENT.fullmatch(name) or name.lower() in seen:
            raise ValueError(f"Invalid or duplicate context name: {name}")
        seen.add(name.lower())
        files, projects = files_for(name, ns, with_db=record.get("efCore", False))
        if record["owner"] != name or record["path"] != f"src/Modules/{name}":
            raise ValueError("Context ownership/path metadata mismatch")
        owned = record.get("owns")
        if not isinstance(owned, dict):
            raise ValueError(f"Missing explicit ownership map: {name}")
        for category in ("data", "businessRules", "decisions", "publicContracts"):
            items = owned.get(category)
            if not isinstance(items, list):
                raise ValueError(f"Invalid ownership category {name}/{category}")
            for item in items:
                if not isinstance(item, str) or not item.strip():
                    raise ValueError(f"Invalid owned responsibility in {name}/{category}")
                key = (category, item.strip().casefold())
                owner = responsibility_owners.setdefault(key, name)
                if owner != name:
                    raise ValueError(f"Two owners for {category}:{item}: {owner} and {name}")
        for rel in projects:
            if not (ROOT / rel).is_file() or rel.as_posix() not in declared:
                raise ValueError(f"Missing project in solution: {rel}")
            project_xml = ET.parse(ROOT / rel).getroot()
            refs = project_xml.findall(".//ProjectReference")
            if rel.parent.name in ("Domain", "Contracts"):
                if refs or project_xml.findall(".//FrameworkReference"):
                    raise ValueError(f"Private or technical dependencies in pure boundary: {rel}")
                for package in project_xml.findall(".//PackageReference"):
                    package_name = package.attrib.get("Include", "")
                    if re.search(r"(?i)(EntityFramework|AspNetCore|Wolverine|Redis|Npgsql|SqlClient|Dapper)", package_name):
                        raise ValueError(f"Forbidden technical package in pure boundary: {rel}: {package_name}")
            for ref in refs:
                dest = (ROOT / rel.parent / ref.attrib["Include"]).resolve()
                other = ROOT / "src/Modules"
                if (dest.is_relative_to(other) and not dest.is_relative_to(other / name)
                        and dest.parent.name != "Contracts"):
                    raise ValueError(f"Forbidden cross-context implementation dependency: {rel}")
        for filename in ("ownership.yaml", "context-map.yaml", "ai-rules.yaml"):
            info = json.loads((ROOT / "src/Modules" / name / "_Docs" / filename).read_text())
            if info.get("context") != name:
                raise ValueError(f"Wrong metadata owner: {name}/{filename}")
            if filename == "ownership.yaml":
                if info.get("owner") != name or info.get("owns") != owned:
                    raise ValueError(f"Root and context ownership metadata drift: {name}")
        # Text imports are checked as a conservative pre-compiler guard. Real
        # compilation and runtime/architecture tests are additional CI gates.
        for source in (ROOT / "src/Modules" / name).rglob("*.cs"):
            code = source.read_text(encoding="utf-8")
            layer = source.relative_to(ROOT / "src/Modules" / name).parts[0]
            if layer in ("Domain", "Contracts") and re.search(
                r"\b(?:global\s+)?using\s+(?:static\s+)?"
                r"(?:Microsoft\.(?:EntityFrameworkCore|AspNetCore)|"
                r"StackExchange\.Redis|Wolverine|Npgsql|Microsoft\.Data\.SqlClient)\b",
                code,
            ):
                raise ValueError(f"Technical framework imported into pure boundary: {source}")
            for other_record in records:
                other = other_record["name"]
                if other == name:
                    continue
                forbidden = rf"\b(?:global\s+)?using\s+(?:static\s+)?{re.escape(ns)}\.{re.escape(other)}\.(?:Domain|Application|Infrastructure)\b"
                if re.search(forbidden, code):
                    raise ValueError(f"Forbidden cross-context implementation import: {source}")
        if f"../Modules/{name}/Infrastructure/{ns}.{name}.Infrastructure.csproj" not in api.read_text():
            raise ValueError(f"Missing host project reference for {name}")
        registration = ("builder.Configuration" if record.get("efCore", False) else "")
        if f"builder.Services.Add{name}Module({registration});" not in (api.parent / "Program.cs").read_text():
            raise ValueError(f"Missing host service registration for {name}")
    modules = ROOT / "src/Modules"
    extras = ({p.name.lower() for p in modules.iterdir() if p.is_dir()} - seen) if modules.exists() else set()
    if extras:
        raise ValueError(f"Unregistered module folder(s): {sorted(extras)}")
    print(f"PASS: {len(records)} context boundary/boundaries verified")
    return records


def create(name, with_db=False):
    if not IDENT.fullmatch(name):
        raise ValueError("Context name must start with a capital letter and contain only letters/digits")
    records = check()
    if any(x["name"].lower() == name.lower() for x in records):
        raise ValueError(f"Duplicate context: {name}")
    solution, xml, api, ns = identity()
    srcdir = ROOT / "src/Modules" / name
    testdir = ROOT / "tests" / f"{ns}.{name}.Tests"
    if srcdir.exists() or testdir.exists():
        raise ValueError("Refusing to overwrite existing source/test folder")
    files, projects = files_for(name, ns)
    progpath = api.parent / "Program.cs"
    program = progpath.read_text()
    hook = "builder.Services.AddHealthChecks();"
    if hook not in program:
        raise ValueError("API composition anchor missing")
    program = f"using {ns}.{name}.Infrastructure;\n" + program.replace(
        hook, hook + f"\nbuilder.Services.Add{name}Module(" +
        ("builder.Configuration" if with_db else "") + ");", 1)
    api_tree = ET.parse(api)
    group = next((g for g in api_tree.getroot().findall("ItemGroup")
                  if g.find("ProjectReference") is not None), None)
    if group is None:
        group = ET.SubElement(api_tree.getroot(), "ItemGroup")
    ET.SubElement(group, "ProjectReference",
                  Include=f"../Modules/{name}/Infrastructure/{ns}.{name}.Infrastructure.csproj")
    for rel in projects:
        ET.SubElement(xml.getroot(), "Project", Path=rel.as_posix())
    ET.indent(xml, space="  ")
    ET.indent(api_tree, space="  ")
    catalog = json.loads(CATALOG.read_text())
    catalog["contexts"].append({
        "name": name, "owner": name, "path": f"src/Modules/{name}",
        "efCore": with_db,
        "owns": {"data": [], "businessRules": [], "decisions": [], "publicContracts": []}})
    catalog["contexts"].sort(key=lambda x: x["name"].lower())
    replacements = {
        solution: ET.tostring(xml.getroot(), encoding="unicode") + "\n",
        api: ET.tostring(api_tree.getroot(), encoding="unicode") + "\n",
        progpath: program,
        CATALOG: json.dumps(catalog, indent=2) + "\n"
    }
    original = {path: path.read_bytes() for path in replacements}
    try:
        for rel, content in files.items():
            target = ROOT / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding="utf-8")
        for path, content in replacements.items():
            path.write_text(content, encoding="utf-8")
        check()
    except Exception:
        for path, raw in original.items():
            path.write_bytes(raw)
        shutil.rmtree(srcdir, ignore_errors=True)
        shutil.rmtree(testdir, ignore_errors=True)
        raise
    print(f"CREATED: {name} Domain, Application, Infrastructure, Contracts, tests, ownership and host wiring" + (" with isolated EF Core" if with_db else ""))


def main():
    parser = argparse.ArgumentParser(description="ABA generic bounded-context generator")
    parser.add_argument("command", choices=("create", "check", "list"))
    parser.add_argument("--name")
    parser.add_argument("--with-db", action="store_true", help="Generate a context-owned EF Core DbContext and both SQL provider wiring")
    args = parser.parse_args()
    try:
        if args.command == "create":
            if not args.name:
                parser.error("create requires --name")
            create(args.name, with_db=args.with_db)
        elif args.command == "check":
            check()
        else:
            for record in check():
                print(record["name"])
    except (ValueError, KeyError, OSError, json.JSONDecodeError, ET.ParseError) as exc:
        print(f"ABA CONTEXT ERROR: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())

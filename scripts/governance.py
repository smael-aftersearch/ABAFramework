#!/usr/bin/env python3
"""Executable, dependency-free ABA governance for the phase-1 source repository.

The metadata `.yaml` files deliberately use the JSON subset of YAML 1.2,
so the Python standard library is sufficient for validation.
"""
import argparse
import json
import re
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

MODULES = ('Identity', 'Enrollment', 'Payments', 'Accounting', 'Reporting')
REQUIRED_METADATA = ('ownership.yaml', 'context-map.yaml', 'ai-rules.yaml', 'decisions.md', 'evolution.md')


def validate(root: Path) -> list[str]:
    violations = []

    def fail(code: str, message: str, remedy: str) -> None:
        violations.append(f'{code}: {message}\n  Fix: {remedy}')

    def load_json(path: Path, code: str):
        try:
            return json.loads(path.read_text(encoding='utf-8'))
        except (OSError, ValueError) as exc:
            fail(code, f'Cannot load {path.relative_to(root)}: {exc}',
                 'Restore valid machine-readable metadata (YAML 1.2 JSON subset).')
            return {}

    project_paths = sorted(root.rglob('*.csproj'))
    if len(project_paths) < 10:
        fail('ABA000', 'Expected more than 10 real project files.', 'Restore the solution projects.')

    project_names = {p.resolve(): p.stem for p in project_paths}
    graph: dict[Path, list[Path]] = {}
    for project in project_paths:
        try:
            xml = ET.parse(project)
        except ET.ParseError as exc:
            fail('ABA000', f'Malformed project {project.name}: {exc}', 'Correct the XML.')
            continue
        dependencies = []
        for node in xml.findall('.//ProjectReference'):
            relative = node.attrib.get('Include', '')
            target = (project.parent / relative).resolve()
            if target not in project_names:
                fail('ABA000', f'{project.name} references missing project {relative}',
                     'Use an existing project and a valid relative path.')
            dependencies.append(target)
        graph[project.resolve()] = dependencies

    # Context modules may depend on another context's Contracts ONLY.
    for context in MODULES:
        module = root / 'samples' / 'Academy' / context
        path = (module / f'Academy.{context}.csproj').resolve()
        for reference in graph.get(path, []):
            target_name = project_names.get(reference, reference.stem)
            match = re.match(r'Academy\.(Identity|Enrollment|Payments|Accounting|Reporting)(\.Contracts)?$', target_name)
            if match and match.group(1) != context and not match.group(2):
                fail('ABA001', f'{context} references private project {target_name}',
                     f'Remove this reference; depend on Academy.{match.group(1)}.Contracts instead.')

        for source in (module / 'Domain').rglob('*.cs'):
            text = source.read_text(encoding='utf-8')
            for forbidden in ('Microsoft.AspNetCore', 'Microsoft.EntityFrameworkCore',
                              'Aba.LocalMessaging', 'Aba.Caching', 'StackExchange.Redis', 'Wolverine'):
                if forbidden in text:
                    fail('ABA002', f'{source.relative_to(root)} imports {forbidden}',
                         'Move infrastructure access to Application/Infrastructure; keep domain pure.')
            for other in MODULES:
                if other != context and re.search(r'\bAcademy\.' + other + r'\b', text):
                    fail('ABA002', f'{source.relative_to(root)} references other context {other}',
                         'Use a public contract in the Application layer, not Domain.')

        docs = module / '_Docs'
        for name in REQUIRED_METADATA:
            if not (docs / name).is_file():
                fail('ABA004', f'Missing {context}/_Docs/{name}',
                     'Add the required ABA metadata or architectural document.')
        ownership = load_json(docs / 'ownership.yaml', 'ABA004') if (docs / 'ownership.yaml').exists() else {}
        context_map = load_json(docs / 'context-map.yaml', 'ABA004') if (docs / 'context-map.yaml').exists() else {}
        rules = load_json(docs / 'ai-rules.yaml', 'ABA004') if (docs / 'ai-rules.yaml').exists() else {}
        if ownership.get('context') != context or context_map.get('context') != context:
            fail('ABA004', f'{context}: context name mismatch in metadata',
                 'Set context to the owning bounded-context name in both files.')
        for category in ('data_ownership','rule_ownership','decision_ownership','contract_ownership'):
            members = ownership.get(category)
            if not isinstance(members, dict) or not members:
                fail('ABA004', f'{context}: missing/empty {category}',
                     'Declare exactly one owner for each relevant concept.')
            elif any(not isinstance(x, dict) or x.get('owner') != context for x in members.values()):
                fail('ABA004', f'{context}: owner inconsistent in {category}',
                     'All concepts in a context must explicitly identify their unique owner.')
        for field in ('publishes','consumes','allowed_dependencies','forbidden_dependencies','owned_capabilities'):
            if not isinstance(context_map.get(field), list):
                fail('ABA004', f'{context}: invalid {field} in context-map.yaml',
                     'Use an explicit list, including [] for empty lists.')
        if not isinstance(rules.get('must_read'), list) or not isinstance(rules.get('forbidden'), list):
            fail('ABA004', f'{context}: AI agent rules incomplete',
                 'Record mandatory context files and forbidden actions.')

        declared_references = {project_names.get(ref, ref.stem) for ref in graph.get(path, [])}
        allowed = set(context_map.get('allowed_dependencies', []))
        unlisted = declared_references - allowed
        if unlisted:
            fail('ABA006', f'{context} references unapproved dependencies {sorted(unlisted)}',
                 'Update context-map.yaml only after reviewing and accepting the dependency.')
        forbidden = declared_references.intersection(context_map.get('forbidden_dependencies', []))
        if forbidden:
            fail('ABA006', f'{context} references forbidden dependencies {sorted(forbidden)}',
                 'Remove the dependency and use public Contracts.')

        contracts_dir = root / 'samples' / 'Academy' / f'{context}.Contracts'
        declared = set()
        for source in contracts_dir.rglob('*.cs'):
            content = source.read_text(encoding='utf-8')
            for match in re.finditer(r'\bpublic\s+(?:sealed\s+)?(?:record|interface)\s+(\w+)', content):
                declared.add(match.group(1))
        for name in declared:
            if not re.search(r'V[1-9][0-9]*$', name):
                fail('ABA003', f'{context} publishes unversioned public contract {name}',
                     'Publish a new explicit V1/V2 contract; do not share domain entities.')
        declared_in_metadata = set(ownership.get('contract_ownership', {}))
        if declared != declared_in_metadata:
            fail('ABA011', f'{context} contract ownership differs: code={sorted(declared)}, metadata={sorted(declared_in_metadata)}',
                 'Update ownership.yaml and the Contracts project together.')

        published = set(context_map.get('publishes', []))
        if not published.issubset(declared):
            fail('ABA011', f'{context} publishes contracts absent from its own Contracts project',
                 'Add the public event records to the owning Contracts project.')
        # Published events must use the V<number> convention, not an internal DTO.
        for name in published:
            if not re.search(r'V[1-9][0-9]*$', name):
                fail('ABA003', f'{context} has unversioned event {name}', 'Rename and version it.')

    # Framework is technical, with no references to sample business modules.
    for source in (root / 'src' / 'Framework').rglob('*.cs'):
        if re.search(r'\bAcademy\.(Identity|Enrollment|Payments|Accounting|Reporting)\b',
                     source.read_text(encoding='utf-8')):
            fail('ABA009', f'Framework has business-specific dependency: {source.relative_to(root)}',
                 'Move policy to the owning context; keep framework reusable.')

    accounting_path = root / 'samples' / 'Academy' / 'Accounting'
    for source in accounting_path.rglob('*.cs'):
        if re.search(r'\b(?:class|record|interface|void|Task)\s+(?:Update|Delete)PostedJournal\b',
                     source.read_text(encoding='utf-8')):
            fail('ABA010', f'Unsafe immutable-journal command in {source.relative_to(root)}',
                 'Use reversal/correction journals; never edit posted journal history.')

    # Cross-reference the global contract catalog with declared public types.
    catalog_path = root / 'contract-catalog.yaml'
    catalog = load_json(catalog_path, 'ABA011') if catalog_path.is_file() else {}
    records = catalog.get('contracts', {})
    if not isinstance(records, dict):
        fail('ABA011', 'Contract catalog is not a mapping.', 'Restore the catalog structure.')
        records = {}
    for context in MODULES:
        ownership_path = root / 'samples' / 'Academy' / context / '_Docs' / 'ownership.yaml'
        if not ownership_path.is_file():
            continue
        owns = load_json(ownership_path, 'ABA011').get('contract_ownership', {})
        for name in owns:
            entry = records.get(name, {})
            if entry.get('owner') != context or entry.get('version') != 1:
                fail('ABA011', f'Contract {name} missing or incorrect in catalog.',
                     'Record owner, explicit version and consumers in contract-catalog.yaml.')

    # Detect dependency cycles in any solution projects.
    color: dict[Path, int] = {}
    def visit(path: Path, chain: tuple[Path, ...]) -> None:
        if color.get(path) == 1:
            fail('ABA012', 'Circular project references: ' + ' -> '.join(p.stem for p in chain + (path,)),
                 'Remove cycles; separate stable versioned Contracts from implementations.')
            return
        if color.get(path) == 2:
            return
        color[path] = 1
        for nxt in graph.get(path, []):
            if nxt in graph:
                visit(nxt, chain + (path,))
        color[path] = 2
    for p in graph:
        visit(p, ())

    props = root / 'Directory.Build.props'
    if not props.is_file() or 'net10.0' not in props.read_text(encoding='utf-8'):
        fail('ABA000', 'Missing net10.0 target in Directory.Build.props',
             'Target .NET 10 consistently and pin the SDK in global.json.')

    api_spec = root / 'samples' / 'Academy' / 'Host' / 'openapi-v1.json'
    spec = load_json(api_spec, 'API001') if api_spec.is_file() else {}
    if not spec.get('openapi') or not spec.get('paths'):
        fail('API001', 'Development API specification missing/invalid',
             'Maintain a matching OpenAPI document.')

    return violations


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.root.resolve()
    violations = validate(root)
    if violations:
        print('\n\n'.join(violations), file=sys.stderr)
        print(f'FAILED: {len(violations)} ABA/API governance finding(s)', file=sys.stderr)
        return 1
    print('PASS: ABA governance (project edges, domain isolation, metadata, contracts, catalog, cycles, API spec)')
    return 0


if __name__ == '__main__':
    sys.exit(main())

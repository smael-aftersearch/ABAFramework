#!/usr/bin/env python3
"""A basic pre-compiler structural scan, NOT a substitute for `dotnet build`."""
import json
import re
import sys
from pathlib import Path
from xml.etree import ElementTree as ET

root = Path(__file__).resolve().parents[1]
errors = []
source_count = 0
for path in root.rglob('*.cs'):
    source_count += 1
    source = path.read_text(encoding='utf-8')
    # Strip comments and ordinary/interpolated strings before balancing delimiters.
    source = re.sub(r'/\*.*?\*/', '', source, flags=re.S)
    source = re.sub(r'//[^\n]*', '', source)
    source = re.sub(r'@"(?:[^"]|"")*"', '""', source)
    source = re.sub(r'\$?"(?:\\.|[^"\\])*"', '""', source)
    stack = []
    for position, char in enumerate(source):
        if char in '({[':
            stack.append((char, position))
        elif char in ')}]':
            if not stack or '({['[')}]'.index(char)] != stack[-1][0]:
                errors.append(f'{path.relative_to(root)}: mismatched {char} at {position}')
            else:
                stack.pop()
    if stack:
        errors.append(f'{path.relative_to(root)}: unclosed delimiters {stack[-3:]}')

# A dotnet-new template is a separately buildable SOLUTION, not part of the repository's
# current Academy solution. Verify template compilation independently in CI.
project_paths = {
    path.relative_to(root).as_posix()
    for path in root.rglob('*.csproj')
    if path.relative_to(root).parts[0] not in ('templates', 'artifacts')
}
try:
    solution = ET.parse(root / 'ABA.Net10.Phase1.slnx').getroot()
    solution_paths = {node.attrib['Path'] for node in solution.findall('Project')}
    if project_paths != solution_paths:
        errors.append('Solution/project mismatch: ' + repr(sorted(project_paths ^ solution_paths)))
except Exception as exc:
    errors.append(f'Cannot parse solution: {exc}')

for path in root.rglob('*.yaml'):
    if path.parent.name == 'infra' or (path.parent.name == 'agents' and path.name == 'openai.yaml'):
        continue  # Docker Compose and Skill UI metadata use ordinary YAML.
    try:
        json.loads(path.read_text(encoding='utf-8'))
    except Exception as exc:
        errors.append(f'{path.relative_to(root)}: invalid metadata JSON/YAML: {exc}')

if errors:
    print('\n'.join(errors), file=sys.stderr)
    sys.exit(1)
print(f'PASS: basic delimiter scan on {source_count} C# files, solution with {len(project_paths)} projects, metadata parsing.')
print('NOTE: This cannot prove that C# compiles; install .NET 10 and run dotnet build.')

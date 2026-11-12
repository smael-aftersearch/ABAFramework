#!/usr/bin/env python3
"""Negative tests must prove architecture governance really rejects violations."""
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

source = Path(__file__).resolve().parents[1]
checker = source/'scripts'/'governance.py'

def expect(root: Path, expected_ok: bool, label: str, code: str = ''):
    result = subprocess.run([sys.executable, str(checker), '--root', str(root)],
                            capture_output=True, text=True)
    content = result.stdout + result.stderr
    if (result.returncode == 0) != expected_ok or (code and code not in content):
        raise AssertionError(f'{label}: unexpected governance result {result.returncode}\n{content}')
    print('PASS:', label)

expect(source, True, 'Clean repository accepted')
with tempfile.TemporaryDirectory(prefix='aba-governance-') as folder:
    root = Path(folder)/'repo'
    shutil.copytree(source, root, ignore=shutil.ignore_patterns('bin','obj','.git','local-data'))
    project = root/'samples/Academy/Payments/Academy.Payments.csproj'
    project.write_text(project.read_text().replace('</ItemGroup>',
        '  <ProjectReference Include="../Accounting/Academy.Accounting.csproj" />\n  </ItemGroup>', 1))
    expect(root, False, 'Cross-context implementation reference rejected', 'ABA001')

with tempfile.TemporaryDirectory(prefix='aba-governance-') as folder:
    root = Path(folder)/'repo'
    shutil.copytree(source, root, ignore=shutil.ignore_patterns('bin','obj','.git','local-data'))
    (root/'samples/Academy/Identity/_Docs/ownership.yaml').unlink()
    expect(root, False, 'Missing ownership metadata rejected', 'ABA004')

with tempfile.TemporaryDirectory(prefix='aba-governance-') as folder:
    root = Path(folder)/'repo'
    shutil.copytree(source, root, ignore=shutil.ignore_patterns('bin','obj','.git','local-data'))
    contracts = root/'samples/Academy/Payments.Contracts/PaymentContracts.cs'
    contracts.write_text(contracts.read_text().replace('PaymentCapturedV1', 'PaymentCaptured'))
    expect(root, False, 'Unversioned event contract rejected', 'ABA003')

print('ALL ABA GOVERNANCE NEGATIVE TESTS PASSED')

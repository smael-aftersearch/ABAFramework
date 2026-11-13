#!/usr/bin/env python3
"""Deterministic task/test planning and verification evidence for ABA repositories.

No external dependencies. This helper cannot decide business ownership or prove
that a test suite exercises the requested behavior; the agent must inspect code.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time


CONTEXTS = ('Identity', 'Enrollment', 'Payments', 'Accounting', 'Reporting')
RULES = {
    'domain': {
        'match': ('aggregate', 'domain', 'invariant', 'policy', 'capacity', 'journal', 'refund', 'transition', 'rule', 'ظرفیت', 'قانون', 'بازپرداخت'),
        'cases': ['Valid state transition and business result', 'Invalid transition and boundary values rejected', 'Owned invariant preserved under retries/concurrency'],
        'test': 'Focused owning-context unit tests (planned; use existing smoke coverage until verified)',
    },
    'contracts': {
        'match': ('contract', 'event', 'schema', 'version', 'integration event', 'قرارداد', 'رویداد'),
        'cases': ['Producer and consumer agree on versioned payload', 'Old V1 consumer remains compatible or V2 is introduced', 'Out-of-order/duplicate message behavior is defined'],
        'test': 'Contract compatibility and consumer tests (currently planned)',
    },
    'messaging': {
        'match': ('outbox', 'inbox', 'wolverine', 'broker', 'messaging', 'retry', 'idempotent', 'idempotency', 'dead-letter', 'پیام', 'تکراری'),
        'cases': ['Business state and outbox message stored atomically', 'Duplicate delivery does not duplicate side effects', 'Transient failure retries; exhausted retries are diagnosable'],
        'test': 'Messaging integration/failure tests; local smoke exists, external broker tests planned',
    },
    'persistence': {
        'match': ('database', 'postgres', 'dbcontext', 'ef core', 'migration', 'transaction', 'sql', 'دیتابیس', 'تراکنش'),
        'cases': ['Expected writes and reads persist after restart', 'Transaction rollback and unique constraints prevent corruption', 'One context cannot modify another context storage'],
        'test': 'Real database migration/transaction integration tests (planned)',
    },
    'caching': {
        'match': ('redis', 'cache', 'caching', 'ttl', 'invalidate', 'کش', 'ردیس'),
        'cases': ['Cache miss and hit return correct data', 'State change invalidates affected cached result', 'TTL, tenant isolation and cache outage have safe behavior'],
        'test': 'Cache integration/failure tests; only memory cache exists now',
    },
    'security': {
        'match': ('security', 'oauth', 'oidc', 'jwt', 'token', 'permission', 'auth', 'tenant', 'امنیت', 'مجوز', 'احراز هویت'),
        'cases': ['Authorized request succeeds with least privilege', 'Unauthenticated and forbidden requests return 401/403', 'Tenant boundary and sensitive error response cannot leak information'],
        'test': 'HTTP authorization/tenant integration tests (planned)',
    },
    'api': {
        'match': ('api', 'endpoint', 'http', 'openapi', 'controller', 'route', 'swagger', 'مسیر', 'وب سرویس'),
        'cases': ['Successful route response matches public schema', 'Bad request/conflict/not found return correct status', 'OpenAPI and implementation remain aligned'],
        'test': 'Host/API HTTP integration tests (planned)',
    },
    'architecture': {
        'match': ('architecture', 'governance', 'dependency', 'bounded context', 'metadata', 'skill', 'agent', 'ci', 'pipeline', 'معماری', 'ایجنت'),
        'cases': ['Compliant dependency/metadata validates', 'A deliberately forbidden dependency or missing ownership is rejected', 'Agent and CI entrypoints agree on the source of truth'],
        'test': 'Python governance and agent-readiness tests (implemented)',
    },
    'general': {
        'match': (),
        'cases': ['Expected behavior and observable output', 'Invalid input or failure is handled safely', 'Existing behavior is not regressed'],
        'test': 'Inspect affected code and select the smallest real test; owner unresolved until confirmed',
    },
    'testing': {
        'match': ('test', 'testing', 'xunit', 'smoke', 'coverage', 'verify', 'qa', 'تست', 'آزمون'),
        'cases': ['Changed behavior has a reproducible success test', 'Relevant negative test fails against broken behavior', 'Gate reports absent runtime/coverage as NOT_RUN, not PASS'],
        'test': 'Test-suite tests and evidence checks (Python implemented; .NET suite not verified)',
    },
}


def clean_file(root: Path, raw: str) -> str:
    path = Path(raw)
    if path.is_absolute() or not (root / path).resolve().is_relative_to(root.resolve()):
        raise ValueError(f'Unsafe file path outside repository: {raw}')
    return path.as_posix().removeprefix('./')


def discover_xunit_test_projects(root: Path) -> list[str]:
    """Find checked-in xUnit projects in the repo and generated templates."""
    search_roots = [root / 'tests']
    templates = root / 'templates'
    if templates.is_dir():
        search_roots.extend(sorted(path / 'tests' for path in templates.iterdir()
                                   if path.is_dir() and (path / 'tests').is_dir()))

    projects = set()
    ignored_parts = {'.git', 'artifacts', 'bin', 'obj'}
    xunit_reference = re.compile(
        r'<PackageReference\b(?=[^>]*\bInclude\s*=\s*[\"\']xunit(?:\.v\d+)?[\"\'])[^>]*>',
        re.IGNORECASE,
    )
    for search_root in search_roots:
        if not search_root.is_dir():
            continue
        for project in search_root.rglob('*.csproj'):
            if any(part.casefold() in ignored_parts for part in project.relative_to(root).parts):
                continue
            try:
                contents = project.read_text(encoding='utf-8')
            except (OSError, UnicodeError):
                continue
            if xunit_reference.search(contents):
                projects.add(project.relative_to(root).as_posix())
    return sorted(projects)


def match_term(term: str, body: str) -> bool:
    term = term.casefold()
    body = body.casefold()
    if term.isascii():
        return bool(re.search(r'(?<![a-z])' + re.escape(term) + r'(?:s|es)?(?![a-z])', body))
    return term in body


def classify_task(root: Path, task: str, files: list[str]) -> dict:
    root = root.resolve()
    if not (root / 'docs/ai/knowledge-map.json').is_file():
        raise ValueError('Missing docs/ai/knowledge-map.json: cannot propose owners or reading paths')
    knowledge = json.loads((root / 'docs/ai/knowledge-map.json').read_text(encoding='utf-8'))
    normalized = [clean_file(root, raw) for raw in files]
    haystack = task + ' ' + ' '.join(normalized)
    contexts = []
    recommended = list(knowledge.get('always', []))
    for ctx, record in knowledge.get('contexts', {}).items():
        if (any(match_term(k, haystack) for k in record['keywords'])
                or any(f'/Academy/{ctx}/' in f'/{f}' or f'/Academy/{ctx}.Contracts/' in f'/{f}' for f in normalized)):
            contexts.append(ctx)
            recommended.extend(record['docs'])
    topics = []
    for name, record in knowledge.get('topics', {}).items():
        if any(match_term(k, haystack) for k in record['keywords']):
            topics.append(name)
            recommended.extend(record['docs'])
    for file in normalized:
        if file.startswith('src/Framework/'):
            recommended.extend(['docs/architecture.md', 'docs/ai/DOC-UPDATE-RULES.md'])
        elif file.startswith('samples/Academy/Host/'):
            recommended.extend(['samples/Academy/Host/openapi-v1.json', 'docs/security.md'])
    categories = set()
    for name, info in RULES.items():
        if any(match_term(term, haystack) for term in info['match']):
            categories.add(name)
    for file in normalized:
        lower = file.casefold()
        if '/domain/' in lower or lower.endswith('domain.cs'):
            categories.add('domain')
        if '.contracts/' in lower or '/contracts/' in lower:
            categories.add('contracts')
        if '/host/' in lower or 'openapi' in lower:
            categories.add('api')
        if 'localmessaging/' in lower:
            categories.add('messaging')
        if '/aba.caching/' in lower:
            categories.add('caching')
        if '/_docs/' in lower or '.agents/skills/' in lower or '/docs/ai/' in lower:
            categories.add('architecture')
    if not categories:
        categories.add('general')
    test_cases = []
    for category in RULES:
        if category in categories:
            spec = RULES[category]
            test_cases.append({'category': category, 'test_target': spec['test'], 'acceptance_examples': spec['cases']})
    existing = {
        'console_smoke_source': (root / 'tests/Aba.SmokeTests/Program.cs').is_file(),
        'python_governance_tests': (root / 'scripts/test_governance.py').is_file(),
        'python_agent_tests': (root / 'scripts/test_ai_check.py').is_file(),
        'xunit_test_projects': discover_xunit_test_projects(root),
    }
    return {
        'schema_version': 1,
        'task': task,
        'files': normalized,
        'possible_contexts': contexts,
        'ownership_confirmed': False,
        'ownership_warning': 'Candidates only. Inspect ownership.yaml, decisions.md, contracts, implementation and consumers before edits.',
        'matched_topics': topics,
        'reading_paths': list(dict.fromkeys(recommended)),
        'test_categories': test_cases,
        'existing_test_inventory': existing,
        'execution_rule': 'Define success, negative and invariant tests, implement with docs, then run narrow tests, architecture checks, and actual runtime verification.',
    }


def format_plan(plan: dict) -> str:
    lines = ['ABA TASK PLAN (suggestion, NOT an ownership decision)',
             f"Task: {plan['task']}",
             'Possible contexts: ' + (', '.join(plan['possible_contexts']) or 'UNRESOLVED'),
             'Confirm each owner in metadata and code before implementing.',
             'Read these sources selectively:']
    lines.extend(f'  - {item}' for item in plan['reading_paths'])
    lines.append('Test obligations:')
    for item in plan['test_categories']:
        lines.append(f"  [{item['category']}] {item['test_target']}")
        lines.extend(f'    - {test}' for test in item['acceptance_examples'])
    present = plan['existing_test_inventory']
    projects = present['xunit_test_projects']
    project_list = ', '.join(projects) if projects else 'none'
    lines.append('Inventory: console smoke source=' + str(present['console_smoke_source']) +
                 '; Python governance tests=' + str(present['python_governance_tests']) +
                 '; .NET xUnit projects=' + project_list)
    lines.append('Existing source does not imply that C# tests ran. Verify and report PASS/FAIL/NOT_RUN.')
    return '\n'.join(lines)


def check_command(root: Path, name: str, command: list[str], timeout: int) -> dict:
    started = time.monotonic()
    try:
        result = subprocess.run(command, cwd=root, capture_output=True, text=True, encoding='utf-8',
                                errors='replace', timeout=timeout, check=False)
        status = 'PASS' if result.returncode == 0 else 'FAIL'
        detail = (result.stderr or result.stdout)[-1000:] if status == 'FAIL' else ''
        # Evidence is shareable; do not store raw command output or environment secrets.
        return {'name': name, 'command': command, 'status': status,
                'exit_code': result.returncode, 'duration_seconds': round(time.monotonic() - started, 2),
                'failure_output_redacted': redact(detail) if detail else ''}
    except subprocess.TimeoutExpired:
        return {'name': name, 'command': command, 'status': 'FAIL', 'exit_code': None,
                'duration_seconds': round(time.monotonic() - started, 2), 'reason': f'Timed out after {timeout}s'}
    except OSError as exc:
        return {'name': name, 'command': command, 'status': 'NOT_RUN', 'exit_code': None,
                'duration_seconds': round(time.monotonic() - started, 2), 'reason': str(exc)}


def redact(text: str) -> str:
    text = re.sub(r'(?i)(bearer\s+)[^\s]+', r'\1[REDACTED]', text)
    text = re.sub(r'(?i)((?:password|api[_-]?key|secret|token)\s*[=:]\s*)[^\s]+', r'\1[REDACTED]', text)
    return text


def not_run(name: str, command: list[str], reason: str) -> dict:
    return {'name': name, 'command': command, 'status': 'NOT_RUN', 'exit_code': None,
            'duration_seconds': 0, 'reason': reason}


def verify_repo(root: Path, profile: str = 'all', timeout: int = 180) -> dict:
    root = root.resolve()
    checks = []
    python = sys.executable
    for name, script in (
        ('AI readiness', 'scripts/ai_check.py'),
        ('AI negative tests', 'scripts/test_ai_check.py'),
        ('Task protocol negative tests', 'scripts/test_task_protocol.py'),
        ('ABA governance', 'scripts/governance.py'),
        ('Governance negative tests', 'scripts/test_governance.py'),
        ('Static C# scan (NOT compilation)', 'scripts/static_review.py'),
    ):
        command = [python, script]
        if script == 'scripts/ai_check.py':
            command.append('check')
        if not (root / script).is_file():
            checks.append(not_run(name, command, f'Missing {script}'))
        else:
            checks.append(check_command(root, name, command, timeout))
    if profile == 'all':
        stages = [('Dotnet restore', ['dotnet', 'restore', 'ABA.Net10.Phase1.slnx']),
                  ('Dotnet build', ['dotnet', 'build', 'ABA.Net10.Phase1.slnx', '-c', 'Release', '--no-restore']),
                  ('Academy runtime smoke', ['dotnet', 'run', '--project', 'tests/Aba.SmokeTests', '-c', 'Release', '--no-build'])]
        dotnet = shutil.which('dotnet')
        if not dotnet:
            for name, command in stages:
                checks.append(not_run(name, command, '.NET SDK unavailable in this environment'))
        else:
            sdk = check_command(root, 'Dotnet SDK version', ['dotnet', '--version'], timeout)
            checks.append(sdk)
            if sdk['status'] != 'PASS':
                for name, command in stages:
                    checks.append(not_run(name, command, 'Could not determine .NET SDK version'))
            else:
                version = subprocess.run(['dotnet', '--version'], cwd=root, capture_output=True, text=True,
                                         errors='replace', timeout=timeout).stdout.strip()
                if not version.startswith('10.'):
                    for name, command in stages:
                        checks.append(not_run(name, command, f'Requires pinned .NET 10 SDK; observed {redact(version)}'))
                else:
                    dependency_ok = True
                    for name, command in stages:
                        if not dependency_ok:
                            checks.append(not_run(name, command, 'Prerequisite restore/build failed'))
                            continue
                        outcome = check_command(root, name, command, max(timeout, 240))
                        checks.append(outcome)
                        dependency_ok = outcome['status'] == 'PASS'
    counts = {status: sum(c['status'] == status for c in checks) for status in ('PASS', 'FAIL', 'NOT_RUN')}
    return {'schema_version': 1, 'generated_utc': datetime.now(timezone.utc).isoformat(),
            'profile': profile, 'repository': root.name, 'checks': checks, 'summary': counts,
            'claim_limit': 'Static PASS is not a .NET compile/runtime result. NOT_RUN is not PASS.'}


def format_verification(report: dict) -> str:
    lines = [f"# ABA verification evidence ({report['profile']})", '',
             f"Generated UTC: {report['generated_utc']}", '',
             '| Check | Result | Command | Detail |', '| --- | --- | --- | --- |']
    for c in report['checks']:
        detail = c.get('reason') or (f"exit {c['exit_code']}" if c['status'] == 'FAIL' else 'executed')
        detail = detail.replace('|', '/')
        command = ' '.join(c['command']).replace('|', '/')
        lines.append(f"| {c['name']} | {c['status']} | `{command}` | {detail} |")
    lines.extend(['', '## Interpretation',
                  'Successful static checks do not establish C# compilation, .NET runtime correctness, real Redis/PostgreSQL/Wolverine integration, or production readiness.',
                  'Missing/unexecuted tests are NOT_RUN and remain explicit blockers.', ''])
    return '\n'.join(lines)


def write_output(root: Path, path_str: str, content: str) -> Path:
    path = (root / path_str).resolve()
    if not path.is_relative_to(root.resolve()) or path.suffix not in ('.json', '.md') or path.relative_to(root.resolve()).parts[:2] != ('artifacts', 'verification'):
        raise ValueError('Evidence output must be .json/.md under artifacts/verification/ in the repository')
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding='utf-8')
    return path


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd(), help='ABA repository root')
    sub = parser.add_subparsers(dest='action', required=True)
    plan = sub.add_parser('plan', help='Heuristic task ownership and test matrix, not authority')
    plan.add_argument('--task', required=True)
    plan.add_argument('--file', action='append', default=[])
    plan.add_argument('--json', action='store_true', help='Emit machine-readable plan')
    verify = sub.add_parser('verify', help='Run fixed QA commands and record real outcomes')
    verify.add_argument('--profile', choices=('static', 'all'), default='all')
    verify.add_argument('--timeout', type=int, default=180)
    verify.add_argument('--output', help='Write JSON evidence under the repository')
    verify.add_argument('--markdown', help='Write Markdown summary under the repository')
    args = parser.parse_args(argv)
    try:
        if args.action == 'plan':
            result = classify_task(args.root, args.task, args.file)
            print(json.dumps(result, ensure_ascii=False, indent=2) if args.json else format_plan(result))
            return 0
        if args.timeout < 1:
            raise ValueError('timeout must be positive')
        result = verify_repo(args.root, args.profile, args.timeout)
        if args.output:
            print('Evidence JSON: ' + str(write_output(args.root, args.output, json.dumps(result, ensure_ascii=False, indent=2) + '\n')))
        if args.markdown:
            print('Evidence Markdown: ' + str(write_output(args.root, args.markdown, format_verification(result))))
        print(format_verification(result))
        return 1 if result['summary']['FAIL'] else 2 if result['summary']['NOT_RUN'] else 0
    except (ValueError, OSError, KeyError, json.JSONDecodeError) as exc:
        print(f'ABA task protocol error: {exc}', file=sys.stderr)
        return 3


if __name__ == '__main__':
    raise SystemExit(main())

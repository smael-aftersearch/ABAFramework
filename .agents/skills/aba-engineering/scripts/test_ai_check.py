#!/usr/bin/env python3
"""Prove that ABA agent readiness rules reject deliberately broken documentation."""
import argparse
import shutil
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ai_check

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', type=Path, default=Path.cwd())
ROOT = parser.parse_args().root.resolve()
errors = ai_check.validate_repo(ROOT)
assert not errors, 'Clean repository failed AI readiness: ' + '\n'.join(errors)
print('PASS: clean repository agent-readiness check')

brief = ai_check.task_brief(ROOT, 'Add refund event API for Payments', ['samples/Academy/Payments/Domain/Payment.cs'])
assert 'Payments' in brief and 'contract-catalog.yaml' in brief and 'openapi-v1.json' in brief, brief
print('PASS: task brief selects matching context and sources')

brief_tests = ai_check.task_brief(ROOT, 'تست واحد و integration test برای پرداخت', [])
assert 'docs/testing/TEST-STRATEGY.md' in brief_tests and 'tests/README.md' in brief_tests and 'testing' in brief_tests, brief_tests
print('PASS: testing strategy selected for test-related tasks')
assert 'Documentation to review' in brief and 'samples/Academy/Payments/_Docs/ownership.yaml' in brief, brief
assert 'contract-catalog.yaml' in ai_check.task_brief(ROOT, 'change invoice contract', ['samples/Academy/Payments.Contracts/PaymentContracts.cs'])
print('PASS: change-path documentation impact hints')

unknown = ai_check.task_brief(ROOT, 'rename an unrelated local variable', [])
assert 'UNRESOLVED' in unknown, unknown
print('PASS: task brief does not invent a business owner')

with tempfile.TemporaryDirectory(prefix='aba-ai-readiness-') as folder:
    repo = Path(folder) / 'repo'
    shutil.copytree(ROOT, repo, ignore=shutil.ignore_patterns('bin', 'obj', '.git', 'local-data', '__pycache__'))

    target = repo / 'docs/ai/knowledge-map.json'
    original = target.read_text(encoding='utf-8')
    target.write_text(original.replace('"AGENTS.md",', '"missing/UNOWNED.md", "AGENTS.md",', 1), encoding='utf-8')
    errors = ai_check.validate_repo(repo)
    assert any('AI008' in error and 'UNOWNED.md' in error for error in errors), errors
    target.write_text(original, encoding='utf-8')
    print('PASS: missing indexed documentation rejected')

    policy = repo / 'docs/testing/TEST-STRATEGY.md'
    policy.unlink()
    errors = ai_check.validate_repo(repo)
    assert any('AI001' in error or 'AI008' in error for error in errors), errors
    policy.write_text((ROOT / 'docs/testing/TEST-STRATEGY.md').read_text(encoding='utf-8'), encoding='utf-8')
    print('PASS: missing mandatory test strategy rejected')

    skill_path = repo / '.agents/skills/aba-engineering/SKILL.md'
    original_skill = skill_path.read_text(encoding='utf-8')
    skill_path.write_text(original_skill.replace('name: aba-engineering', 'name: wrong-skill', 1), encoding='utf-8')
    errors = ai_check.validate_repo(repo)
    assert any('AI002' in error for error in errors), errors
    skill_path.write_text(original_skill, encoding='utf-8')
    print('PASS: incorrect Skill frontmatter rejected')

    runner = repo / 'scripts/task_protocol.py'
    runner.unlink()
    errors = ai_check.validate_repo(repo)
    assert any('AI001' in error and 'task_protocol.py' in error for error in errors), errors
    runner.write_text((ROOT / 'scripts/task_protocol.py').read_text(encoding='utf-8'), encoding='utf-8')
    print('PASS: missing mandatory task-verification entrypoint rejected')

    workflow = repo / '.github/workflows/verify.yml'
    original_workflow = workflow.read_text(encoding='utf-8')
    workflow.write_text(original_workflow.replace('scripts/task_protocol.py', 'scripts/inert_placeholder.py'), encoding='utf-8')
    errors = ai_check.validate_repo(repo)
    assert any('AI009' in error and 'task_protocol.py' in error for error in errors), errors
    workflow.write_text(original_workflow, encoding='utf-8')
    print('PASS: CI bypass of QA evidence gate rejected')

    path = repo / 'samples/Academy/Payments/_Docs/ownership.yaml'
    path.unlink()
    errors = ai_check.validate_repo(repo)
    assert any('AI006' in error and 'Payments' in error for error in errors), errors
    print('PASS: missing owner metadata rejected')

print('ALL AGENT DOCUMENTATION NEGATIVE TESTS PASSED')

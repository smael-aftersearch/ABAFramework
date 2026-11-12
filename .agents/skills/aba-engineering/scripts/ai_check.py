#!/usr/bin/env python3
"""ABA AI-documentation readiness check and task-specific reading guide.

Standard-library only. This is a static safety net, not a language-model
semantic validator or proof that a future AI will follow instructions.
"""
import argparse
import json
import re
import sys
from pathlib import Path

SKILL = '.agents/skills/aba-engineering/SKILL.md'
MAP = 'docs/ai/knowledge-map.json'
REQUIRED = (
    'AGENTS.md', SKILL, 'docs/ai/INDEX.md', 'docs/ai/CHANGE-PROTOCOL.md',
    'docs/ai/DOC-UPDATE-RULES.md', 'docs/testing/TEST-STRATEGY.md', 'tests/README.md', MAP, 'CLAUDE.md', 'GEMINI.md',
    '.github/copilot-instructions.md', '.github/pull_request_template.md',
    'docs/reference/ABA_Adaptive_Business_Architecture_English_Edition.pdf',
    'scripts/governance.py', 'contract-catalog.yaml',
    'scripts/task_protocol.py', 'scripts/test_task_protocol.py',
)
REFERENCE_FILES = (
    'references/aba-rules.md', 'references/task-playbooks.md',
    'references/evidence-and-drift.md', 'references/testing-and-evidence.md',
)
CONTEXT_FILES = ('ownership.yaml', 'context-map.yaml', 'ai-rules.yaml', 'decisions.md', 'evolution.md')


def json_file(path: Path):
    return json.loads(path.read_text(encoding='utf-8'))


def is_within(root: Path, path: Path) -> bool:
    return path.resolve().is_relative_to(root.resolve())


def validate_repo(root: Path) -> list[str]:
    root = root.resolve()
    errors = []

    def error(code, detail, fix):
        errors.append(f'{code}: {detail}\n  Fix: {fix}')

    for required in REQUIRED:
        if not (root / required).is_file():
            error('AI001', f'Missing required agent source {required}',
                  'Restore the file or explicitly update the knowledge contract.')

    skill_path = root / SKILL
    if skill_path.is_file():
        skill = skill_path.read_text(encoding='utf-8')
        header = re.match(r'\A---\n(.*?)\n---\n', skill, re.S)
        if not header or not re.search(r'^name:\s*aba-engineering\s*$', header.group(1), re.M) or not re.search(r'^description:\s*\S', header.group(1), re.M):
            error('AI002', 'Skill frontmatter missing name/description.',
                  'Use valid YAML frontmatter with aba-engineering and precise trigger description.')
        for referenced in REFERENCE_FILES:
            resource = skill_path.parent / referenced
            if not resource.is_file() or referenced not in skill:
                error('AI003', f'Missing/unlinked skill reference {referenced}',
                      'Include the resource and link to it directly from SKILL.md.')
        meta = skill_path.parent / 'agents/openai.yaml'
        if not meta.is_file() or 'display_name:' not in meta.read_text(encoding='utf-8'):
            error('AI003', 'Skill agents/openai.yaml missing UI name.',
                  'Supply interface.display_name.')
        if 'TODO' in skill:
            error('AI002', 'Skill entrypoint includes placeholder TODO text.',
                  'Finish the skill instructions.')

    for anchor, needle in [('AGENTS.md', 'docs/ai/INDEX.md'),
                           ('AGENTS.md', SKILL),
                           ('.github/copilot-instructions.md', 'AGENTS.md'),
                           ('docs/ai/INDEX.md', MAP)]:
        path = root / anchor
        if path.is_file() and needle not in path.read_text(encoding='utf-8'):
            error('AI004', f'{anchor} fails to route agents to {needle}',
                  'Add a concise link; do not copy large documents into the agent entrypoint.')

    map_path = root / MAP
    if not map_path.is_file():
        return errors
    try:
        knowledge = json_file(map_path)
    except (ValueError, OSError) as exc:
        error('AI005', f'Malformed knowledge map: {exc}', 'Restore JSON schema_version 1.')
        return errors

    if knowledge.get('schema_version') != 1:
        error('AI005', 'Unsupported schema_version.', 'Use schema_version 1.')
    seen = set()
    for name, paths in [('always', knowledge.get('always'))]:
        if not isinstance(paths, list) or not paths:
            error('AI005', f'Knowledge map {name} must be a nonempty path list.',
                  'Provide existing repository-relative paths.')
            continue
        for item in paths:
            if isinstance(item, str):
                seen.add(item)
            else:
                error('AI008', f'Invalid {name} document path {item!r}',
                      'Use a repository-relative string path.')

    for section in ('contexts', 'topics'):
        entries = knowledge.get(section)
        if not isinstance(entries, dict) or not entries:
            error('AI005', f'Knowledge map has no {section} entries.',
                  'Declare discoverable contexts/topics with keywords and docs.')
            continue
        for key, entry in entries.items():
            if not isinstance(entry, dict):
                error('AI005', f'{section}.{key} must be an object.', 'Use keywords and docs arrays.')
                continue
            keywords, docs = entry.get('keywords'), entry.get('docs')
            if not isinstance(keywords, list) or not keywords or any(not isinstance(w, str) or not w.strip() for w in keywords):
                error('AI005', f'{section}.{key} has no valid keywords.', 'Include searchable task terms.')
            if not isinstance(docs, list) or not docs:
                error('AI005', f'{section}.{key} has no referenced docs.', 'List source paths.')
                continue
            for item in docs:
                if isinstance(item, str):
                    seen.add(item)
                else:
                    error('AI008', f'{section}.{key} contains an invalid document path {item!r}',
                          'Use a repository-relative string path.')
            if section == 'contexts':
                expected_dir = root / 'samples' / 'Academy' / key / '_Docs'
                for filename in CONTEXT_FILES:
                    rel = f'samples/Academy/{key}/_Docs/{filename}'
                    if rel not in docs:
                        error('AI006', f'{key} knowledge map missing {rel}',
                              'List all context documents so an agent can discover them.')
                    if not (expected_dir / filename).is_file():
                        error('AI006', f'{key} source document absent: {rel}',
                              'Restore the owning context metadata/document.')
                for filename in ('ownership.yaml', 'context-map.yaml', 'ai-rules.yaml'):
                    path = expected_dir / filename
                    if path.is_file():
                        try:
                            record = json_file(path)
                        except (OSError, ValueError) as exc:
                            error('AI007', f'Invalid JSON-subset YAML at {path}: {exc}',
                                  'Correct the machine-readable metadata.')
                            continue
                        if filename != 'ai-rules.yaml' and record.get('context') != key:
                            error('AI007', f'{path.relative_to(root)} context name does not match {key}',
                                  'Set the owner to the actual context.')
                        if filename == 'ai-rules.yaml':
                            must_read = record.get('must_read')
                            if not isinstance(must_read, list) or any(x not in CONTEXT_FILES for x in must_read):
                                error('AI007', f'{key} ai-rules must_read references unknown metadata.',
                                      'Use recognized context document names.')

    for item in sorted(seen):
        if not isinstance(item, str) or not item.strip():
            error('AI008', f'Invalid knowledge-map path {item!r}', 'Use nonblank relative paths.')
            continue
        path = root / item
        if Path(item).is_absolute() or not is_within(root, path):
            error('AI008', f'Outside-repository knowledge path: {item}',
                  'Use only safe, repository-relative paths.')
        elif not path.is_file():
            error('AI008', f'Knowledge map points to missing file {item}',
                  'Fix the path or restore the source document.')

    if 'Identity' not in knowledge.get('contexts', {}) or 'Payments' not in knowledge.get('contexts', {}):
        error('AI005', 'Basic Academy context discovery is incomplete.',
              'Index the implemented business contexts.')
    for checkfile in ('scripts/verify-all.sh', '.github/workflows/verify.yml'):
        path = root / checkfile
        if not path.is_file():
            error('AI009', f'Missing required verification entrypoint: {checkfile}',
                  'Restore the CI/local verification workflow.')
            continue
        content = path.read_text(encoding='utf-8')
        for fragment in ('scripts/task_protocol.py', 'scripts/test_task_protocol.py'):
            if fragment not in content:
                error('AI009', f'{checkfile} does not run {fragment}',
                      'Wire the QA evidence runner and its negative tests into local checks and CI.')
    runner_path = root / SKILL.replace('SKILL.md', 'scripts/task_protocol.py')
    if runner_path.is_file():
        body = runner_path.read_text(encoding='utf-8')
        for fragment in ('scripts/ai_check.py', 'scripts/governance.py',
                         'scripts/test_governance.py', 'scripts/test_ai_check.py',
                         'scripts/test_task_protocol.py', 'scripts/static_review.py'):
            if fragment not in body:
                error('AI009', f'QA evidence runner does not invoke {fragment}',
                      'Include actual executable checks rather than a misleading PASS placeholder.')
    return errors


def term_matches(needle: str, haystack: str) -> bool:
    needle, haystack = needle.casefold().strip(), haystack.casefold()
    if not needle:
        return False
    if needle.isascii():
        return bool(re.search(r'(?<![a-z])' + re.escape(needle) + r'(?:s|es)?(?![a-z])', haystack))
    return needle in haystack


def review_docs_for_changes(paths: list[str]) -> list[str]:
    """Conservative review hints; never claims documentation is automatically correct."""
    docs = []
    for raw in paths:
        path = raw.replace('\\', '/').removeprefix('./')
        match = re.match(r'samples/Academy/(Identity|Enrollment|Payments|Accounting|Reporting)(\.Contracts)?/(.+)', path)
        if match:
            ctx, is_contract, rest = match.groups()
            own = f'samples/Academy/{ctx}/_Docs/'
            if is_contract:
                docs += [own + 'ownership.yaml', own + 'context-map.yaml', 'contract-catalog.yaml',
                         'docs/ai/DOC-UPDATE-RULES.md']
            elif rest.endswith('.csproj'):
                docs += [own + 'context-map.yaml', own + 'decisions.md']
            elif rest.startswith('Domain/'):
                docs += [own + 'ownership.yaml', own + 'decisions.md']
            elif rest.startswith(('Application/', 'Infrastructure/')):
                docs += [own + 'context-map.yaml', own + 'evolution.md']
        elif path.startswith('samples/Academy/Host/'):
            docs += ['samples/Academy/Host/openapi-v1.json', 'docs/security.md', 'README.md']
        elif path.startswith('src/Framework/'):
            docs += ['docs/architecture.md', 'docs/KNOWN-LIMITATIONS.md', 'docs/decisions/']
        elif path.startswith(('.agents/skills/', 'docs/ai/', 'AGENTS.md')):
            docs += ['docs/ai/INDEX.md', 'docs/ai/knowledge-map.json', 'docs/ai/DOC-UPDATE-RULES.md']
    return list(dict.fromkeys(docs))


def task_brief(root: Path, task: str, files: list[str]) -> str:
    knowledge = json_file(root / MAP)
    haystack = task + ' ' + ' '.join(files)
    contexts = []
    topics = []
    selected = list(knowledge['always'])
    for context, entry in knowledge['contexts'].items():
        by_name = term_matches(context, haystack)
        by_keyword = any(term_matches(word, haystack) for word in entry['keywords'])
        by_file = any(f'/Academy/{context}/' in '/' + f.replace('\\', '/') or
                      f'/Academy/{context}.Contracts/' in '/' + f.replace('\\', '/') for f in files)
        if by_name or by_keyword or by_file:
            contexts.append(context)
            selected += entry['docs']
    for topic, entry in knowledge['topics'].items():
        if any(term_matches(word, haystack) for word in entry['keywords']):
            topics.append(topic)
            selected += entry['docs']
    unique = list(dict.fromkeys(selected))
    out = ['ABA TASK READING BRIEF (heuristic, not architectural authority)',
           f'Task: {task}',
           'Possible owning contexts: ' + (', '.join(contexts) if contexts else 'UNRESOLVED — inspect ownership.yaml and context maps'),
           'Relevant topics: ' + (', '.join(topics) if topics else '(none matched)'),
           'Recommended reading order:']
    out += [f'  - {item}' for item in unique]
    impact = review_docs_for_changes(files)
    if impact:
        out.append('Documentation to review **if behavior/ownership really changed** (not automatic edits):')
        out += [f'  - {item}' for item in impact]
    out += ['Confirm ownership in metadata before changing code.',
            'For material changes: define success/negative/invariant test cases first, implement + tests + docs together; run ai_check, governance, and actual .NET checks; report PASS/FAIL/NOT RUN.']
    return '\n'.join(out)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd(), help='Repository root (default: current working directory)')
    commands = parser.add_subparsers(dest='command', required=True)
    commands.add_parser('check', help='Validate skill and document discovery paths')
    brief = commands.add_parser('brief', help='Suggest task-relevant architecture sources')
    brief.add_argument('--task', required=True)
    brief.add_argument('--file', action='append', default=[], help='Affected file path (repeatable)')
    args = parser.parse_args(argv)
    root = args.root.resolve()
    if args.command == 'check':
        findings = validate_repo(root)
        if findings:
            print('\n\n'.join(findings), file=sys.stderr)
            print(f'FAIL: {len(findings)} AI readiness finding(s)', file=sys.stderr)
            return 1
        print('PASS: ABA agent entrypoints, skill, doc index, knowledge map, per-context metadata and CI wiring')
        return 0
    try:
        print(task_brief(root, args.task, args.file))
        return 0
    except (OSError, ValueError, KeyError) as exc:
        print(f'Unable to generate reading brief: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())

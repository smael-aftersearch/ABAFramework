#!/usr/bin/env python3
"""Validate and render the ABA implementation matrix from a reviewed JSON catalog.

Does NOT infer "implemented" from a filename: a maintainer must document actual
scope, test evidence and remaining release gates. CI checks rendered docs for drift.
"""
import argparse
import json
import re
import sys
from collections import Counter
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "docs/implementation-status.json"
README = ROOT / "README.md"
DETAILS = ROOT / "docs/IMPLEMENTATION-STATUS.md"
START = "<!-- ABA-STATUS:START -->"
END = "<!-- ABA-STATUS:END -->"
STATES = {"implemented": "Implemented", "partial": "Partial", "not_started": "Not implemented"}
PRIORITY = ("P0", "P1", "P2")
REQUIRED = ("id", "area", "feature", "status", "owner", "priority", "delivered",
            "remaining", "acceptance", "evidence", "tests")


def validate(data, root=ROOT):
    if data.get("schema_version") != 1:
        raise ValueError("Invalid implementation status schema version")
    run = data.get("baseline")
    if not isinstance(run, dict) or not str(run.get("ci_run", "")).isdigit() or not re.fullmatch(
            r"[a-f0-9]{40}", str(run.get("commit", ""))):
        raise ValueError("Provide explicit evidence-bearing baseline CI run and source commit")
    entries = data.get("features")
    if not isinstance(entries, list) or not entries:
        raise ValueError("Implementation matrix requires a nonempty features list")
    seen = set()
    for item in entries:
        if any(field not in item for field in REQUIRED):
            raise ValueError(f"Missing required implementation field for {item.get('id')}")
        id_ = item["id"]
        if not isinstance(id_, str) or not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", id_):
            raise ValueError(f"Invalid capability ID: {id_}")
        if id_ in seen:
            raise ValueError(f"Duplicate capability ID: {id_}")
        seen.add(id_)
        if item["status"] not in STATES or item["priority"] not in PRIORITY:
            raise ValueError(f"Invalid status or priority for {id_}")
        if any(not isinstance(item[k], str) or not item[k].strip()
               for k in ("area", "feature", "owner", "delivered", "remaining", "acceptance")):
            raise ValueError(f"Missing meaningful scope/owner/next step for {id_}")
        if item["status"] in ("partial", "not_started") and item["remaining"] in ("—", "-", "None"):
            raise ValueError(f"Unfinished capability must name a remaining task: {id_}")
        if item["status"] == "not_started" and item["delivered"] != "Not implemented":
            raise ValueError(f"Not-started capability must not claim delivered behavior: {id_}")
        for list_key in ("evidence", "tests"):
            paths = item[list_key]
            if not isinstance(paths, list) or len(paths) != len(set(paths)):
                raise ValueError(f"Invalid {list_key} list for {id_}")
            for p in paths:
                if not isinstance(p, str) or not p or PurePosixPath(p).is_absolute() or ".." in PurePosixPath(p).parts:
                    raise ValueError(f"Unsafe evidence path for {id_}: {p}")
                if not (root / p).is_file():
                    raise ValueError(f"Evidence file missing for {id_}: {p}")
        if item["status"] == "implemented" and (not item["evidence"] or not item["tests"]):
            raise ValueError(f"An implemented feature needs source AND executable test evidence: {id_}")
        if item["status"] == "not_started" and (item["evidence"] or item["tests"]):
            raise ValueError(f"Not-started feature must not claim delivered source/tests: {id_}")
    return entries


def cell(value):
    return str(value).replace("|", r"\|").replace("\n", " ").strip()


def render_section(data):
    entries = validate(data)
    counts = Counter(x["status"] for x in entries)
    baseline = data["baseline"]
    lines = [
        "The **generic .NET 10 API template** is the primary product. This matrix is separate from the",
        "historical Academy Phase 1 demo below. A feature can be implemented technically while",
        "its **production release gates remain open**. Evidence refers to the last reviewed baseline:",
        f"[CI run {baseline['ci_run']}](https://github.com/smael-aftersearch/ABAFramework/actions/runs/{baseline['ci_run']})",
        f"(commit `{baseline['commit'][:12]}`). New commits must pass their own CI before being treated as verified.\n",
        f"**Tracked: {len(entries)}** · Implemented: {counts['implemented']} · Partial: {counts['partial']} · Not implemented: {counts['not_started']}.\n",
        "| Area | Parameter / feature | Status | Delivered scope | Remaining task | Priority |",
        "| --- | --- | --- | --- | --- | --- |",
    ]
    for e in entries:
        label = STATES[e["status"]]
        lines.append("| " + " | ".join(cell(x) for x in (
            e["area"], e["feature"], f"**{label}**", e["delivered"],
            e["remaining"], e["priority"]
        )) + " |")
    lines.extend([
        "",
        "### Release-critical tasks still open (P0)",
        "",
        "| Task ID | Task / acceptance gate | Current state |",
        "| --- | --- | --- |",
    ])
    p0 = [x for x in entries if x["priority"] == "P0" and x["status"] != "implemented"]
    for e in p0:
        lines.append(f"| `{e['id']}` | {cell(e['remaining'])} — {cell(e['acceptance'])} | {STATES[e['status']]} |")
    lines.extend([
        "",
        "For exact code paths, tests and the P0/P1/P2 backlog, read",
        "[docs/IMPLEMENTATION-STATUS.md](docs/IMPLEMENTATION-STATUS.md).",
        "Statuses are **maintainer reviewed**, not inferred by the mere existence of a file.",
        "Run `python3 scripts/status_matrix.py --check` to fail CI on mismatched tables.",
    ])
    return "\n".join(lines)


def render_details(data):
    entries = validate(data)
    lines = [
        "# ABA API — implementation evidence and remaining work",
        "",
        "This file is generated from `docs/implementation-status.json`.",
        "The README matrix and this backlog must be updated together via",
        "`python3 scripts/status_matrix.py --write`, and independently verified by CI.",
        "Implemented = code plus focused tests and a reviewed baseline; Partial = real",
        "technical work exists but a stated acceptance gate is missing; Not implemented",
        "= no implementation in the domain-neutral template. Business examples in Academy",
        "do not count as generic framework features.",
        "",
        render_section(data),
        "",
        "## Execution backlog and exact acceptance checks",
        "",
        "| ID | Owner | Priority | Status | Required acceptance / completion evidence |",
        "| --- | --- | --- | --- | --- |",
    ]
    todo = [e for p in PRIORITY for e in entries if e["priority"] == p and e["status"] != "implemented"]
    for e in todo:
        lines.append("| " + " | ".join(cell(x) for x in (
            f"`{e['id']}`", e["owner"], e["priority"], STATES[e["status"]], e["acceptance"]
        )) + " |")
    lines.extend([
        "",
        "## Implemented or partial: checked-in source / executable tests",
        "",
        "| ID | Source files / docs | Executable tests / CI gates |",
        "| --- | --- | --- |",
    ])
    for e in entries:
        if e["status"] == "not_started":
            continue
        source = ", ".join(f"`{p}`" for p in e["evidence"]) or "No source proof"
        checks = ", ".join(f"`{p}`" for p in e["tests"]) or "NOT RUN / no complete test"
        lines.append(f"| `{e['id']}` | {source} | {checks} |")
    lines.extend([
        "",
        "## How to record progress without exaggeration",
        "",
        "1. Implement source in its ABA owner, and add success, negative and invariant tests.",
        "2. Run the relevant .NET 10 and real PostgreSQL/SQL Server/Redis/Wolverine",
        "   tests when the capability requires actual infrastructure.",
        "3. Change the feature's status only when the stated acceptance gate is met;",
        "   record exact source paths, tests, remaining gaps and the reviewed CI baseline.",
        "4. Run `python3 scripts/status_matrix.py --write`, then",
        "   `python3 scripts/test_status_matrix.py` and",
        "   `python3 scripts/status_matrix.py --check`. CI must pass.",
        "5. Archive historical evidence; do not erase Academy limitations or describe",
        "   a successful technical probe as production proof.",
        "",
    ])
    return "\n".join(lines)


def replace_readme(readme, section):
    if readme.count(START) != 1 or readme.count(END) != 1:
        raise ValueError("README must have exactly one ABA status marker pair")
    start = readme.index(START) + len(START)
    end = readme.index(END)
    if end < start:
        raise ValueError("Invalid status marker order")
    return readme[:start] + "\n\n" + section + "\n\n" + readme[end:]


def main():
    parser = argparse.ArgumentParser(description="Validate or regenerate the ABA implementation status matrix")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--write", action="store_true")
    mode.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        data = json.loads(CATALOG.read_text(encoding="utf-8"))
        section = render_section(data)
        doc = render_details(data)
        expected_readme = replace_readme(README.read_text(encoding="utf-8"), section)
        if args.write:
            README.write_text(expected_readme, encoding="utf-8")
            DETAILS.write_text(doc + "\n", encoding="utf-8")
            print("PASS: ABA README status table and detailed backlog updated")
            return 0
        errors = []
        if README.read_text(encoding="utf-8") != expected_readme:
            errors.append("README capability table is stale. Run python3 scripts/status_matrix.py --write")
        if not DETAILS.is_file() or DETAILS.read_text(encoding="utf-8") != doc + "\n":
            errors.append("docs/IMPLEMENTATION-STATUS.md is stale. Run --write")
        if errors:
            print("\n".join(errors), file=sys.stderr)
            return 1
        print(f"PASS: {len(data['features'])} tracked capabilities, source/test path evidence, both published tables in sync")
        return 0
    except (ValueError, OSError, KeyError, TypeError, json.JSONDecodeError) as error:
        print(f"ABA STATUS ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Portable ABA task planner and truthful verification protocol.

No LLM dependency, shell evaluation, generated business ownership, or
hidden test bypass. The generated repository is the source of truth.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
REQUIRED_DOCS = (
    "AGENTS.md",
    "docs/ai/INDEX.md",
    "docs/architecture.md",
    "docs/testing/TEST-STRATEGY.md",
    "metadata/contexts.json",
)


def catalog() -> list[dict]:
    for relative in REQUIRED_DOCS:
        if not (ROOT / relative).is_file():
            raise ValueError(f"Required ABA source-of-truth file missing: {relative}")
    value = json.loads((ROOT / "metadata/contexts.json").read_text(encoding="utf-8"))
    contexts = value.get("contexts")
    if not isinstance(contexts, list):
        raise ValueError("Context catalog must have a list of contexts")
    return contexts


def normalize_relative(value: str) -> str:
    path = PurePosixPath(value.replace("\\", "/"))
    if not value.strip() or path.is_absolute() or ".." in path.parts or path.parts[0] == ".":
        raise ValueError(f"Refusing unsafe path: {value}")
    return path.as_posix()


def plan(task: str, files: list[str]) -> dict:
    if not task.strip():
        raise ValueError("Task must not be empty")
    records = catalog()
    owners = {record["name"]: record for record in records}
    selected = [normalize_relative(file) for file in files]
    affected = set()
    requires_contracts = False
    needs_platform = False

    for file in selected:
        parts = PurePosixPath(file).parts
        if len(parts) >= 4 and parts[:2] == ("src", "Modules"):
            name = parts[2]
            if name not in owners:
                raise ValueError(
                    f"No ABA owner for {file}: declare context metadata first")
            affected.add(name)
            if "Contracts" in parts:
                requires_contracts = True
            if "Infrastructure" in parts:
                needs_platform = True
        elif "Platform" in file or any(
            x in file.casefold() for x in (
                "persistence", "caching", "wolverine", "messaging",
                "security", "observability"
            )
        ):
            needs_platform = True
            affected.add("Platform (technical only)")
        elif "/Api/" in file or file.endswith("Api/Program.cs"):
            affected.add("Host (composition only)")
        else:
            affected.add("Unclassified (manual ownership review)")

    docs = list(REQUIRED_DOCS[:4])
    docs.append("metadata/contexts.json")
    for owner in sorted(affected):
        if owner in owners:
            docs.extend([
                f"src/Modules/{owner}/_Docs/ownership.yaml",
                f"src/Modules/{owner}/_Docs/context-map.yaml",
                f"src/Modules/{owner}/_Docs/ai-rules.yaml",
                f"src/Modules/{owner}/_Docs/decisions.md",
                f"src/Modules/{owner}/_Docs/evolution.md",
            ])
    if needs_platform:
        docs.append("docs/platform-infrastructure.md")
    if requires_contracts or len(affected) > 1:
        docs.append("docs/decisions/")

    if not selected:
        status = "NEEDS_OWNER"
        owner_display = ["Undetermined — inspect requirements and context catalog"]
    elif "Unclassified (manual ownership review)" in affected:
        status = "NEEDS_OWNER"
        owner_display = sorted(affected)
    else:
        status = "READY_FOR_REVIEW"
        owner_display = sorted(affected)

    return {
        "task": task.strip(),
        "status": status,
        "owners": owner_display,
        "files": selected,
        "read_before_edit": list(dict.fromkeys(docs)),
        "acceptance": {
            "success": "A real owner-approved behavior and its successful case must be tested",
            "negative": "Invalid input, unauthorized access or infrastructure failure must be tested",
            "invariant": "Ownership, public V1 compatibility and data isolation must remain intact",
        },
        "required_checks": [
            "python3 tools/aba_task.py verify --level static",
            "python3 tools/aba_task.py verify --level build",
            "python3 tools/aba_task.py verify --level full (when external service adapters change)",
        ],
        "notes": [
            "Do not invent a business domain or assign ownership from task keywords alone.",
            "Only public versioned Contracts may cross bounded-context boundaries.",
            "A plan is reading/test guidance, not evidence a test passed.",
            "Report exact PASS, FAIL or NOT_RUN, and keep Skill and documentation aligned.",
        ],
    }


def redact_output(content: str) -> str:
    """Do not persist configured DB/Redis credentials in a verification artifact."""
    for name in (
        "ABA_TEST_POSTGRESQL", "ABA_TEST_SQLSERVER", "ABA_TEST_REDIS",
        "ConnectionStrings__AbaDatabase", "ConnectionStrings__AbaRedis",
        "Authentication__ClientSecret",
    ):
        secret = os.environ.get(name)
        if secret:
            content = content.replace(secret, "[REDACTED]")
    # Also mask common password segments when diagnostics render only fragments.
    import re
    content = re.sub(
        r"(?i)(password|pwd)\s*=\s*[^;\s]+",
        lambda match: match.group(1) + "=[REDACTED]",
        content,
    )
    return content[-3200:]


def run_step(command: list[str], title: str) -> dict:
    try:
        p = subprocess.run(
            command, cwd=ROOT, check=False,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, timeout=240)
        return {
            "title": title,
            "command": command,
            "status": "PASS" if p.returncode == 0 else "FAIL",
            "exit_code": p.returncode,
            "output_tail": redact_output(p.stdout),
        }
    except subprocess.TimeoutExpired as error:
        return {
            "title": title, "command": command,
            "status": "FAIL", "exit_code": None,
            "output_tail": f"Timed out after {error.timeout}s",
        }
    except OSError as error:
        return {
            "title": title, "command": command,
            "status": "NOT_RUN", "exit_code": None,
            "output_tail": str(error),
        }


def verify(level: str) -> dict:
    catalog()
    if level not in ("static", "build", "full"):
        raise ValueError("Unsupported verification level")
    evidence = []
    def execute(command: list[str], title: str) -> bool:
        step = run_step(command, title)
        evidence.append(step)
        return step["status"] == "PASS"

    if not execute([sys.executable, "tools/new_context.py", "check"],
                   "Generated context ownership and dependency governance"):
        return result(level, evidence)

    if level != "static":
        solution = list(ROOT.glob("*.slnx"))
        if len(solution) != 1:
            evidence.append({
                "title": "Solution discovery", "status": "FAIL", "exit_code": None,
                "command": [], "output_tail": "Expected exactly one root .slnx",
            })
            return result(level, evidence)
        if not shutil.which("dotnet"):
            evidence.append({
                "title": ".NET 10 SDK", "status": "NOT_RUN", "exit_code": None,
                "command": ["dotnet"], "output_tail": "dotnet is unavailable",
            })
            return result(level, evidence)

        s = solution[0].name
        commands = [
            (["dotnet", "restore", s], "Restore generated solution"),
            (["dotnet", "build", s, "-c", "Release", "--no-restore"],
             "Compile generated solution"),
            (["dotnet", "test", s, "-c", "Release", "--no-build"],
             "Run default xUnit tests"),
        ]
        for command, title in commands:
            if not execute(command, title):
                return result(level, evidence)

    if level == "full":
        required = ("ABA_TEST_POSTGRESQL", "ABA_TEST_SQLSERVER", "ABA_TEST_REDIS")
        missing = [key for key in required if not os.environ.get(key)]
        if missing:
            evidence.append({
                "title": "Real external infrastructure",
                "status": "NOT_RUN", "command": [], "exit_code": None,
                "output_tail": f"Missing connections: {', '.join(missing)}",
            })
            return result(level, evidence)
        projects = list((ROOT / "tests").glob(
            "*.Platform.IntegrationTests/*.Platform.IntegrationTests.csproj"))
        if len(projects) != 1:
            evidence.append({
                "title": "Integration project discovery",
                "status": "FAIL", "command": [], "exit_code": None,
                "output_tail": "Expected one separate infrastructure test project",
            })
            return result(level, evidence)
        proj = projects[0].relative_to(ROOT).as_posix()
        for command, title in (
            (["dotnet", "restore", proj], "Restore external integration project"),
            (["dotnet", "test", proj, "-c", "Release", "--filter", "Category!=RecoveryDrill&Category!=CrashDrill"],
             "Run real PostgreSQL, SQL Server, Redis and messaging tests"),
        ):
            if not execute(command, title):
                break
    return result(level, evidence)


def result(level: str, evidence: list[dict]) -> dict:
    statuses = {x["status"] for x in evidence}
    status = ("FAIL" if "FAIL" in statuses else
              "NOT_RUN" if "NOT_RUN" in statuses else "PASS")
    return {
        "level": level,
        "status": status,
        "generated_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "steps": evidence,
        "warning": (
            "This verifies only the selected level and does not certify "
            "production readiness or business behavior not represented by tests."
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Read-only ABA plan and executable verification evidence")
    sub = parser.add_subparsers(dest="command", required=True)
    plan_parser = sub.add_parser("plan")
    plan_parser.add_argument("--task", required=True)
    plan_parser.add_argument("--file", action="append", default=[])
    verify_parser = sub.add_parser("verify")
    verify_parser.add_argument(
        "--level", choices=("static", "build", "full"), default="static")
    for command in (plan_parser, verify_parser):
        command.add_argument("--output", help="Explicit JSON output destination")
    args = parser.parse_args()

    try:
        data = (plan(args.task, args.file) if args.command == "plan"
                else verify(args.level))
        payload = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
        if args.output:
            dest = Path(args.output).expanduser().resolve()
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(payload, encoding="utf-8")
        print(payload, end="")
        if args.command == "plan":
            return 0 if data["status"] == "READY_FOR_REVIEW" else 2
        return 0 if data["status"] == "PASS" else 3
    except (ValueError, KeyError, OSError, json.JSONDecodeError) as error:
        print(f"ABA TASK ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())

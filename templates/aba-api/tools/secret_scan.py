#!/usr/bin/env python3
"""Fail-closed, redacted Gitleaks scan for the repository or a generated API."""

import argparse
from pathlib import Path
import subprocess
import sys

# Official Gitleaks v8.30.1 multi-platform OCI manifest digest. A mutable tag is
# included only for readability; Docker resolves the immutable digest.
SCANNER_IMAGE = "ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f"
DOCKER_TIMEOUT_SECONDS = 300
DOCKER_PREFLIGHT_TIMEOUT_SECONDS = 15


class DockerUnavailableError(RuntimeError):
    """Docker CLI or daemon is unavailable, so no scan result exists."""


def docker_available() -> bool:
    """Check the local Docker daemon without exposing its diagnostics."""
    try:
        result = subprocess.run(
            ["docker", "info"],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=DOCKER_PREFLIGHT_TIMEOUT_SECONDS,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    return result.returncode == 0


def _container_command(root: Path, config_root: Path, operation: str) -> list[str]:
    root = root.resolve()
    config_root = config_root.resolve()
    if not root.is_dir():
        raise ValueError("Scan root must be an existing directory")
    if not (config_root / ".gitleaks.toml").is_file():
        raise ValueError("Scan configuration is missing")
    if not (config_root / ".gitleaksignore").is_file():
        raise ValueError("Reviewed ignore file is missing")

    args = [
        "docker", "run", "--rm", "--network", "none", "--read-only",
        "--tmpfs", "/tmp:rw,nosuid,noexec,size=32m",
        "--env", "GIT_CONFIG_COUNT=1",
        "--env", "GIT_CONFIG_KEY_0=safe.directory",
        "--env", "GIT_CONFIG_VALUE_0=/scan",
        "--mount", f"type=bind,source={root},target=/scan,readonly",
        "--mount", f"type=bind,source={config_root / '.gitleaks.toml'},target=/config/gitleaks.toml,readonly",
        "--mount", f"type=bind,source={config_root / '.gitleaksignore'},target=/config/.gitleaksignore,readonly",
        "--workdir", "/scan", SCANNER_IMAGE,
        operation,
        "--config=/config/gitleaks.toml",
        "--gitleaks-ignore-path=/config/.gitleaksignore",
        "--ignore-gitleaks-allow",
        "--no-banner",
        "--redact=100",
        "--log-level=error",
        "--exit-code=1",
    ]
    if operation == "git":
        args.append("--log-opts=--all")
    args.append("/scan")
    return args


def run_scan(root: Path, config_root: Path, operation: str) -> subprocess.CompletedProcess[str]:
    if operation not in {"dir", "git"}:
        raise ValueError("Unsupported scan operation")
    if not docker_available():
        raise DockerUnavailableError("Docker CLI or daemon is unavailable")
    return subprocess.run(
        _container_command(root, config_root, operation),
        check=False,
        capture_output=True,
        text=True,
        timeout=DOCKER_TIMEOUT_SECONDS,
    )


def scan_repository(root: Path) -> tuple[bool, str]:
    root = root.resolve()
    for operation, label in (("dir", "working tree"), ("git", "reachable history")):
        result = run_scan(root, root, operation)
        if result.returncode != 0:
            # Scanner output is deliberately never printed; even a future
            # scanner regression must not copy a discovered credential to CI.
            return False, label
    return True, "working tree and reachable history"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    args = parser.parse_args()
    try:
        passed, scanned = scan_repository(args.root)
    except DockerUnavailableError:
        print("NOT_RUN: Docker CLI or daemon is unavailable; secret scan was not executed.", file=sys.stderr)
        return 2
    except subprocess.TimeoutExpired:
        print("FAIL: the pinned secret scan timed out; scanner output was suppressed.", file=sys.stderr)
        return 1
    except (OSError, ValueError):
        print("FAIL: the pinned secret scanner could not complete; scanner output was suppressed.", file=sys.stderr)
        return 1

    if not passed:
        print(f"FAIL: secret candidate or scanner error in {scanned}; scanner output was suppressed.", file=sys.stderr)
        return 1
    print(f"PASS: pinned Gitleaks scan completed for {scanned}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Fail-closed policy for the domain-neutral template's immutable .NET OCI bases.

This verifies *reviewed references* in the Dockerfile, not image signatures,
registry publication, running-container authenticity, or patched CVEs.
"""
import argparse
import datetime as dt
import json
from pathlib import Path
import re
import sys

STAGES = {
    "build": "mcr.microsoft.com/dotnet/sdk:10.0-noble",
    "runtime": "mcr.microsoft.com/dotnet/aspnet:10.0-noble",
}
DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")
FROM = re.compile(r"^FROM\s+(\S+)\s+AS\s+(\S+)\s*$", re.I)


class OciLockError(ValueError):
    pass


def verify(root: Path) -> dict:
    root = root.resolve()
    try:
        lock = json.loads((root / "oci-base-lock.json").read_text(encoding="utf8"))
        dockerfile = (root / "Dockerfile").read_text(encoding="utf8")
    except (OSError, ValueError) as exc:
        raise OciLockError("OCI base lock and Dockerfile must be readable and valid") from exc
    if not isinstance(lock, dict) or lock.get("schema_version") != 1:
        raise OciLockError("Invalid OCI lock schema version")
    try:
        reviewed = dt.date.fromisoformat(lock["reviewed_date"])
    except (KeyError, TypeError, ValueError) as exc:
        raise OciLockError("Missing or malformed OCI security review date") from exc
    if reviewed > dt.datetime.now(dt.timezone.utc).date():
        raise OciLockError("OCI base review date cannot be in the future")
    images = lock.get("images")
    if not isinstance(images, dict) or set(images) != set(STAGES):
        raise OciLockError("Exactly the build and runtime OCI base images must be locked")
    for stage, trusted in STAGES.items():
        item = images[stage]
        if not isinstance(item, dict) or item.get("name") != trusted or not isinstance(item.get("digest"), str):
            raise OciLockError("OCI base image registry/tag is untrusted")
        if not DIGEST.fullmatch(item["digest"]):
            raise OciLockError("OCI base image SHA256 must be an immutable 64-character digest")

    found = {}
    for line in dockerfile.splitlines():
        if line.lstrip().startswith("#"):
            continue
        if not line.strip().upper().startswith("FROM"):
            continue
        match = FROM.fullmatch(line.strip())
        if match is None:
            raise OciLockError("Dockerfile FROM must explicitly name a locked stage")
        ref, stage = match.groups()
        if stage not in STAGES or stage in found:
            raise OciLockError("Unexpected or duplicate container build stage")
        if ref != images[stage]["name"] + "@" + images[stage]["digest"]:
            raise OciLockError("Dockerfile base does not match the reviewed immutable digest lock")
        found[stage] = ref
    if set(found) != set(STAGES):
        raise OciLockError("Missing build/runtime stage or additional FROM statement")
    if "USER $APP_UID" not in dockerfile or re.search(r"(?im)^\s*USER\s+root\s*$", dockerfile):
        raise OciLockError("Generated runtime image must remain explicitly nonroot")
    return found


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        bases = verify(args.root)
        for stage, name in bases.items():
            print(f"PASS: {stage} uses reviewed immutable {name}")
    except OciLockError as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)

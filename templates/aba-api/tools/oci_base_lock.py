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
    # Reviewed dates are civil dates; for reviewers east of UTC the local
    # date may already be tomorrow during a late-night UTC CI run.
    # Allow one calendar-day offset, but still reject fabricated future reviews.
    if reviewed > dt.datetime.now(dt.timezone.utc).date() + dt.timedelta(days=1):
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
    user_instructions = {}
    active_stage = None
    for line in dockerfile.splitlines():
        if line.lstrip().startswith("#"):
            continue
        stripped = line.strip()
        if not stripped:
            continue
        if stripped.upper().startswith("FROM"):
            match = FROM.fullmatch(stripped)
            if match is None:
                raise OciLockError("Dockerfile FROM must explicitly name a locked stage")
            ref, stage = match.groups()
            if stage not in STAGES or stage in found:
                raise OciLockError("Unexpected or duplicate container build stage")
            if ref != images[stage]["name"] + "@" + images[stage]["digest"]:
                raise OciLockError("Dockerfile base does not match the reviewed immutable digest lock")
            found[stage] = ref
            user_instructions[stage] = []
            active_stage = stage
            continue
        user = re.fullmatch(r"USER\s+(.+?)\s*", stripped, re.I)
        if user is not None:
            if active_stage is None:
                raise OciLockError("Dockerfile USER instruction must follow a locked stage")
            user_instructions[active_stage].append(user.group(1).strip())
    if set(found) != set(STAGES):
        raise OciLockError("Missing build/runtime stage or additional FROM statement")
    if (user_instructions.get("runtime") != ["$APP_UID"]
            or any(user_instructions[stage] for stage in user_instructions if stage != "runtime")):
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

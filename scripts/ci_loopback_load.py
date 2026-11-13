#!/usr/bin/env python3
"""Opt-in, CI-only, bounded loopback liveness baseline; never a production SLO."""
import argparse
import json
import os
import re
import statistics
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener

REQUESTS = 40
WORKERS = 4
TIMEOUT_SECONDS = 3


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def validate_target(url):
    try:
        parsed = urlsplit(url)
        port = parsed.port
    except ValueError as exc:
        raise ValueError("invalid loopback target") from exc
    if (parsed.scheme != "http" or parsed.hostname != "127.0.0.1"
            or not port or not (1024 <= port <= 65535)
            or parsed.path != "/health/live" or parsed.username is not None
            or parsed.password is not None or parsed.query or parsed.fragment):
        raise ValueError("only http://127.0.0.1:<high-port>/health/live is allowed")
    canonical = f"http://127.0.0.1:{port}/health/live"
    # urlsplit silently removes TAB/CR/LF and leading C0 whitespace. Refuse
    # any input that requires parser normalization before a network request.
    if url != canonical:
        raise ValueError("noncanonical loopback liveness target")
    return canonical


def validate_output(raw_path):
    """Prevent an opt-in CI drill from overwriting source, secrets or prior evidence."""
    path = Path(raw_path)
    if (path.is_absolute() or path.parts[:2] != ("artifacts", "load")
            or len(path.parts) != 3
            or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\.json", path.name)):
        raise ValueError("output must be artifacts/load/<safe-name>.json")
    root = Path(__file__).resolve().parents[1]
    artifacts = root / "artifacts"
    directory = artifacts / "load"
    if artifacts.is_symlink() or directory.is_symlink():
        raise ValueError("output directories must not be symlinks")
    output = directory / path.name
    if output.exists() or output.is_symlink():
        raise ValueError("existing evidence cannot be overwritten")
    return output


def write_evidence(output, evidence):
    output.parent.mkdir(parents=True, exist_ok=True)
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
    fd = os.open(output, flags, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as stream:
        json.dump(evidence, stream, sort_keys=True, indent=2)
        stream.write("\n")


def sample_once(url):
    opener = build_opener(ProxyHandler({}), NoRedirect())
    start = time.monotonic()
    try:
        with opener.open(Request(url, method="GET"), timeout=TIMEOUT_SECONDS) as response:
            ok = response.status == 200
            response.read(256)
    except Exception:  # HTTP errors, redirect attempts and socket failures count as failures.
        ok = False
    return ok, (time.monotonic() - start) * 1000


def probe(url, count=REQUESTS, workers=WORKERS):
    target = validate_target(url)
    if not (1 <= count <= REQUESTS and 1 <= workers <= WORKERS):
        raise ValueError("unbounded load settings refused")
    start = time.monotonic()
    with ThreadPoolExecutor(max_workers=workers) as pool:
        samples = list(pool.map(lambda _: sample_once(target), range(count)))
    elapsed = max(time.monotonic() - start, 0.000001)
    durations = sorted(duration for _, duration in samples)
    successes = sum(ok for ok, _ in samples)
    return {
        "kind": "disposable-loopback-liveness-only",
        "requested": count,
        "concurrency_cap": workers,
        "successes": successes,
        "failures": count - successes,
        "duration_seconds": round(elapsed, 4),
        "requests_per_second": round(count / elapsed, 2),
        "latency_ms_p50": round(statistics.median(durations), 2),
        "latency_ms_p95": round(durations[(95 * len(durations) + 99) // 100 - 1], 2),
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", required=True)
    parser.add_argument("--output", default="artifacts/load/loopback.json")
    args = parser.parse_args(argv)
    if os.environ.get("CI") != "true" or os.environ.get("ABA_ENABLE_LOOPBACK_LOAD_DRILL") != "yes":
        print("BLOCKED: loopback load needs explicit CI + opt-in", file=sys.stderr)
        return 3
    try:
        target = validate_target(args.url)
        output = validate_output(args.output)
        evidence = probe(target)
        write_evidence(output, evidence)
    except ValueError as exc:
        print(f"BLOCKED: {exc}", file=sys.stderr)
        return 3
    except OSError as exc:
        print(f"BLOCKED: unsafe or unavailable evidence output ({exc.strerror})", file=sys.stderr)
        return 3
    if evidence["failures"]:
        print(f"FAIL: {evidence['failures']} of {evidence['requested']} loopback requests failed", file=sys.stderr)
        return 1
    print("PASS: bounded CI loopback liveness baseline; not an SLO or multi-node load test")
    print(json.dumps(evidence, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())

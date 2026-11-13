#!/usr/bin/env python3
"""CI-only cross-process HTTP quota proof against loopback disposable Redis."""
import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from uuid import uuid4


def validate_ci_gate(env, connection, dll):
    if env.get("CI", "").lower() != "true" or env.get(
        "ABA_ENABLE_SHARED_RATE_LIMIT_PROCESS_DRILL"
    ) != "yes":
        raise ValueError("Separate-process probe requires explicit CI opt-in.")
    if connection.split(",", 1)[0].strip() not in ("localhost:6379", "127.0.0.1:6379"):
        raise ValueError("Only loopback CI Redis on port 6379 is allowed.")
    if not dll.is_file() or not dll.name.endswith(".Api.dll"):
        raise ValueError("A built generated .Api.dll fixture is required.")


def free_loopback_port():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def get_status(port, path="/api/v1/system/status", headers=None):
    request = Request(f"http://127.0.0.1:{port}{path}", headers=headers or {})
    try:
        with urlopen(request, timeout=4) as response:
            return response.status
    except HTTPError as error:
        return error.code


def launch(dll, connection, scope, directory, number):
    port = free_loopback_port()
    env = dict(os.environ)
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": f"http://127.0.0.1:{port}",
        "DOTNET_NOLOGO": "1",
        "Platform__RateLimit__PermitLimit": "3",
        "Platform__RateLimit__WindowSeconds": "300",
        "Platform__RateLimit__Shared__Enabled": "true",
        "Platform__RateLimit__Shared__Namespace": scope,
        "ConnectionStrings__AbaRedis": connection,
    })
    log = (directory / f"api-{number}.log").open("w", encoding="utf-8")
    try:
        process = subprocess.Popen(
            ["dotnet", str(dll.resolve())], cwd=dll.parent,
            env=env, stdout=log, stderr=subprocess.STDOUT,
        )
    finally:
        log.close()
    try:
        for _ in range(100):
            if process.poll() is not None:
                raise RuntimeError(
                    f"API process {number} exited on startup ({process.returncode}).")
            try:
                if get_status(port, "/health/live") == 200:
                    return process, port
            except (URLError, TimeoutError, ConnectionError):
                pass
            time.sleep(0.15)
        raise RuntimeError(f"API process {number} did not bind localhost in time.")
    except BaseException:
        stop(process)
        raise


def stop(process):
    if process.poll() is not None:
        return
    process.terminate()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=5)


def run(dll, connection):
    namespace = "ci-process-" + uuid4().hex[:16]
    processes = []
    with tempfile.TemporaryDirectory(prefix="aba-shared-quota-") as tmp:
        try:
            first, a = launch(dll, connection, namespace, Path(tmp), 1)
            processes.append(first)
            second, b = launch(dll, connection, namespace, Path(tmp), 2)
            processes.append(second)
            assert first.pid != second.pid, "Hosts must have different PIDs."
            assert get_status(a, "/health/ready") == 200
            assert get_status(b, "/health/ready") == 200

            # Happy: one shared Redis budget across two independent processes.
            assert get_status(a) == 200
            assert get_status(b) == 200

            # Negative: forged tenant/proxy headers cannot escape the bucket.
            def hostile_call(index):
                return get_status(
                    a if index % 2 else b,
                    headers={"X-Tenant-ID": f"forged-{index}",
                             "X-Forwarded-For": f"203.0.113.{index + 1}"},
                )

            with ThreadPoolExecutor(max_workers=10) as pool:
                result = Counter(pool.map(hostile_call, range(10)))
            assert result == {200: 1, 429: 9}, (
                f"Expected 1 permitted / 9 denied after two successes: {result}"
            )
            assert get_status(a, headers={"X-Tenant-ID": "another"}) == 429
            assert get_status(b, headers={"X-Forwarded-For": "198.51.100.99"}) == 429

            # Invariant: health exemption and application namespace isolation.
            for port in (a, b):
                assert get_status(port, "/health/live") == 200
                assert get_status(port, "/health/ready") == 200
            third, c = launch(
                dll, connection, "ci-isolated-" + uuid4().hex[:16],
                Path(tmp), 3,
            )
            processes.append(third)
            assert third.pid not in (first.pid, second.pid)
            assert get_status(c) == 200
            assert get_status(a) == 429
            print("PASS: separate OS-process Redis quota 3/12, forged tenant/peer "
                  "bypass denied, health and namespace invariant.")
        finally:
            for process in reversed(processes):
                stop(process)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=Path, required=True)
    args = parser.parse_args()
    try:
        connection = os.environ.get("ABA_TEST_REDIS", "")
        validate_ci_gate(os.environ, connection, args.dll)
        if shutil.which("dotnet") is None:
            raise ValueError(".NET runtime is required.")
        run(args.dll, connection)
    except (ValueError, AssertionError, OSError, RuntimeError) as error:
        print(f"FAIL: cross-process shared Redis quota: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Fail-closed fixture authorization tests; no live Redis is touched."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("verify_shared_rate_limit_processes.py")
SPEC = importlib.util.spec_from_file_location("shared_process_probe", SCRIPT)
assert SPEC and SPEC.loader
probe = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(probe)


class ProbeBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.dll = Path(self.temp.name) / "Pilot.Backends.Api.dll"
        self.dll.write_bytes(b"synthetic file - never executed")
        self.gated = {
            "CI": "true",
            "ABA_ENABLE_SHARED_RATE_LIMIT_PROCESS_DRILL": "yes",
        }

    def test_explicit_ci_opt_in_accepts_loopback_only(self):
        probe.validate_ci_gate(self.gated, "localhost:6379,abortConnect=false", self.dll)
        probe.validate_ci_gate(self.gated, "127.0.0.1:6379", self.dll)

    def test_missing_ci_or_opt_in_fails_closed(self):
        for env in ({}, {"CI": "true"},
                    {"ABA_ENABLE_SHARED_RATE_LIMIT_PROCESS_DRILL": "yes"},
                    {**self.gated, "CI": "false"}):
            with self.subTest(env=env), self.assertRaises(ValueError):
                probe.validate_ci_gate(env, "localhost:6379", self.dll)

    def test_remote_redis_or_different_port_is_refused(self):
        for address in ("redis.internal:6379", "10.0.0.5:6379",
                        "localhost:6380", "", "localhost:6379.evil:6379"):
            with self.subTest(address=address), self.assertRaises(ValueError):
                probe.validate_ci_gate(self.gated, address, self.dll)

    def test_missing_or_unrelated_executable_is_refused(self):
        for dll in (Path(self.temp.name) / "Absent.Api.dll",
                    Path(self.temp.name) / "Pilot.Domain.dll"):
            with self.subTest(dll=str(dll)), self.assertRaises(ValueError):
                probe.validate_ci_gate(self.gated, "localhost:6379", dll)


if __name__ == "__main__":
    unittest.main(verbosity=2)

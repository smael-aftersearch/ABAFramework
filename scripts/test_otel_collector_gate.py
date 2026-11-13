#!/usr/bin/env python3
"""Negative and invariant tests for opt-in, CI-only Collector execution."""
import os
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts/verify-otel-collector.sh"
CONFIG = ROOT / "infra/otel-collector-ci.yaml"


class CollectorDrillGuardTests(unittest.TestCase):
    def invoke(self, ci, enabled):
        env = os.environ.copy()
        env.pop("CI", None)
        env.pop("ABA_ENABLE_OTEL_COLLECTOR_DRILL", None)
        if ci is not None:
            env["CI"] = ci
        if enabled is not None:
            env["ABA_ENABLE_OTEL_COLLECTOR_DRILL"] = enabled
        return subprocess.run(["bash", str(SCRIPT)], cwd=ROOT,
                              env=env, text=True, capture_output=True, timeout=10)

    def test_no_implicit_execution(self):
        result = self.invoke(None, None)
        self.assertEqual(3, result.returncode)
        self.assertIn("explicit CI + opt-in", result.stderr)

    def test_ci_alone_does_not_run(self):
        self.assertEqual(3, self.invoke("true", None).returncode)

    def test_optin_without_ci_does_not_run(self):
        self.assertEqual(3, self.invoke(None, "yes").returncode)

    def test_ci_guard_refuses_false_or_unrecognized_optin(self):
        self.assertEqual(3, self.invoke("false", "yes").returncode)
        self.assertEqual(3, self.invoke("true", "true").returncode)

    def test_collector_has_separate_trace_and_metric_pipelines(self):
        config = CONFIG.read_text(encoding="utf-8")
        self.assertIn("endpoint: 0.0.0.0:4317", config)
        self.assertIn("traces:", config)
        self.assertIn("metrics:", config)
        self.assertIn("verbosity: detailed", config)
        self.assertNotIn("password:", config)


if __name__ == "__main__":
    unittest.main(verbosity=2)

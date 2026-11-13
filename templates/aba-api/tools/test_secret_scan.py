#!/usr/bin/env python3
"""Positive, negative and redaction regression cases for the pinned scanner."""

from pathlib import Path
import io
import os
import random
import string
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from secret_scan import docker_available, main, run_scan


class SecretScannerTests(unittest.TestCase):
    def setUp(self):
        if not docker_available():
            if os.environ.get("CI", "").lower() == "true":
                self.fail("Docker CLI and daemon are required for secret scanner fixtures in CI")
            self.skipTest("NOT_RUN locally: Docker CLI or daemon is unavailable")

        self.project_root = Path(__file__).resolve().parents[1]
        self.fixture = tempfile.TemporaryDirectory(prefix="aba-secret-scan-")
        self.addCleanup(self.fixture.cleanup)
        self.fixture_root = Path(self.fixture.name)

    def test_clean_fixture_passes(self):
        (self.fixture_root / "safe.txt").write_text(
            "Synthetic configuration uses environment-provided values.\n",
            encoding="utf-8",
        )
        result = run_scan(self.fixture_root, self.project_root, "dir")
        self.assertEqual(result.returncode, 0, "clean fixture was rejected")

    def test_synthetic_credential_fails_and_is_redacted(self):
        randomizer = random.Random(0xABA2026)
        material = "".join(randomizer.choice(string.ascii_letters + string.digits) for _ in range(36))
        token = "ghp_" + material
        (self.fixture_root / "credential.txt").write_text(
            "github_token=" + token + "\n", encoding="utf-8"
        )

        result = run_scan(self.fixture_root, self.project_root, "dir")
        self.assertEqual(result.returncode, 1, "synthetic credential was not rejected")
        if token in result.stdout or token in result.stderr:
            raise AssertionError("scanner output exposed the synthetic credential")


class SecretScannerPreflightTests(unittest.TestCase):
    @mock.patch("secret_scan.subprocess.run")
    @mock.patch("secret_scan.sys.argv", ["secret_scan.py", "--root", "."])
    def test_unavailable_daemon_reports_not_run_without_diagnostics(self, run, _argv=None):
        run.return_value = subprocess.CompletedProcess(
            ["docker", "info"], 1, stdout="private daemon details", stderr="internal socket path"
        )
        diagnostic = io.StringIO()
        with mock.patch("secret_scan.sys.stderr", diagnostic):
            result = main()

        self.assertEqual(result, 2)
        self.assertIn("NOT_RUN", diagnostic.getvalue())
        self.assertNotIn("private daemon details", diagnostic.getvalue())
        self.assertNotIn("internal socket path", diagnostic.getvalue())

    @mock.patch("secret_scan.subprocess.run", side_effect=FileNotFoundError("docker"))
    @mock.patch("secret_scan.sys.argv", ["secret_scan.py", "--root", "."])
    def test_missing_cli_reports_not_run(self, _run, _argv=None):
        diagnostic = io.StringIO()
        with mock.patch("secret_scan.sys.stderr", diagnostic):
            result = main()

        self.assertEqual(result, 2)
        self.assertIn("NOT_RUN", diagnostic.getvalue())
        self.assertNotIn("FileNotFoundError", diagnostic.getvalue())
        self.assertNotIn("No such file", diagnostic.getvalue())


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Positive/negative lock validators for the generated OCI image bases."""
import json
from pathlib import Path
import tempfile
import unittest
from oci_base_lock import OciLockError, verify


class ReviewedOciBaseLockTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        original = Path(__file__).resolve().parents[1]
        self.dockerfile = self.root / "Dockerfile"
        self.lock = self.root / "oci-base-lock.json"
        self.dockerfile.write_text((original / "Dockerfile").read_text())
        self.lock.write_text((original / "oci-base-lock.json").read_text())

    def change_lock(self, mutate):
        data = json.loads(self.lock.read_text())
        mutate(data)
        self.lock.write_text(json.dumps(data))

    def test_current_reviewed_lock_is_consistent(self):
        bases = verify(self.root)
        self.assertEqual(set(bases), {"build", "runtime"})
        self.assertIn("@sha256:", bases["runtime"])

    def test_floating_runtime_tag_rejected(self):
        self.dockerfile.write_text(self.dockerfile.read_text().replace(
            "mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:",
            "mcr.microsoft.com/dotnet/aspnet:10.0-noble#sha256:", 1))
        with self.assertRaisesRegex(OciLockError, "does not match"):
            verify(self.root)

    def test_locked_digest_drift_rejected(self):
        self.change_lock(lambda d: d["images"]["runtime"].update(
            digest="sha256:" + "f" * 64))
        with self.assertRaisesRegex(OciLockError, "does not match"):
            verify(self.root)

    def test_unapproved_registry_rejected(self):
        self.change_lock(lambda d: d["images"]["build"].update(
            name="untrusted.example/dotnet/sdk:10.0-noble"))
        with self.assertRaisesRegex(OciLockError, "untrusted"):
            verify(self.root)

    def test_missing_or_short_digest_rejected(self):
        self.change_lock(lambda d: d["images"]["build"].update(digest="sha256:bad"))
        with self.assertRaisesRegex(OciLockError, "immutable"):
            verify(self.root)

    def test_unreviewed_extra_stage_rejected(self):
        self.dockerfile.write_text(self.dockerfile.read_text() +
                                   "\nFROM example.com/other:latest AS unsafe\n")
        with self.assertRaisesRegex(OciLockError, "Unexpected"):
            verify(self.root)

    def test_root_runtime_rejected(self):
        self.dockerfile.write_text(self.dockerfile.read_text().replace(
            "USER $APP_UID", "USER root"))
        with self.assertRaisesRegex(OciLockError, "nonroot"):
            verify(self.root)

    def test_artificial_future_review_is_rejected(self):
        self.change_lock(lambda d: d.update(reviewed_date="2099-01-01"))
        with self.assertRaisesRegex(OciLockError, "future"):
            verify(self.root)

    def test_missing_lock_or_dockerfile_fail_closed(self):
        self.lock.unlink()
        with self.assertRaisesRegex(OciLockError, "readable"):
            verify(self.root)


if __name__ == "__main__":
    unittest.main()

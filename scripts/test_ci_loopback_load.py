#!/usr/bin/env python3
"""Happy, negative and invariant probes without external traffic or infrastructure."""
import importlib.util
import os
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

SCRIPT = Path(__file__).with_name("ci_loopback_load.py")
spec = importlib.util.spec_from_file_location("ci_loopback_load", SCRIPT)
load = importlib.util.module_from_spec(spec)
spec.loader.exec_module(load)


class Handler(BaseHTTPRequestHandler):
    hits = 0
    active = 0
    peak = 0
    status = 200
    lock = threading.Lock()

    def do_GET(self):
        with self.lock:
            type(self).hits += 1
            type(self).active += 1
            type(self).peak = max(type(self).peak, type(self).active)
        try:
            time.sleep(0.012)
            self.send_response(self.status)
            if self.status == 302:
                self.send_header("Location", "https://example.com/escape")
            self.end_headers()
        finally:
            with self.lock:
                type(self).active -= 1

    def log_message(self, *args):
        pass


class LoopbackLoadTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.url = f"http://127.0.0.1:{cls.server.server_port}/health/live"

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=2)

    def setUp(self):
        with Handler.lock:
            Handler.hits = Handler.active = Handler.peak = 0
            Handler.status = 200

    def test_success_and_bounded_concurrency(self):
        result = load.probe(self.url, count=12, workers=3)
        self.assertEqual((12, 0), (result["successes"], result["failures"]))
        self.assertEqual(12, Handler.hits)
        self.assertLessEqual(Handler.peak, 3)
        self.assertGreaterEqual(result["latency_ms_p95"], result["latency_ms_p50"])
        self.assertGreater(result["requests_per_second"], 0)

    def test_rejects_remote_redirect_and_query_targets(self):
        for target in ("https://127.0.0.1:5193/health/live", "http://example.com/health/live",
                       "http://localhost:5193/health/live", self.url + "?token=x",
                       self.url.replace("/health/live", "/admin"),
                       "http://user:secret@127.0.0.1:5193/health/live"):
            with self.subTest(target=target), self.assertRaises(ValueError):
                load.probe(target, count=1)
        self.assertEqual(0, Handler.hits)

    def test_negative_status_and_redirect_are_not_success(self):
        for status in (503, 302):
            Handler.status = status
            evidence = load.probe(self.url, count=3, workers=2)
            self.assertEqual(3, evidence["failures"])
            self.assertEqual(0, evidence["successes"])

    def test_fail_closed_without_ci_and_explicit_optin(self):
        for ci, flag in ((None, None), ("true", None), (None, "yes"), ("false", "yes")):
            env = os.environ.copy()
            env.pop("CI", None)
            env.pop("ABA_ENABLE_LOOPBACK_LOAD_DRILL", None)
            if ci is not None:
                env["CI"] = ci
            if flag is not None:
                env["ABA_ENABLE_LOOPBACK_LOAD_DRILL"] = flag
            with tempfile.TemporaryDirectory() as temp:
                out = Path(temp) / "evidence.json"
                result = subprocess.run([sys.executable, str(SCRIPT), "--url", self.url,
                                         "--output", str(out)], env=env, capture_output=True,
                                        text=True, timeout=5)
                self.assertEqual(3, result.returncode)
                self.assertFalse(out.exists())
        self.assertEqual(0, Handler.hits)

    def test_unbounded_probe_settings_are_refused(self):
        with self.assertRaises(ValueError):
            load.probe(self.url, count=load.REQUESTS + 1)
        with self.assertRaises(ValueError):
            load.probe(self.url, workers=load.WORKERS + 1)
        self.assertEqual(0, Handler.hits)


if __name__ == "__main__":
    unittest.main(verbosity=2)

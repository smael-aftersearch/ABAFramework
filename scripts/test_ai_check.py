#!/usr/bin/env python3
"""Repository entrypoint for ABA agent documentation negative tests."""
import runpy
from pathlib import Path

if __name__ == '__main__':
    runpy.run_path(str(Path(__file__).resolve().parents[1] /
                       '.agents/skills/aba-engineering/scripts/test_ai_check.py'),
                   run_name='__main__')

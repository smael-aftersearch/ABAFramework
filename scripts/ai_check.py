#!/usr/bin/env python3
"""Repository entrypoint for the versioned ABA agent Skill checker."""
import runpy
from pathlib import Path

if __name__ == '__main__':
    runpy.run_path(str(Path(__file__).resolve().parents[1] /
                       '.agents/skills/aba-engineering/scripts/ai_check.py'),
                   run_name='__main__')

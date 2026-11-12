#!/usr/bin/env python3
"""Forward task planning and verification to the canonical embedded ABA Skill."""
from pathlib import Path
import runpy

if __name__ == '__main__':
    runpy.run_path(str(Path(__file__).resolve().parents[1] /
                       '.agents/skills/aba-engineering/scripts/task_protocol.py'),
                   run_name='__main__')

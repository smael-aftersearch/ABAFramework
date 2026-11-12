#!/usr/bin/env python3
"""Forward ABA task planning/verification negative tests to embedded Skill."""
from pathlib import Path
import runpy

if __name__ == '__main__':
    runpy.run_path(str(Path(__file__).resolve().parents[1] /
                       '.agents/skills/aba-engineering/scripts/test_task_protocol.py'),
                   run_name='__main__')

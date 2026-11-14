#!/usr/bin/env python3
"""Render the source-backed public C# declaration reference."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[3]))
from tools.source_reference.public_types import main

if __name__ == "__main__":
    raise SystemExit(main())

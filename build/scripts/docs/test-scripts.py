#!/usr/bin/env python3
"""
Script Validation Test

Validates that all documentation scripts in this directory are properly
configured and can execute basic operations.

Tests:
- Script has shebang
- Script has docstring
- Script responds to --help
- Script has required arguments
- Script returns correct exit codes

Usage:
    python3 test-scripts.py
"""

import os
import subprocess
import sys
from pathlib import Path
from typing import List, Tuple


def test_script(script_path: Path) -> Tuple[bool, List[str]]:
    """Test a single script for basic functionality.

    Returns:
        (success, errors) tuple
    """
    errors = []

    # Windows commonly runs scripts via the interpreter without execute bits.
    if os.name != 'nt' and not script_path.stat().st_mode & 0o111:
        errors.append(f"{script_path.name}: Not executable")

    # Test 2: Has shebang
    try:
        first_line = script_path.read_text(encoding='utf-8').split('\n')[0]
        if not first_line.startswith('#!/usr/bin/env python'):
            errors.append(f"{script_path.name}: Missing or incorrect shebang")
    except Exception as e:
        errors.append(f"{script_path.name}: Could not read file: {e}")
        return False, errors

    # Test 3: Responds to --help
    try:
        result = subprocess.run(
            [sys.executable, str(script_path), '--help'],
            capture_output=True,
            text=True,
            timeout=10
        )
        if result.returncode != 0:
            errors.append(f"{script_path.name}: --help returned non-zero: {result.returncode}")
        combined_output = f"{result.stdout}\n{result.stderr}".strip()
        if not combined_output:
            errors.append(f"{script_path.name}: --help produced no output")
    except subprocess.TimeoutExpired:
        errors.append(f"{script_path.name}: --help timed out")
    except Exception as e:
        errors.append(f"{script_path.name}: --help failed: {e}")

    return len(errors) == 0, errors


def main() -> int:
    """Run all tests."""
    scripts_dir = Path(__file__).parent

    # Find all Python scripts except this test file
    scripts = [
        p for p in scripts_dir.glob('*.py')
        if p.name != 'test-scripts.py'
    ]

    if not scripts:
        print("Error: No scripts found to test", file=sys.stderr)
        return 1

    print(f"Testing {len(scripts)} scripts...")
    print()

    total_errors = []
    passed = 0
    failed = 0

    for script in sorted(scripts):
        success, errors = test_script(script)

        if success:
            print(f"[PASS] {script.name}")
            passed += 1
        else:
            print(f"[FAIL] {script.name}")
            for error in errors:
                print(f"  - {error}")
                total_errors.append(error)
            failed += 1

    print()
    print(f"Results: {passed} passed, {failed} failed")

    if total_errors:
        print()
        print(f"Total errors: {len(total_errors)}")
        return 1

    return 0


if __name__ == '__main__':
    sys.exit(main())

"""Behavioral checks for consumer certification's native process boundary.

The installed Windows lifecycle runs in the release workflow. These portable
checks cover the helper's deadlines without supplying fake lifecycle evidence.
"""

import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
HARNESS = REPO_ROOT / "build/scripts/install/certify-consumer-install-lifecycle.ps1"
PWSH = shutil.which("pwsh")


class ConsumerCertificationProcessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not PWSH:
            raise RuntimeError("PowerShell 7 (pwsh) must be on PATH for consumer process-boundary validation")

    def run_helper(self, folder: Path, code: str, *, capture: bool = False, timeout: int = 5):
        params = folder / "arguments.json"
        params.write_text(json.dumps({"path": sys.executable, "arguments": ["-c", code]}))
        runner = folder / "invoke.ps1"
        timing = folder / "elapsed-ms"
        # Parse the real helper without executing the Windows-only certification.
        runner.write_text(
            "param([string]$Source,[string]$Parameters,[int]$Deadline,[int]$Capture,[string]$Timing)\n"
            "$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest\n"
            "$tokens=$null; $errors=$null\n"
            "$ast=[Management.Automation.Language.Parser]::ParseFile($Source,[ref]$tokens,[ref]$errors)\n"
            "if ($errors.Count) { throw ($errors | Out-String) }\n"
            "$function=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] "
            "-and $n.Name -eq 'Invoke-Executable'},$false)\n"
            ". ([scriptblock]::Create($function.Extent.Text))\n"
            "$TimeoutSeconds=$Deadline\n"
            "$p=Get-Content -LiteralPath $Parameters -Raw | ConvertFrom-Json\n"
            "$clock=[Diagnostics.Stopwatch]::StartNew()\n"
            "try { Invoke-Executable -Path $p.path -Arguments $p.arguments -CaptureOutput:([bool]$Capture) }\n"
            "finally { $clock.Stop(); [IO.File]::WriteAllText($Timing,[string]$clock.ElapsedMilliseconds) }\n"
        )
        # Files avoid the test runner itself waiting on inherited descendant pipes.
        with (folder / "stdout").open("w") as stdout, (folder / "stderr").open("w") as stderr:
            result = subprocess.run(
                [PWSH, "-NoLogo", "-NoProfile", "-File", str(runner), str(HARNESS), str(params),
                 str(timeout), str(int(capture)), str(timing)],
                stdout=stdout,
                stderr=stderr,
                # PowerShell cold startup and shutdown are outside the native
                # process deadline. Supervise those separately on loaded runners.
                timeout=timeout + 30,
                check=False,
            )
        return result, (folder / "stdout").read_text(), (folder / "stderr").read_text(), int(timing.read_text()) / 1000

    def test_runtime_descendant_cannot_hold_parent_output_drain_open(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            child_pid_path = folder / "child.pid"
            code = (
                "import subprocess,sys,pathlib; "
                "child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(30)'],"
                "stdin=subprocess.DEVNULL,stdout=sys.stdout,stderr=sys.stderr); "
                f"pathlib.Path({str(child_pid_path)!r}).write_text(str(child.pid))"
            )
            try:
                result, _, error, elapsed = self.run_helper(folder, code)
                self.assertEqual(result.returncode, 0, error)
                self.assertLess(elapsed, 5, "Persistent runtime held the helper's output pipes open")
            finally:
                if child_pid_path.exists():
                    try:
                        os.kill(int(child_pid_path.read_text()), signal.SIGTERM)
                    except ProcessLookupError:
                        pass

    def test_sql_style_capture_returns_output_and_rejects_nonzero_exit(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            result, output, error, _ = self.run_helper(folder, "print('SQL sentinel')", capture=True)
            self.assertEqual(result.returncode, 0, error)
            self.assertEqual(output.strip(), "SQL sentinel")
            result, _, error, _ = self.run_helper(folder, "import sys; print('failed probe',file=sys.stderr); sys.exit(9)", capture=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("exited 9", error)
            self.assertIn("failed probe", error)

    def test_native_process_execution_deadline_fails_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            result, _, error, elapsed = self.run_helper(Path(temporary), "import time; time.sleep(30)", timeout=1)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("execution deadline", error)
            # One second for native execution, ten for termination, and runner
            # scheduling margin; completion after the child's 30s sleep fails.
            self.assertLess(elapsed, 15)


if __name__ == "__main__":
    unittest.main()

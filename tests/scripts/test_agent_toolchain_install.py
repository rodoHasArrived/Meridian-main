"""Exercise the Linux agent bootstrap without package, network, or system writes."""

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
INSTALLER = REPO_ROOT / "build/scripts/install/install.sh"
ACTIONLINT_SHA256 = "8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8"


class AgentToolchainInstallTests(unittest.TestCase):
    def run_installer(
        self,
        *,
        node_version="24.5.0",
        supported_node="24",
        actionlint_version=None,
        checksum_fails=False,
        preflight_fails=False,
        node_candidate_missing=False,
    ):
        """Real shell orchestration, with every externally mutating tool replaced."""
        with tempfile.TemporaryDirectory() as temp:
            fixture = Path(temp)
            tools = fixture / "fake-bin"
            tools.mkdir()
            script = fixture / "build/scripts/install/install.sh"
            script.parent.mkdir(parents=True)
            shutil.copyfile(INSTALLER, script)
            requirements = fixture / "build/scripts/ci/requirements.txt"
            requirements.parent.mkdir(parents=True)
            requirements.write_text("# fixture requirements\n", encoding="utf-8")
            (fixture / ".nvmrc").write_text(supported_node + "\n", encoding="utf-8")
            (fixture / "node-version").write_text(node_version + "\n", encoding="utf-8")

            def tool(name, body):
                path = tools / name
                path.write_text("#!/bin/bash\nset -eu\n" + body + "\n", encoding="utf-8")
                path.chmod(0o755)

            tool("git", 'printf "%s\\n" "$FIXTURE_ROOT"')
            tool("id", 'printf "0\\n"')
            tool("uname", 'if [ "$1" = "-s" ]; then echo Linux; else echo x86_64; fi')
            tool("node", 'printf "v%s\\n" "$(cat "$FIXTURE_ROOT/node-version")"')
            tool("pwsh", 'echo "PowerShell 7.6.0"')
            tool(
                "dotnet",
                'case "$1" in\n'
                '  --version) echo 10.0.100 ;;\n'
                '  restore) echo restore >> "$FIXTURE_LOG" ;;\n'
                '  *) echo "Unexpected dotnet operation" >&2; exit 91 ;;\n'
                'esac',
            )
            tool(
                "python3",
                'case "$*" in\n'
                '  --version) echo "Python 3.12.9" ;;\n'
                '  -c*) echo 3.12 ;;\n'
                '  "-m pip "*) printf "pip:%s\\n" "$*" >> "$FIXTURE_LOG" ;;\n'
                '  "build/python/prerequisites.py --actionlint-version") echo 1.7.12 ;;\n'
                '  "build/python/prerequisites.py --profile full-quality-gate")\n'
                '    echo preflight >> "$FIXTURE_LOG"\n'
                '    if [ "$FIXTURE_PREFLIGHT_FAILS" = 1 ]; then exit 19; fi ;;\n'
                '  *) echo "Unexpected Python operation: $*" >&2; exit 92 ;;\n'
                'esac',
            )
            tool(
                "apt-get",
                'printf "apt:%s\\n" "$*" >> "$FIXTURE_LOG"\n'
                'if [[ "$*" == *nodejs* ]]; then\n'
                '  supported="$(tr -d "[:space:]" < "$FIXTURE_ROOT/.nvmrc")"\n'
                '  [ "$*" = "install -y --allow-downgrades nodejs=${supported}.19.0-1nodesource1" ] || exit 96\n'
                '  printf "%s.19.0\\n" "$supported" > "$FIXTURE_ROOT/node-version"\n'
                'fi',
            )
            tool(
                "apt-cache",
                'printf "apt-cache:%s\\n" "$*" >> "$FIXTURE_LOG"\n'
                '[ "$*" = "madison nodejs" ] || exit 97\n'
                'supported="$(tr -d "[:space:]" < "$FIXTURE_ROOT/.nvmrc")"\n'
                'echo "nodejs | 99.0.0-1nodesource1 | https://deb.nodesource.com/node_99.x nodistro/main amd64 Packages"\n'
                'printf "nodejs | %s0.1.0-1nodesource1 | https://deb.nodesource.com/node_%s0.x nodistro/main amd64 Packages\\n" "$supported" "$supported"\n'
                'if [ "$FIXTURE_NODE_CANDIDATE_MISSING" = 0 ]; then\n'
                '  printf "nodejs | %s.19.0-1nodesource1 | https://deb.nodesource.com/node_%s.x nodistro/main amd64 Packages\\n" "$supported" "$supported"\n'
                'fi\n'
                'echo "nodejs | 20.19.0-1ubuntu1 | http://archive.ubuntu.com/ubuntu noble/main amd64 Packages"',
            )
            tool(
                "curl",
                'printf "curl:%s\\n" "$*" >> "$FIXTURE_LOG"\n'
                'if [[ "$*" == *deb.nodesource.com* ]]; then\n'
                '  echo ": # inert NodeSource fixture"\n'
                'elif [[ "$*" == *github.com/rhysd/actionlint/* ]]; then\n'
                '  while [ "$#" -gt 0 ]; do\n'
                '    if [ "$1" = -o ]; then printf archive > "$2"; exit 0; fi\n'
                '    shift\n'
                '  done\n'
                '  exit 93\n'
                'else\n'
                '  echo "Unexpected download" >&2; exit 94\n'
                'fi',
            )
            tool(
                "sha256sum",
                'IFS= read -r checksum_line\n'
                'printf "checksum:%s\\n" "$checksum_line" >> "$FIXTURE_LOG"\n'
                'if [ "$FIXTURE_CHECKSUM_FAILS" = 1 ]; then exit 1; fi',
            )
            tool(
                "tar",
                'echo extract >> "$FIXTURE_LOG"\n'
                'while [ "$#" -gt 0 ]; do\n'
                '  if [ "$1" = -C ]; then\n'
                '    printf "#!/bin/bash\\necho 1.7.12\\n" > "$2/actionlint"\n'
                '    exit 0\n'
                '  fi\n'
                '  shift\n'
                'done\n'
                'exit 95',
            )
            tool(
                "install",
                'echo install-actionlint >> "$FIXTURE_LOG"\n'
                '[ "$1" = -m ] && [ "$2" = 755 ] && [ "$4" = /usr/local/bin/actionlint ]\n'
                'cp "$3" "$FIXTURE_BIN/actionlint"\n'
                'chmod 755 "$FIXTURE_BIN/actionlint"',
            )
            if actionlint_version is not None:
                tool("actionlint", 'printf "%s\\n" "$FIXTURE_ACTIONLINT_VERSION"')
            # Avoid inheriting any ambient actionlint when testing a missing binary.
            # The actual bootstrap only needs these ordinary POSIX utilities.
            for name in ("awk", "bash", "cat", "chmod", "cp", "cut", "head", "mkdir", "mktemp", "rm", "sed", "tr"):
                executable = shutil.which(name)
                self.assertIsNotNone(executable, name)
                (tools / name).symlink_to(executable)

            env = {
                **os.environ,
                "PATH": str(tools),
                "FIXTURE_ROOT": str(fixture),
                "FIXTURE_BIN": str(tools),
                "FIXTURE_LOG": str(fixture / "operations.log"),
                "FIXTURE_ACTIONLINT_VERSION": actionlint_version or "",
                "FIXTURE_CHECKSUM_FAILS": "1" if checksum_fails else "0",
                "FIXTURE_PREFLIGHT_FAILS": "1" if preflight_fails else "0",
                "FIXTURE_NODE_CANDIDATE_MISSING": "1" if node_candidate_missing else "0",
            }
            result = subprocess.run(
                [str(tools / "bash"), str(script), "--agent-tools"],
                cwd=fixture,
                env=env,
                capture_output=True,
                text=True,
                timeout=15,
            )
            log = fixture / "operations.log"
            return result, log.read_text(encoding="utf-8").splitlines() if log.exists() else []

    def test_older_and_newer_node_are_replaced_with_the_supported_major(self):
        for node_version in ("20.19.0", "25.0.0"):
            with self.subTest(node_version=node_version):
                result, operations = self.run_installer(node_version=node_version)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertTrue(any("deb.nodesource.com/setup_24.x" in operation for operation in operations))
                self.assertIn("apt-cache:madison nodejs", operations)
                self.assertIn("apt:install -y --allow-downgrades nodejs=24.19.0-1nodesource1", operations)
                self.assertIn("node:    v24.19.0", result.stdout)

    def test_node_installation_reads_the_repository_version_file(self):
        result, operations = self.run_installer(supported_node="26")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertTrue(any("deb.nodesource.com/setup_26.x" in operation for operation in operations))
        self.assertIn("apt:install -y --allow-downgrades nodejs=26.19.0-1nodesource1", operations)
        self.assertIn("node:    v26.19.0", result.stdout)

    def test_missing_supported_node_candidate_stops_before_install_or_completion(self):
        result, operations = self.run_installer(node_version="25.0.0", node_candidate_missing=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("apt-cache:madison nodejs", operations)
        self.assertIn("No Node.js 24 package is available", result.stderr)
        self.assertFalse(any(operation.startswith("apt:install -y --allow-downgrades nodejs=") for operation in operations))
        self.assertNotIn("preflight", operations)
        self.assertNotIn("restore", operations)
        self.assertNotIn("Agent toolchain setup complete.", result.stdout)

    def test_supported_node_and_pinned_actionlint_are_retained(self):
        result, operations = self.run_installer(actionlint_version="1.7.12")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertFalse(any(operation.startswith("curl:") for operation in operations))
        self.assertFalse(any(operation.startswith("apt:") for operation in operations))
        self.assertIn("pip:-m pip install -r build/scripts/ci/requirements.txt", operations)
        self.assertLess(operations.index("preflight"), operations.index("restore"))
        self.assertIn("Agent toolchain setup complete.", result.stdout)

    def test_missing_or_wrong_actionlint_is_verified_before_installation(self):
        for version in (None, "1.7.11", "1.8.0"):
            with self.subTest(actionlint_version=version):
                result, operations = self.run_installer(actionlint_version=version)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertTrue(any("/v1.7.12/actionlint_1.7.12_linux_amd64.tar.gz" in operation for operation in operations))
                checksums = [operation for operation in operations if operation.startswith("checksum:")]
                self.assertEqual(1, len(checksums))
                self.assertTrue(checksums[0].startswith("checksum:" + ACTIONLINT_SHA256 + "  "))
                self.assertLess(operations.index(checksums[0]), operations.index("extract"))
                self.assertLess(operations.index("extract"), operations.index("install-actionlint"))
                self.assertLess(operations.index("install-actionlint"), operations.index("preflight"))

    def test_failed_checksum_never_installs_or_reports_a_complete_toolchain(self):
        result, operations = self.run_installer(checksum_fails=True)
        self.assertNotEqual(0, result.returncode)
        self.assertTrue(any(operation.startswith("checksum:") for operation in operations))
        for forbidden in ("extract", "install-actionlint", "preflight", "restore"):
            self.assertNotIn(forbidden, operations)
        self.assertNotIn("Agent toolchain setup complete.", result.stdout)

    def test_failed_full_gate_preflight_prevents_restore_and_completion(self):
        result, operations = self.run_installer(preflight_fails=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("pip:-m pip install -r build/scripts/ci/requirements.txt", operations)
        self.assertIn("install-actionlint", operations)
        self.assertIn("preflight", operations)
        self.assertNotIn("restore", operations)
        self.assertNotIn("Agent toolchain setup complete.", result.stdout)


if __name__ == "__main__":
    unittest.main()

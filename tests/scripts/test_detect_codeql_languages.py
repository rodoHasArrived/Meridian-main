from __future__ import annotations

import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "build/scripts/ci/detect-codeql-languages.py"
spec = importlib.util.spec_from_file_location("detect_codeql_languages", SCRIPT)
detector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(detector)

CSHARP = "csharp"
JS = "javascript-typescript"


class CodeqlLanguageDetectionTests(unittest.TestCase):
    def test_documentation_requires_neither_language(self):
        paths = ["README.md", "docs/architecture/design.md", "docs/images/flow.svg"]
        self.assertFalse(detector.requires_analysis(CSHARP, paths))
        self.assertFalse(detector.requires_analysis(JS, paths))

    def test_empty_diff_requires_both_languages(self):
        self.assertTrue(detector.requires_analysis(CSHARP, []))
        self.assertTrue(detector.requires_analysis(JS, []))

    def test_dashboard_only_change_skips_csharp_but_not_typescript(self):
        paths = ["src/Meridian.Ui/dashboard/src/screens/Trading.tsx",
                 "src/Meridian.Ui/dashboard/package-lock.json", "docs/ui.md"]
        self.assertFalse(detector.requires_analysis(CSHARP, paths))
        self.assertTrue(detector.requires_analysis(JS, paths))

    def test_dotnet_only_change_skips_typescript_but_not_csharp(self):
        paths = ["src/Meridian.Ledger/Book.cs", "src/Meridian.FSharp/Pricing.fs",
                 "src/Meridian.Wpf/Views/Main.xaml", "src/Meridian.Ledger/Meridian.Ledger.csproj",
                 "Directory.Packages.props", "src/Meridian.Ledger/packages.lock.json",
                 "global.json", "NuGet.Config"]
        self.assertTrue(detector.requires_analysis(CSHARP, paths))
        self.assertFalse(detector.requires_analysis(JS, paths))

    def test_mixed_change_requires_both_languages(self):
        paths = ["src/Meridian.Ledger/Book.cs", "src/Meridian.Ui/dashboard/src/api.ts"]
        self.assertTrue(detector.requires_analysis(CSHARP, paths))
        self.assertTrue(detector.requires_analysis(JS, paths))

    def test_unknown_and_shared_paths_require_both_languages(self):
        for path in [".github/workflows/codeql.yml", ".github/codeql/codeql-config.yml",
                     "scripts/dev/capture-web-screenshots.mjs", "build/scripts/ci/x.py",
                     "src/Meridian/Pages/Index.cshtml", "src/Meridian/Shared/App.razor",
                     "src/Meridian/appsettings.json", "package.json",
                     "Meridian Design System/package/index.js",
                     # The dashboard root itself is not a path inside the dashboard tree.
                     "src/Meridian.Ui/dashboard", "src/Meridian.Ui/wwwroot/workstation/app.js",
                     "src/../src/Meridian.Ui/dashboard/x.ts"]:
            with self.subTest(path=path):
                self.assertTrue(detector.requires_analysis(CSHARP, [path]))
                self.assertTrue(detector.requires_analysis(JS, [path]))

    def test_unknown_language_is_rejected(self):
        with self.assertRaises(ValueError):
            detector.irrelevant_to("python", "README.md")

    def test_cli_writes_one_decision_from_a_real_diff(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as directory:
            repo = Path(directory)

            def git(*args: str) -> str:
                return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True,
                                      text=True).stdout.strip()

            git("init", "-q")
            git("config", "user.email", "ci@example.invalid")
            git("config", "user.name", "ci")
            (repo / "README.md").write_text("a\n", encoding="utf-8")
            git("add", "-A")
            git("commit", "-qm", "base")
            base = git("rev-parse", "HEAD")
            dashboard = repo / "src/Meridian.Ui/dashboard/src"
            dashboard.mkdir(parents=True)
            (dashboard / "App.tsx").write_text("export {};\n", encoding="utf-8")
            git("add", "-A")
            git("commit", "-qm", "head")
            head = git("rev-parse", "HEAD")
            for language, expected in ((CSHARP, "analyze=false"), (JS, "analyze=true")):
                with self.subTest(language=language):
                    output = repo / f"out-{language}"
                    subprocess.run(["python3", str(SCRIPT), "--base", base, "--head", head,
                                    "--language", language, "--github-output", str(output)],
                                   cwd=repo, check=True, capture_output=True)
                    self.assertEqual(output.read_text(encoding="utf-8").strip(), expected)


class CodeqlWorkflowSkipTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        workflow = yaml.load((ROOT / ".github/workflows/codeql.yml").read_text(encoding="utf-8"),
                             Loader=yaml.BaseLoader)
        cls.job = workflow["jobs"]["analyze"]
        cls.steps = {step["name"]: step for step in cls.job["steps"]}

    def test_job_always_runs_so_required_check_names_report(self):
        self.assertEqual(self.job["name"], "Analyze ${{ matrix.language }}")
        self.assertNotIn("if", self.job)
        self.assertNotIn("needs", self.job)

    def test_only_pull_requests_are_classified(self):
        detect = self.steps["Detect analyzable changes"]
        self.assertEqual(detect["id"], "changes")
        self.assertIn('if [[ "$EVENT_NAME" == "pull_request" ]]', detect["run"])
        self.assertIn("build/scripts/ci/detect-codeql-languages.py", detect["run"])
        self.assertIn('--language "$LANGUAGE"', detect["run"])
        self.assertIn('echo "analyze=true"', detect["run"])
        self.assertIn("rm -rf .codeql-change-detection", detect["run"])
        self.assertNotIn("${{", detect["run"])
        checkout = self.steps["Check out PR history for change detection"]
        self.assertEqual(checkout["if"], "github.event_name == 'pull_request'")
        self.assertEqual(checkout["with"]["persist-credentials"], "false")

    def test_every_analysis_step_is_gated_only_by_the_decision(self):
        guard = "steps.changes.outputs.analyze == 'true'"
        for name in ("Check out repository", "Initialize CodeQL", "Perform CodeQL analysis"):
            with self.subTest(step=name):
                self.assertEqual(self.steps[name]["if"], guard)
        for name in ("Set up .NET SDK", "Restore C# solution", "Build C# solution"):
            with self.subTest(step=name):
                self.assertEqual(self.steps[name]["if"], f"{guard} && matrix.language == 'csharp'")
        upload = self.steps["Retain CodeQL reports for release audit evidence"]
        self.assertEqual(upload["if"], f"always() && {guard}")
        # Release callers rely on SARIF always being present when analysis ran.
        self.assertEqual(upload["with"]["if-no-files-found"], "error")


if __name__ == "__main__":
    unittest.main()

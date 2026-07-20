import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "check-apiclient-callers.py"
SPEC = importlib.util.spec_from_file_location("check_apiclient_callers", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class CheckApiClientCallersTests(unittest.TestCase):
    def test_pattern_matches_legacy_generic_calls_only(self):
        matches = MODULE.CALL_PATTERN.findall(
            "await client.GetAsync<Foo>(url); "
            "await client.PostAsync<Foo>(url, body); "
            "await client.GetWithResponseAsync<Foo>(url); "
            "await client.PostWithResponseAsync<Foo>(url, body); "
            "await httpClient.GetAsync(url);"
        )

        self.assertEqual(len(matches), 2, "only the legacy generic Get/PostAsync calls may match")

    def test_count_call_sites_scans_src_and_skips_excluded_definition_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            caller_dir = root / "src" / "App"
            caller_dir.mkdir(parents=True)
            (caller_dir / "Caller.cs").write_text(
                "var a = await api.GetAsync<Foo>(url);\nvar b = await api.PostAsync<Foo>(url);\n",
                encoding="utf-8",
            )
            excluded = root / "src" / "Meridian.Ui.Services" / "Services"
            excluded.mkdir(parents=True)
            (excluded / "ApiClientService.cs").write_text(
                "return SendAsync<T>(method, url, () => _httpClient.GetAsync<T>(url), ct);\n",
                encoding="utf-8",
            )
            package_dir = root / "src" / "Dashboard" / "node_modules" / "meridian-tools" / "src"
            package_dir.mkdir(parents=True)
            (package_dir / "RecursiveCaller.cs").write_text(
                "var recursive = await api.GetAsync<Foo>(url);\n",
                encoding="utf-8",
            )

            counts = MODULE.count_call_sites(root)

            self.assertEqual(counts, {"src/App/Caller.cs": 2})

    def test_repository_baseline_is_current(self):
        baseline = json.loads(MODULE.DEFAULT_BASELINE.read_text(encoding="utf-8"))["files"]
        current = MODULE.count_call_sites(MODULE.REPO_ROOT)

        new_files = sorted(set(current) - set(baseline))
        regressions = sorted(
            f"{rel}: {current[rel]} > {baseline[rel]}" for rel in current if rel in baseline and current[rel] > baseline[rel]
        )

        self.assertEqual(new_files, [], "new legacy ApiClientService caller files — use *WithResponseAsync")
        self.assertEqual(regressions, [], "legacy call sites grew beyond the baseline")


if __name__ == "__main__":
    unittest.main()

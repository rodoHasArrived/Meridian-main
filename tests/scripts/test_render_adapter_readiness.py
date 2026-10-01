"""Protect deterministic readiness views and the documentation validation entry points."""

from __future__ import annotations

import argparse
import copy
import hashlib
import importlib.util
import io
import json
import re
import shlex
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
DOCS_SCRIPTS = ROOT / "build" / "scripts" / "docs"
sys.path.insert(0, str(DOCS_SCRIPTS))


def load_script(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, DOCS_SCRIPTS / filename)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


renderer = load_script("render_adapter_readiness_tests", "render-adapter-readiness.py")
automation = load_script("readiness_docs_automation_tests", "run-docs-automation.py")
source_renderer = load_script("readiness_source_docs_tests", "render-source-docs.py")


def adapter(folder: str, provider_id: str | None = "alpha") -> dict:
    """Rendering fixture; source/catalog validation has its own independent fixtures."""
    return {
        "folder": folder,
        "provider_id": provider_id,
        "aliases": ["alpha-legacy"],
        "state": "partial",
        "capabilities": {
            "streaming": True,
            "historical": False,
            "symbol_search": True,
            "options": False,
            "corporate_actions": False,
            "brokerage": True,
        },
        "adapter_types": {
            "streaming": "AlphaStreamClient",
            "historical": None,
            "symbol_search": "AlphaSymbolSearch",
            "options": None,
            "corporate_actions": None,
            "brokerage": "AlphaBrokerage",
        },
        "other_types": {"symbol_resolver": None, "compatibility_data_source": None},
        "requirements": {
            "credentials": "ALPHA_API_KEY is required.",
            "optional_sdk": "EnableAlphaSdk loads the optional Alpha SDK.",
        },
        "risks": ["Vendor pacing applies.", "Brokerage entitlement is required."],
        "degradation": "Reject missing entitlement; preserve the failure diagnostic.",
        "registration": [{"path": "src/Alpha/Registration.cs", "symbol": "AddAlpha"}],
        "evidence": [
            {"kind": "test", "path": "tests/AlphaTests.cs", "symbol": "RejectsMissingEntitlement"},
            {"kind": "source", "path": "src/Alpha/Client.cs", "symbol": "ConnectAsync"},
        ],
        "owner": "Data Confidence and Validation",
        "next_action": "Capture vendor acceptance evidence.",
    }


def registry(*rows: dict) -> dict:
    return {
        "schema": {"id": "meridian.adapter-readiness", "version": "1.0.0"},
        "adapters": list(rows or [adapter("Alpha")]),
    }


class AdapterReadinessRenderTests(unittest.TestCase):
    def invoke(self, root: Path, data: dict, *arguments: str, errors: list[str] | None = None):
        stdout, stderr = io.StringIO(), io.StringIO()
        with (
            patch.object(sys, "argv", ["render-adapter-readiness.py", "--root", str(root), *arguments]),
            patch.object(renderer, "load_registry", return_value=data),
            patch.object(renderer, "validate_registry", return_value=errors or []) as validate,
            redirect_stdout(stdout),
            redirect_stderr(stderr),
        ):
            result = renderer.main()
        validate.assert_called_once_with(root.resolve(), data)
        return result, stdout.getvalue(), stderr.getvalue()

    def test_rows_and_detail_sections_are_sorted_without_mutating_input(self):
        data = registry(adapter("Zulu", "zulu"), adapter("Alpha"))
        before = copy.deepcopy(data)
        rendered = renderer.render_matrix(data)

        self.assertEqual(before, data)
        self.assertEqual(rendered, renderer.render_matrix(registry(*reversed(data["adapters"]))))
        self.assertLess(rendered.index("| [Alpha]"), rendered.index("| [Zulu]"))
        self.assertLess(rendered.index("## Alpha"), rendered.index("## Zulu"))
        self.assertIn("do_not_edit: true", rendered)
        self.assertTrue(rendered.endswith("\n"))
        self.assertNotIn("\r", rendered)

    def test_matrix_retains_capabilities_requirements_risks_and_actionable_evidence(self):
        rendered = renderer.render_matrix(registry())
        self.assertIn("| [Alpha](#alpha) | `alpha` | partial | Yes | No | Yes | No | No | Yes |", rendered)
        for detail in (
            "**Aliases:** `alpha-legacy`",
            "**Credentials:** ALPHA_API_KEY is required.",
            "**Optional SDK:** EnableAlphaSdk loads the optional Alpha SDK.",
            "- Vendor pacing applies.",
            "- Brokerage entitlement is required.",
            "**Degradation / fail-closed behavior:** Reject missing entitlement; preserve the failure diagnostic.",
            "- Streaming: `AlphaStreamClient`",
            "- Symbol search: `AlphaSymbolSearch`",
            "- Brokerage: `AlphaBrokerage`",
            "[AddAlpha](../../../src/Alpha/Registration.cs)",
            "Test: [RejectsMissingEntitlement](../../../tests/AlphaTests.cs)",
            "Source: [ConnectAsync](../../../src/Alpha/Client.cs)",
            "**Owner:** Data Confidence and Validation",
            "**Next action:** Capture vendor acceptance evidence.",
        ):
            with self.subTest(detail=detail):
                self.assertIn(detail, rendered)
        self.assertIn("does not grant runtime entitlements", rendered)
        self.assertIn("their presence does not claim a passing live run", rendered)
        self.assertNotIn("No catalogued provider implementation is listed", rendered)
        self.assertNotIn("No runtime registration.", rendered)

    def test_separate_contracts_do_not_advertise_shared_capabilities(self):
        row = adapter("Core", None)
        row["aliases"] = []
        row["capabilities"] = {key: False for key in row["capabilities"]}
        row["adapter_types"] = {key: None for key in row["adapter_types"]}
        row["other_types"] = {
            "symbol_resolver": "CoreResolver",
            "compatibility_data_source": "CoreDataSource",
        }
        rendered = renderer.render_matrix(registry(row))
        self.assertIn("| [Core](#core) | n/a | partial | No | No | No | No | No | No |", rendered)
        self.assertIn("**Aliases:** None.", rendered)
        self.assertIn("Symbol resolver: `CoreResolver` (separate contract)", rendered)
        self.assertIn("Compatibility data source: `CoreDataSource` (separate contract)", rendered)
        self.assertNotIn("No catalogued provider implementation is listed", rendered)

    def test_excluded_families_do_not_deny_uncatalogued_implementations(self):
        rows = {row["folder"]: row for row in renderer.load_registry(ROOT)["adapters"]}
        for folder in ("Core", "Failover", "Templates", "Plaid", "TradeStation", "Tradier"):
            with self.subTest(folder=folder):
                row = rows[folder]
                before = copy.deepcopy(row)
                rendered = renderer.render_matrix(registry(row))
                self.assertIn(
                    "- No catalogued provider implementation is listed; see the scoped evidence below.",
                    rendered,
                )
                self.assertNotIn("No shared-contract provider implementation", rendered)
                self.assertIn("| No | No | No | No | No | No |", rendered)
                self.assertIn(row["degradation"], rendered)
                for item in row["evidence"]:
                    self.assertIn(renderer.reference(item), rendered)
                self.assertEqual(before, row)

    def test_unregistered_families_keep_exclusions_as_evidence_not_registration(self):
        rows = {row["folder"]: row for row in renderer.load_registry(ROOT)["adapters"]}
        exclusion_source = {
            "path": "src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs",
            "symbol": "ProviderCapabilityDescriptorCatalog",
            "kind": "source",
        }
        for folder in ("Templates", "TradeStation", "Tradier"):
            with self.subTest(folder=folder):
                row = rows[folder]
                self.assertEqual([], row["registration"])
                self.assertIn(exclusion_source, row["evidence"])
                self.assertTrue(any(item["kind"] == "test" for item in row["evidence"]))
                rendered = renderer.render_matrix(registry(row))
                registration_section, evidence_section = rendered.split("**Registration path:**", 1)[1].split(
                    "**Targeted evidence:**", 1
                )
                self.assertIn("- No runtime registration.", registration_section)
                self.assertNotIn(renderer.reference(exclusion_source), registration_section)
                self.assertIn("Source: " + renderer.reference(exclusion_source), evidence_section)

    def test_reference_labels_escape_table_delimiters_and_newlines(self):
        rendered = renderer.reference({"symbol": "Alpha|Beta\nGamma", "path": "tests/AlphaTests.cs"})
        self.assertEqual(
            "[Alpha\\|Beta Gamma](../../../tests/AlphaTests.cs) (`tests/AlphaTests.cs`)", rendered
        )

    def test_generation_is_idempotent_and_current_output_passes_check(self):
        with tempfile.TemporaryDirectory() as temp:
            root, data = Path(temp), registry()
            self.assertEqual(0, self.invoke(root, data)[0])
            output = root / renderer.OUTPUT
            original_bytes, modified = output.read_bytes(), output.stat().st_mtime_ns
            result, stdout, _ = self.invoke(root, data)
            self.assertEqual(0, result)
            self.assertIn("0 file(s) changed", stdout)
            self.assertEqual(0, self.invoke(root, data, "--check")[0])
            self.assertEqual(original_bytes, output.read_bytes())
            self.assertEqual(modified, output.stat().st_mtime_ns)

    def test_check_rejects_missing_output_without_creating_it(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            result, _, stderr = self.invoke(root, registry(), "--check")
            self.assertEqual(1, result)
            self.assertIn("missing or stale", stderr)
            self.assertFalse((root / renderer.OUTPUT).exists())

    def test_check_rejects_stale_output_without_overwriting_it(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output = root / renderer.OUTPUT
            output.parent.mkdir(parents=True)
            output.write_bytes(b"Previously reviewed matrix\n")
            result, _, stderr = self.invoke(root, registry(), "--check")
            self.assertEqual(1, result)
            self.assertIn("missing or stale", stderr)
            self.assertEqual(b"Previously reviewed matrix\n", output.read_bytes())

    def test_registry_validation_failure_preserves_output(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output = root / renderer.OUTPUT
            output.parent.mkdir(parents=True)
            output.write_text("Retained output\n", encoding="utf-8")
            result, _, stderr = self.invoke(root, registry(), errors=["unknown provider ID"])
            self.assertEqual(1, result)
            self.assertIn("ERROR: unknown provider ID", stderr)
            self.assertEqual("Retained output\n", output.read_text(encoding="utf-8"))

    def test_malformed_input_fails_without_creating_output(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / renderer.REGISTRY
            source.parent.mkdir(parents=True)
            source.write_text("not a registry mapping\n", encoding="utf-8")
            stderr = io.StringIO()
            with (
                patch.object(sys, "argv", ["render-adapter-readiness.py", "--root", str(root)]),
                redirect_stdout(io.StringIO()),
                redirect_stderr(stderr),
            ):
                result = renderer.main()
            self.assertEqual(1, result)
            self.assertIn("ERROR:", stderr.getvalue())
            self.assertNotIn("Traceback", stderr.getvalue())
            self.assertFalse((root / renderer.OUTPUT).exists())


class AdapterReadinessAutomationTests(unittest.TestCase):
    def test_documented_regeneration_refreshes_shared_source_manifest(self):
        documents = {
            "source README": (ROOT / "docs/source/README.md").read_text(encoding="utf-8"),
            "scripts README": (DOCS_SCRIPTS / "README.md").read_text(encoding="utf-8"),
            "generated matrix": renderer.render_matrix(registry()),
        }
        renderers = {
            "build/scripts/docs/render-adapter-readiness.py": renderer,
            "build/scripts/docs/render-source-docs.py": source_renderer,
        }
        for name, document in documents.items():
            with self.subTest(document=name), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                source = root / renderer.REGISTRY
                source.parent.mkdir(parents=True)
                source.write_bytes(b"{}\n")
                # Empty module maps isolate the shared-manifest behavior from unrelated source docs.
                with (
                    patch.object(source_renderer, "module_maps", return_value=({}, {}, {})),
                    patch.object(renderer, "validate_registry", return_value=[]),
                    redirect_stdout(io.StringIO()),
                ):
                    with patch.object(sys, "argv", ["render-source-docs.py", "--root", str(root)]):
                        self.assertEqual(0, source_renderer.main())
                    changed = registry()
                    changed["adapters"][0]["next_action"] = "A newly reviewed action."
                    source.write_bytes((json.dumps(changed) + "\n").encode("utf-8"))
                    block = next(
                        block for block in re.findall(r"```(?:bash|sh)\n(.*?)\n```", document, re.S)
                        if "render-adapter-readiness.py" in block
                    )
                    for line in block.splitlines():
                        command = shlex.split(line)
                        if len(command) < 2 or command[1] not in renderers:
                            continue
                        with patch.object(sys, "argv", [*command[1:], "--root", str(root)]):
                            self.assertEqual(0, renderers[command[1]].main())
                manifest = json.loads((root / "docs/source/generated/MANIFEST.json").read_text(encoding="utf-8"))
                recorded = {item["path"]: item["sha256"] for item in manifest["inputs"]}
                self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), recorded[renderer.REGISTRY.as_posix()])
                self.assertIn("A newly reviewed action.", (root / renderer.OUTPUT).read_text(encoding="utf-8"))

    def test_all_standard_profiles_select_validation_and_matrix_check(self):
        for profile in ("quick", "core", "full"):
            with self.subTest(profile=profile):
                selected = automation.resolve_selected_scripts(
                    argparse.Namespace(profile=profile, scripts=None, auto_create_todos=False)
                )
                self.assertEqual(1, selected.count("validate-adapter-readiness"))
                self.assertEqual(1, selected.count("check-adapter-readiness-matrix"))
                self.assertLess(
                    selected.index("validate-adapter-readiness"),
                    selected.index("check-adapter-readiness-matrix"),
                )

    def test_pipeline_checks_committed_output_without_regenerating_it(self):
        validator = automation.SCRIPT_CONFIG["validate-adapter-readiness"]
        matrix = automation.SCRIPT_CONFIG["check-adapter-readiness-matrix"]
        self.assertEqual("validate-adapter-readiness.py", validator["script"])
        self.assertEqual("render-adapter-readiness.py", matrix["script"])
        self.assertIn("--check", matrix["args"])


if __name__ == "__main__":
    unittest.main()

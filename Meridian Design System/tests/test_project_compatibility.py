"""Compatibility coverage must reject missing reviews and stale/false evidence."""
import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "scripts/project_compatibility.py"
SPEC = importlib.util.spec_from_file_location("project_compatibility", SCRIPT)
COMPAT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMPAT)


class CompatibilityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.root = self.repo / "Meridian Design System"
        (self.root / "api").mkdir(parents=True)
        (self.root / "components/core").mkdir(parents=True)
        source = self.root / "components/core/Button.jsx"
        source.write_text("export function Button() { return null; }\n")
        (self.root / "_ds_bundle.js").write_text("synthetic bundle\n")
        (self.root / "tests").mkdir()
        harnesses = {}
        for name in ("unit", "semantic-safety", "books-loop", "action-contract", "conformance", "a11y-run"):
            path = f"tests/{name}.html"
            (self.root / path).write_text("<title>Synthetic contract</title>")
            harnesses[path] = {"sourceSha256": COMPAT.digest(self.root / path), "cases": [{"name": "contract", "status": "passed"}]}
        self.write("_ds_manifest.json", {
            "namespace": "SyntheticDS", "components": [{"name": "Button", "sourcePath": "components/core/Button.jsx"}],
            "cards": [], "templates": [],
        })
        self.write("api/families.json", {"exports": [{"name": "Button", "family": "primitives", "stability": "stable"}]})
        self.write("api/template-eligibility.json", {"templates": {}})
        self.inventory = {
            "schemaVersion": 1, "namespace": "SyntheticDS",
            "counts": {"exports": 1, "modules": 1, "cards": 0, "publishedTemplates": 0,
                       "templateDirectories": 0, "templateEntries": 0, "browserSurfaces": 0},
            "basis": {p: COMPAT.digest(self.root / p) for p in
                      ("_ds_manifest.json", "api/families.json", "api/template-eligibility.json")},
            "exports": [{"name": "Button", "source": "components/core/Button.jsx",
                         "sourceHash": COMPAT.digest(source), "family": "primitives", "maturity": "stable", "dependencies": [],
                         "behaviorCoverage": {"reviewedSourceHash": COMPAT.digest(source), "ownsInteraction": True,
                                              "coverageStatus": "complete", "remainingApplicableStates": [],
                                              "applicableStates": ["enabled", "disabled"], "testedStates": ["enabled", "disabled"],
                                              "cases": [{"path": "tests/synthetic-regression.mjs", "name": "callback and disabled guard", "status": "passed"}]},
                         "review": {"kind": "component", "decision": "retain-existing", "rationale": "Synthetic source contract reviewed",
                                    "applicableStates": ["enabled", "disabled"], "reviewedSourceHash": COMPAT.digest(source),
                                    "issues": [], "evidence": [{"kind": "source-review", "reference": "components/core/Button.jsx", "status": "reviewed"}]}}],
            "cards": [], "templates": [], "browserSurfaces": [],
            "browserEvidence": {"source": {"manifestSha256": COMPAT.digest(self.root / "_ds_manifest.json"),
                                           "bundleSha256": COMPAT.digest(self.root / "_ds_bundle.js"),
                                           "runtimeSourceHashes": {"components/core/Button.jsx": COMPAT.digest(source)},
                                           "runtimeSourceHashesStable": True, "changedRuntimeSources": [],
                                           "run": {"kind": "all", "matrix": "full", "only": None, "limit": None}},
                                "cards": {}, "templates": {}, "exports": {"Button": {"cases": [{"name": "value exists", "status": "passed"}]}},
                                "harnesses": harnesses, "themes": [],
                                "integrity": {"cases": [{"name": name, "status": "passed"} for name in
                                                         ("media query", "physical size", "overflow negative", "source stability")]}},
            "sourceEvidence": {"status": "passed", "sourceStable": True, "changedSources": [],
                               "sourceHashes": {"components/core/Button.jsx": COMPAT.digest(source),
                                                **{p: COMPAT.digest(self.root / p) for p in harnesses}},
                               "suites": [{"path": f"tests/{name}", "status": "passed", "exitCode": 0}
                                          for name in ("public-contract-types.mjs",)]},
        }
        self.inventory["sourceEvidence"]["sourceHashes"] = {
            path.relative_to(self.root).as_posix(): COMPAT.digest(path) for path in COMPAT.source_evidence_paths(self.root)
        }

    def write(self, path, value):
        (self.root / path).write_text(json.dumps(value))

    def errors(self, value=None):
        return COMPAT.validate(self.inventory if value is None else value, self.root, self.repo)

    def test_complete_current_source_review_passes_without_claiming_behavior(self):
        self.assertEqual([], self.errors())
        self.assertEqual("reviewed", self.inventory["exports"][0]["review"]["evidence"][0]["status"])

    def test_grouped_recipes_accept_report_strings_without_counting_them_as_tests(self):
        case = self.inventory["exports"][0]["behaviorCoverage"]["cases"][0]
        recipe = case.pop("path")
        case.update({"paths": [recipe], "references": [recipe], "evidence": "/tmp/executed-report.json"})
        self.assertEqual([], self.errors())
        case["paths"] = case["references"] = []
        self.assertTrue(any("not interaction proof" in error for error in self.errors()))

    def test_plural_type_compilation_references_cannot_certify_interaction(self):
        case = self.inventory["exports"][0]["behaviorCoverage"]["cases"][0]
        case.pop("path")
        case["references"] = ["tests/public-contract-types.mjs"]
        self.assertTrue(any("not interaction proof" in error for error in self.errors()))

    def test_missing_and_duplicate_public_exports_fail(self):
        value = copy.deepcopy(self.inventory)
        value["exports"] = []
        self.assertTrue(any("coverage differs" in x for x in self.errors(value)))
        value["exports"] = self.inventory["exports"] * 2
        self.assertIn("Duplicate exports identities", self.errors(value))

    def test_no_review_or_source_hash_is_not_a_review(self):
        value = copy.deepcopy(self.inventory)
        value["exports"][0]["review"] = None
        self.assertTrue(any("Unreviewed" in x for x in self.errors(value)))
        value["exports"][0]["review"] = dict(self.inventory["exports"][0]["review"])
        del value["exports"][0]["review"]["reviewedSourceHash"]
        self.assertTrue(any("Missing reviewed-source hash" in x for x in self.errors(value)))

    def test_changed_source_invalidates_both_snapshot_and_review(self):
        (self.root / "components/core/Button.jsx").write_text("export const Button = null;\n")
        self.assertTrue(any("Stale exports source" in x for x in self.errors()))
        value = copy.deepcopy(self.inventory)
        value["exports"][0]["sourceHash"] = COMPAT.digest(self.root / "components/core/Button.jsx")
        self.assertTrue(any("Review predates" in x for x in self.errors(value)))

    def test_public_maturity_and_source_cannot_be_reassigned(self):
        value = copy.deepcopy(self.inventory)
        value["exports"][0]["maturity"] = "release-candidate"
        self.assertTrue(any("Family/maturity mismatch" in x for x in self.errors(value)))
        (self.root / "components/core/Other.jsx").write_text("export const Other = null;")
        value["exports"][0]["source"] = "components/core/Other.jsx"
        value["exports"][0]["sourceHash"] = COMPAT.digest(self.root / value["exports"][0]["source"])
        self.assertTrue(any("source differs from public manifest" in x for x in self.errors(value)))

    def test_source_review_does_not_become_a_behavioral_pass(self):
        value = copy.deepcopy(self.inventory)
        value["exports"][0]["review"]["evidence"][0]["status"] = "passed"
        self.assertTrue(any("mislabeled as behavioral pass" in x for x in self.errors(value)))

    def test_open_defects_prevent_adoption_and_missing_imports_fail(self):
        value = copy.deepcopy(self.inventory)
        value["exports"][0]["review"].update(decision="adopt", issues=[{"id": "synthetic-defect", "status": "open"}])
        self.assertTrue(any("Cannot adopt" in x for x in self.errors(value)))
        value["exports"][0]["dependencies"] = [{"kind": "source", "specifier": "./Absent", "path": None}]
        self.assertTrue(any("Unresolved import" in x for x in self.errors(value)))

    def test_template_coverage_uses_every_declared_entry_not_only_picker(self):
        self.write("api/template-eligibility.json", {"templates": {"synthetic": {"entries": ["Current.dc.html", "Legacy.dc.html"]}}})
        value = copy.deepcopy(self.inventory)
        value["basis"]["api/template-eligibility.json"] = COMPAT.digest(self.root / "api/template-eligibility.json")
        self.assertTrue(any("Legacy.dc.html" in x for x in self.errors(value)))

    def test_browser_evidence_must_be_complete_current_and_successful(self):
        value = copy.deepcopy(self.inventory)
        value["browserEvidence"] = None
        self.assertIn("Missing executed browser evidence", self.errors(value))
        value = copy.deepcopy(self.inventory)
        value["browserEvidence"]["exports"] = {}
        self.assertTrue(any("Browser exports coverage differs" in x for x in self.errors(value)))
        value = copy.deepcopy(self.inventory)
        value["browserEvidence"]["source"]["bundleSha256"] = "old"
        self.assertIn("Stale browser evidence: _ds_bundle.js", self.errors(value))
        value = copy.deepcopy(self.inventory)
        value["browserEvidence"]["exports"]["Button"]["cases"][0]["status"] = "failed"
        self.assertTrue(any("Failed browser case" in x for x in self.errors(value)))

    def test_unexplained_blocks_and_missing_basis_are_rejected(self):
        value = copy.deepcopy(self.inventory)
        value["browserEvidence"]["exports"]["Button"]["cases"][0]["status"] = "blocked"
        self.assertTrue(any("Unexplained blocked" in x for x in self.errors(value)))
        value = copy.deepcopy(self.inventory)
        value["basis"] = {}
        self.assertIn("Missing compatibility source-of-truth basis", self.errors(value))

    def test_shared_runtime_changes_invalidate_browser_proof(self):
        value = copy.deepcopy(self.inventory)
        value["browserEvidence"]["source"]["runtimeSourceHashes"] = {}
        self.assertIn("Browser runtime dependency coverage differs", self.errors(value))
        value = copy.deepcopy(self.inventory)
        value["browserEvidence"]["source"]["runtimeSourceHashesStable"] = False
        self.assertIn("Browser sources changed during execution", self.errors(value))

    def test_browser_audit_helper_is_a_required_hashed_runtime_dependency(self):
        helper = self.root / "tests/browser-audit-helpers.mjs"
        helper.write_text("export const fixture = true;\n")
        value = copy.deepcopy(self.inventory)
        value["sourceEvidence"]["sourceHashes"]["tests/browser-audit-helpers.mjs"] = COMPAT.digest(helper)
        self.assertIn("Browser runtime dependency coverage differs", self.errors(value))
        value["browserEvidence"]["source"]["runtimeSourceHashes"]["tests/browser-audit-helpers.mjs"] = COMPAT.digest(helper)
        self.assertEqual([], self.errors(value))
        helper.write_text("export const fixture = false;\n")
        self.assertIn("Stale browser runtime dependency: tests/browser-audit-helpers.mjs", self.errors(value))

    def test_missing_or_failed_source_contracts_cannot_be_certified(self):
        value = copy.deepcopy(self.inventory)
        value["sourceEvidence"] = None
        self.assertIn("Missing executed source-contract evidence", self.errors(value))
        value = copy.deepcopy(self.inventory)
        value["sourceEvidence"]["suites"][0]["exitCode"] = 1
        self.assertIn("Failed source-contract suite", self.errors(value))

    def test_the_report_cannot_claim_an_invented_census(self):
        value = copy.deepcopy(self.inventory)
        value["counts"]["exports"] = 999
        self.assertIn("Compatibility census differs from source contracts", self.errors(value))

    def test_each_interactive_export_needs_actual_state_cases(self):
        value = copy.deepcopy(self.inventory)
        coverage = value["exports"][0]["behaviorCoverage"]
        coverage["cases"][0]["path"] = "tests/public-contract-types.mjs"
        self.assertIn("Type compilation is not interaction proof: Button", self.errors(value))
        coverage["remainingApplicableStates"] = ["disabled"]
        coverage["testedStates"] = ["enabled"]
        self.assertIn("Incomplete applicable-state coverage: Button", self.errors(value))
        self.assertIn("Untested declared export states: Button", self.errors(value))

    def test_filtered_runs_and_failed_zoom_controls_do_not_certify_the_library(self):
        value = copy.deepcopy(self.inventory)
        value["browserEvidence"]["source"]["run"]["only"] = "Button"
        self.assertIn("Filtered browser runs cannot certify full compatibility", self.errors(value))
        value = copy.deepcopy(self.inventory)
        value["browserEvidence"]["integrity"]["cases"][0]["status"] = "failed"
        self.assertIn("Missing or failed browser integrity/zoom controls", self.errors(value))

    def test_dashboard_proof_rejects_partial_skipped_or_stale_results(self):
        source = self.repo / "src/Meridian.Ui/dashboard/src/screens"
        source.mkdir(parents=True)
        code, test = source / "Screen.tsx", source / "Screen.test.tsx"
        code.write_text("export const Screen = () => null;")
        test.write_text("// Synthetic executed test fixture")
        identity = test.relative_to(self.repo).as_posix()
        proof = {"counts": {"files": 1, "passed": 1, "failed": 0, "skipped": 0},
                 "sourceHashes": {p.relative_to(self.repo).as_posix(): COMPAT.digest(p) for p in (code, test)},
                 "results": [{"path": identity, "sha256": COMPAT.digest(test), "status": "passed", "passed": 1, "failed": 0, "skipped": 0}],
                 "maintainedBrowser": {"result": "PASS", "accessibilityViolations": 0},
                 "primaryAdoptionBrowser": {"summary": {"cases": 24, "axeScans": 24, "failed": 0, "axeViolations": 0, "exceptions": 0, "networkFailures": 0},
                                            "cases": [{"status": "passed"}]}}
        self.assertEqual([], COMPAT.validate_dashboard_evidence(proof, self.repo))
        partial = copy.deepcopy(proof)
        partial["results"] = []
        self.assertIn("Full dashboard test-file coverage differs", COMPAT.validate_dashboard_evidence(partial, self.repo))
        proof["results"][0]["skipped"] = 1
        self.assertIn(f"Incomplete dashboard test evidence: {identity}", COMPAT.validate_dashboard_evidence(proof, self.repo))
        code.write_text("export const Screen = () => 'changed';")
        self.assertTrue(any("Stale dashboard source" in error for error in COMPAT.validate_dashboard_evidence(proof, self.repo)))

    def test_declaration_contracts_cannot_replace_interactive_state_proof(self):
        value = copy.deepcopy(self.inventory)
        value["exports"][0]["behaviorCoverage"].update(coverageStatus="declaration-contract-only", cases=[])
        self.assertIn("Declaration-only proof cannot cover owned behavior: Button", self.errors(value))


if __name__ == "__main__":
    unittest.main()

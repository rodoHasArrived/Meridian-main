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
            "basis": {p: COMPAT.digest(self.root / p) for p in
                      ("_ds_manifest.json", "api/families.json", "api/template-eligibility.json")},
            "exports": [{"name": "Button", "source": "components/core/Button.jsx",
                         "sourceHash": COMPAT.digest(source), "family": "primitives", "maturity": "stable", "dependencies": [],
                         "review": {"kind": "component", "decision": "retain-existing", "rationale": "Synthetic source contract reviewed",
                                    "applicableStates": ["enabled", "disabled"], "reviewedSourceHash": COMPAT.digest(source),
                                    "issues": [], "evidence": [{"kind": "source-review", "reference": "components/core/Button.jsx", "status": "reviewed"}]}}],
            "cards": [], "templates": [], "browserSurfaces": [],
            "browserEvidence": {"source": {"manifestSha256": COMPAT.digest(self.root / "_ds_manifest.json"),
                                           "bundleSha256": COMPAT.digest(self.root / "_ds_bundle.js"),
                                           "runtimeSourceHashes": {"components/core/Button.jsx": COMPAT.digest(source)},
                                           "runtimeSourceHashesStable": True, "changedRuntimeSources": []},
                                "cards": {}, "templates": {}, "exports": {"Button": {"cases": [{"name": "value exists", "status": "passed"}]}},
                                "harnesses": harnesses, "themes": []},
        }

    def write(self, path, value):
        (self.root / path).write_text(json.dumps(value))

    def errors(self, value=None):
        return COMPAT.validate(self.inventory if value is None else value, self.root, self.repo)

    def test_complete_current_source_review_passes_without_claiming_behavior(self):
        self.assertEqual([], self.errors())
        self.assertEqual("reviewed", self.inventory["exports"][0]["review"]["evidence"][0]["status"])

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


if __name__ == "__main__":
    unittest.main()

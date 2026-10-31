from __future__ import annotations

import copy
import importlib.util
import sys
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
DOCS_SCRIPTS = ROOT / "build" / "scripts" / "docs"
SPEC = importlib.util.spec_from_file_location(
    "validate_design_document_adaptation",
    DOCS_SCRIPTS / "validate-design-document-adaptation.py",
)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = module
sys.path.insert(0, str(DOCS_SCRIPTS))
try:
    SPEC.loader.exec_module(module)
finally:
    sys.path.remove(str(DOCS_SCRIPTS))


class DesignDocumentAdaptationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.adaptation = module.load_data(ROOT / module.ADAPTATION_MAP)

    def context(self, name: str) -> dict:
        return next(context for context in self.adaptation["bounded_contexts"] if context["name"] == name)

    def errors(self, *, roadmap: dict | None = None, decisions: dict | None = None) -> list[str]:
        original_load = module.load_data
        replacements = {ROOT / module.ADAPTATION_MAP: self.adaptation}
        if roadmap is not None:
            replacements[ROOT / module.ROADMAP_REGISTRY] = roadmap
        if decisions is not None:
            replacements[ROOT / module.DECISION_REGISTRY] = decisions

        def load(path: Path) -> object:
            return replacements[path] if path in replacements else original_load(path)

        with patch.object(module, "load_data", side_effect=load):
            return [finding.message for finding in module.validate(ROOT) if finding.severity == "error"]

    def test_current_repository_contract_passes(self) -> None:
        self.assertEqual([], [finding for finding in module.validate(ROOT) if finding.severity == "error"])

    def test_rejects_blanket_research_or_trading_deferral(self) -> None:
        for name in ["Research & Analytics", "Trading & Execution"]:
            with self.subTest(context=name):
                context = self.context(name)
                original = copy.deepcopy(context)
                context["stage"] = "deferred"
                context["deferral_reason"] = "Deferred behind W1-W5."
                self.assertTrue(any(f"{name} must have adopted scope with planned delivery" in error for error in self.errors()))
                context.clear()
                context.update(original)

    def test_rejects_deferral_hidden_behind_planned_stage_or_duplicate(self) -> None:
        context = self.context("Research & Analytics")
        context["deferral_reason"] = "Backtesting Studio is delayed behind W1-W5."
        self.assertTrue(any("cannot retain a blanket deferral_reason" in error for error in self.errors()))
        del context["deferral_reason"]
        stale = copy.deepcopy(context)
        stale["stage"] = "deferred"
        self.adaptation["bounded_contexts"].insert(0, stale)
        self.assertTrue(any("duplicate bounded context adaptation entry Research & Analytics" in error for error in self.errors()))

    def test_rejects_missing_adopted_context(self) -> None:
        for name in ["Research & Analytics", "Trading & Execution"]:
            with self.subTest(context=name):
                original = self.adaptation["bounded_contexts"]
                self.adaptation["bounded_contexts"] = [context for context in original if context["name"] != name]
                self.assertTrue(any(f"missing bounded context adaptation entry {name}" in error for error in self.errors()))
                self.adaptation["bounded_contexts"] = original

    def test_rejects_activation_of_unrelated_deferred_contexts_and_mobile(self) -> None:
        for name in ["Treasury & Payments", "Alternative Assets", "Financing & Capital Structure", "Planning & Forecasting", "Risk Management"]:
            with self.subTest(context=name):
                context = self.context(name)
                context["stage"] = "planned"
                self.assertTrue(any(f"{name} must be deferred" in error for error in self.errors()))
                context["stage"] = "deferred"
        self.adaptation["product_baseline"]["no_mobile_development_lane"] = False
        self.assertTrue(any("no_mobile_development_lane must be true" in error for error in self.errors()))

    def test_rejects_missing_or_unaccepted_direction_references(self) -> None:
        context = self.context("Trading & Execution")
        context["roadmap_items"] = ["W11-EXEC-001"]
        self.assertTrue(any("Trading & Execution planned roadmap references" in error for error in self.errors()))
        context["roadmap_items"] = ["W13-OMS-001"]
        decisions = module.load_data(ROOT / module.DECISION_REGISTRY)
        decisions["decisions"] = [decision for decision in decisions["decisions"] if decision["id"] != module.ADOPTION_DECISION]
        self.assertTrue(any("must record accepted scope adoption" in error for error in self.errors(decisions=decisions)))

    def test_rejects_roadmap_completion_claim_and_fictitious_runtime_owner(self) -> None:
        roadmap = module.load_data(ROOT / module.ROADMAP_REGISTRY)
        item = next(item for item in roadmap["items"] if item["id"] == "W13-STRAT-001")
        item["status"] = "done"
        item["evidence_posture"] = "complete"
        self.assertTrue(any("W13-STRAT-001 must retain planned delivery" in error for error in self.errors(roadmap=roadmap)))
        self.context("Research & Analytics")["runtime_projects"] = ["src/Meridian.Research"]
        self.assertTrue(any("Research & Analytics runtime projects" in error for error in self.errors()))

    def test_rejects_financial_baseline_ceiling_and_stale_charter_reference(self) -> None:
        self.adaptation["product_baseline"]["development_ceiling"] = True
        self.assertTrue(any("development_ceiling false" in error for error in self.errors()))
        self.adaptation["design_source"]["version"] = "1.5"
        self.adaptation["design_source"]["sections"].remove(module.DIRECTION_SECTION)
        errors = self.errors()
        self.assertTrue(any("design_source version must be 1.6" in error for error in errors))
        self.assertTrue(any("design_source must include 22.7" in error for error in errors))


if __name__ == "__main__":
    unittest.main()

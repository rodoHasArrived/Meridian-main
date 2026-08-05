import importlib.util
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "build" / "scripts" / "docs"
MODULE_PATH = SCRIPTS / "generate-api-contract-coverage-dashboard.py"

# The generator imports a sibling helper module by bare name, so its own directory has to be
# importable before the spec is executed.
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

spec = importlib.util.spec_from_file_location("api_contract_coverage_dashboard", MODULE_PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class MentionBoundaryTests(unittest.TestCase):
    """A contract or route counts as documented only when a doc names *it*.

    Every case below was observed in this repository's own docs while the check was a plain
    substring test, which is why they are pinned rather than invented.
    """

    def test_a_name_inside_a_longer_method_name_is_not_a_mention(self) -> None:
        # `ApprovalDecision` was reported documented because a design blueprint described a
        # `RecordApprovalDecisionAsync` store method — a different subsystem entirely.
        self.assertFalse(
            module._mentions("approvaldecision", "calls `recordapprovaldecisionasync` inside")
        )

    def test_a_name_inside_a_file_path_is_not_a_mention(self) -> None:
        # `SettlementInstruction` and `WorkflowLibraryDto` were credited for appearing inside
        # `SettlementInstructionCommands.fs` and `WorkflowLibraryDtos.cs`.
        self.assertFalse(
            module._mentions("settlementinstruction", "src/domain/settlementinstructioncommands.fs")
        )
        self.assertFalse(
            module._mentions("workflowlibrarydto", "src/workstation/workflowlibrarydtos.cs")
        )

    def test_a_name_inside_a_longer_contract_name_is_not_a_mention(self) -> None:
        # The longer contract is scanned and credited on its own, so the shorter one must not
        # ride along.
        self.assertFalse(
            module._mentions("startworkflow", "allowed by `operationsstartworkflowrequestdto`")
        )
        self.assertFalse(
            module._mentions("recommendedactiondto", "see securitymasterrecommendedactiondto")
        )

    def test_a_real_mention_still_counts(self) -> None:
        for text in (
            "the `approvaldecision` contract carries",
            "returns approvaldecision.",
            "workstation/approvaldecision is the shape",   # `/` before is a boundary
            "approvaldecision",
        ):
            with self.subTest(text=text):
                self.assertTrue(module._mentions("approvaldecision", text))

    def test_a_route_is_not_credited_by_a_longer_route(self) -> None:
        # `/api/backfill/run` must not be credited by `/api/backfill/runs` or by a deeper path,
        # both of which are separate endpoints the scan reports independently.
        self.assertFalse(module._mentions("/api/backfill/run", "see /api/backfill/runs for"))
        self.assertFalse(module._mentions("/api/backfill/run", "see /api/backfill/run/{id} for"))

    def test_a_route_documented_on_its_own_still_counts(self) -> None:
        for text in ("`/api/backfill/run` triggers", "POST /api/backfill/run\n", "/api/backfill/run."):
            with self.subTest(text=text):
                self.assertTrue(module._mentions("/api/backfill/run", text))


class GeneratorContractTests(unittest.TestCase):
    def test_generated_doc_roots_stay_excluded(self) -> None:
        # The dashboard echoes every name it scans, so counting its own output would let coverage
        # climb without any document being written. Pinned because the boundary fix above is only
        # half the guard against a metric that inflates itself.
        self.assertEqual(("docs/status", "docs/generated"), module.GENERATED_DOC_ROOTS)



class CoverageReportBoundaryTests(unittest.TestCase):
    """`generate-coverage.py` had the same substring defect on public type names."""

    def setUp(self) -> None:
        path = SCRIPTS / "generate-coverage.py"
        spec_cov = importlib.util.spec_from_file_location("generate_coverage", path)
        assert spec_cov is not None and spec_cov.loader is not None
        self.cov = importlib.util.module_from_spec(spec_cov)
        sys.modules[spec_cov.name] = self.cov
        spec_cov.loader.exec_module(self.cov)

    def _documented(self, name: str, doc_text: str) -> bool:
        item = self.cov.SourceItem(name=name, file_path="x.cs", line=1)
        self.cov._check_type_documentation([item], {"d.md": doc_text})
        return item.documented

    def test_a_type_inside_a_longer_type_is_not_documented(self) -> None:
        # Observed: `PriceMark`, `RunResult`, and `ExportPackage` were credited by
        # `DailyPortfolioPriceMark.cs`, `ScriptRunResult.cs`, and
        # `LedgerScheduledReportExportPackageBuilder.cs` appearing in a generated file tree.
        self.assertFalse(self._documented("PriceMark", "see DailyPortfolioPriceMark.cs"))
        self.assertFalse(self._documented("RunResult", "see ScriptRunResult.cs"))
        self.assertFalse(self._documented("ScheduleState", "`AutomatedJournalScheduleStateDto`"))

    def test_a_named_type_is_still_documented(self) -> None:
        self.assertTrue(self._documented("PriceMark", "The `PriceMark` record carries"))
        self.assertTrue(self._documented("PriceMark", "PriceMark."))

    def test_only_the_self_referential_reports_are_excluded(self) -> None:
        # Narrow on purpose. `repository-structure.md` lists every path in the repository and
        # `documentation-coverage.md` is this generator's own output, so both would let a type
        # count as documented for existing or for being reported undocumented. Excluding the
        # whole `docs/generated/` subtree instead — which an earlier revision of this branch did —
        # marked 1,880 genuinely documented types as gaps.
        self.assertEqual(
            (
                "docs/status/",
                "docs/generated/documentation-coverage.md",
                "docs/generated/repository-structure.md",
            ),
            self.cov.DOC_CONTENT_EXCLUDE_PREFIXES,
        )

    def test_the_generated_database_catalog_stays_in_the_corpus(self) -> None:
        # `docs/generated/database/**` is the PostgreSQL data-object catalog named in
        # `docs/generated/README.md`, and its pages carry field-level reference documentation.
        # A loader-level check, because the bug this pins was in *what gets loaded*, not in the
        # matching — asserting the prefix tuple alone would not have caught it.
        loaded = self.cov._load_doc_contents(ROOT)
        catalog = [k for k in loaded if k.startswith("docs/generated/database/")]
        self.assertTrue(catalog, "the generated database catalog must remain documentation")

        excluded = [
            k for k in loaded
            if k in ("docs/generated/documentation-coverage.md",
                     "docs/generated/repository-structure.md")
            or k.startswith("docs/status/")
        ]
        self.assertEqual([], excluded)

if __name__ == "__main__":
    unittest.main()

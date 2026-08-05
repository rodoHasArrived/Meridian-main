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


def documents_name(name: str, docs_text: str) -> bool:
    """Exactly how `build_dashboard` decides a contract is documented."""
    names, _paths = module._index_docs(docs_text)
    return name.casefold() in names


def documents_route(route: str, docs_text: str) -> bool:
    """Exactly how `build_dashboard` decides an endpoint is documented."""
    _names, paths = module._index_docs(docs_text)
    return module._normalize_route(route) in paths


_BOUNDARY_BEFORE = frozenset("0123456789abcdefghijklmnopqrstuvwxyz_-")
_BOUNDARY_AFTER = frozenset("0123456789abcdefghijklmnopqrstuvwxyz_/-")


def reference_mentions(term: str, docs_text: str) -> bool:
    """An independent statement of the boundary rule, used only as a test oracle.

    Decides by walking occurrences and inspecting the characters either side, where the
    implementation decides by tokenizing the corpus into a set. The two share no code, which is
    what makes comparing them worth anything — and the comparison has already caught one real
    defect, the `/*` family-glob case pinned below.

    Deliberately not the `re.search(r"(?<!…)" + escape(term) + r"(?!…)")` form this replaced. That
    version rescans the whole ~10MB corpus per term, so running it over all 1,523 items took 85s
    of the test lane — the very cost that made the generator's own per-item scan untenable. `find`
    short-circuits on the first clean hit and never walks past the last occurrence.
    """
    start = docs_text.find(term)
    while start != -1:
        end = start + len(term)
        before_ok = start == 0 or docs_text[start - 1] not in _BOUNDARY_BEFORE
        after_ok = end == len(docs_text) or docs_text[end] not in _BOUNDARY_AFTER
        if before_ok and after_ok:
            return True
        start = docs_text.find(term, start + 1)
    return False


class MentionBoundaryTests(unittest.TestCase):
    """A contract or route counts as documented only when a doc names *it*.

    Every case below was observed in this repository's own docs while the check was a plain
    substring test, which is why they are pinned rather than invented. Each goes through
    `_index_docs`, the code path `build_dashboard` actually uses.
    """

    def test_a_name_inside_a_longer_method_name_is_not_a_mention(self) -> None:
        # `ApprovalDecision` was reported documented because a design blueprint described a
        # `RecordApprovalDecisionAsync` store method — a different subsystem entirely.
        self.assertFalse(
            documents_name("approvaldecision", "calls `recordapprovaldecisionasync` inside")
        )

    def test_a_name_inside_a_file_path_is_not_a_mention(self) -> None:
        # `SettlementInstruction` and `WorkflowLibraryDto` were credited for appearing inside
        # `SettlementInstructionCommands.fs` and `WorkflowLibraryDtos.cs`.
        self.assertFalse(
            documents_name("settlementinstruction", "src/domain/settlementinstructioncommands.fs")
        )
        self.assertFalse(
            documents_name("workflowlibrarydto", "src/workstation/workflowlibrarydtos.cs")
        )

    def test_a_name_inside_a_longer_contract_name_is_not_a_mention(self) -> None:
        # The longer contract is scanned and credited on its own, so the shorter one must not
        # ride along.
        self.assertFalse(
            documents_name("startworkflow", "allowed by `operationsstartworkflowrequestdto`")
        )
        self.assertFalse(
            documents_name("recommendedactiondto", "see securitymasterrecommendedactiondto")
        )

    def test_a_real_mention_still_counts(self) -> None:
        for text in (
            "the `approvaldecision` contract carries",
            "returns approvaldecision.",
            "workstation/approvaldecision is the shape",   # `/` before is a boundary
            "approvaldecision",
        ):
            with self.subTest(text=text):
                self.assertTrue(documents_name("approvaldecision", text))

    def test_a_route_is_not_credited_by_a_longer_route(self) -> None:
        # `/api/backfill/run` must not be credited by `/api/backfill/runs` or by a deeper path,
        # both of which are separate endpoints the scan reports independently.
        self.assertFalse(documents_route("/api/backfill/run", "see /api/backfill/runs for"))
        self.assertFalse(documents_route("/api/backfill/run", "see /api/backfill/run/{id} for"))

    def test_a_route_is_not_credited_by_a_wildcard_over_its_family(self) -> None:
        # Regression. The corpus writes ``/api/security-master/*`` in a passing architectural
        # aside listing route families. An earlier revision of the index trimmed `/` and `-` off
        # a token as if they were sentence punctuation, which turned that glob into a credit for
        # the specific `/api/security-master` endpoint — reintroducing, in the fix itself, the
        # longer-path defect this module exists to remove.
        for text in (
            "adjacent `/api/portfolio/*`, `/api/security-master/*`, and provider handoff",
            "everything under /api/security-master/ is governed",
        ):
            with self.subTest(text=text):
                self.assertFalse(documents_route("/api/security-master", text))

    def test_a_route_documented_on_its_own_still_counts(self) -> None:
        for text in (
            "`/api/backfill/run` triggers",
            "POST /api/backfill/run\n",
            "/api/backfill/run.",              # sentence period is not part of the route
            "call /api/backfill/run: it returns",
        ):
            with self.subTest(text=text):
                self.assertTrue(documents_route("/api/backfill/run", text))


class IndexAgreementTests(unittest.TestCase):
    """The shipped index must agree with an independently written statement of the same rule.

    `_index_docs` decides the boundary by tokenizing, `reference_mentions` by scanning. They are
    different enough that a defect in one is unlikely to appear in the other, and running both
    over the *real* corpus is what caught the `/api/security-master/*` case above — which no
    hand-written example had covered.
    """

    def test_the_index_agrees_with_an_independent_scan(self) -> None:
        docs_text = module._load_docs_text(ROOT)
        names, paths = module._index_docs(docs_text)

        disagreements = []
        for contract in module._scan_workstation_contracts(ROOT):
            name = str(contract["name"]).casefold()
            if reference_mentions(name, docs_text) != (name in names):
                disagreements.append(("contract", name))
        for endpoint in module._scan_endpoints(ROOT):
            route = module._normalize_route(str(endpoint["path"]))
            if reference_mentions(route, docs_text) != (route in paths):
                disagreements.append(("endpoint", route))

        self.assertEqual([], disagreements)


class GeneratorContractTests(unittest.TestCase):
    def test_generated_doc_roots_stay_excluded(self) -> None:
        # The dashboard echoes every name it scans, so counting its own output would let coverage
        # climb without any document being written. Pinned because the boundary fix above is only
        # half the guard against a metric that inflates itself.
        self.assertEqual(("docs/status", "docs/generated"), module.GENERATED_DOC_ROOTS)

    def test_the_boundary_rule_is_reachable_from_the_dashboard(self) -> None:
        # `_index_docs` replaced a per-item scan that these tests used to call directly. Once the
        # scan was no longer on the hot path, tests against it proved nothing about the artifact.
        # Pinned so a future refactor cannot orphan the rule again.
        source = MODULE_PATH.read_text(encoding="utf-8")
        build = source[source.index("def build_dashboard("):]
        self.assertIn("_index_docs(", build)


class _CoverageModuleTestCase(unittest.TestCase):
    """Loads `generate-coverage.py`, which is not importable by name."""

    def setUp(self) -> None:
        path = SCRIPTS / "generate-coverage.py"
        spec_cov = importlib.util.spec_from_file_location("generate_coverage", path)
        assert spec_cov is not None and spec_cov.loader is not None
        self.cov = importlib.util.module_from_spec(spec_cov)
        sys.modules[spec_cov.name] = self.cov
        spec_cov.loader.exec_module(self.cov)


class NamesTermTests(_CoverageModuleTestCase):
    """The shared boundary rule, stated once and used by three checks."""

    def test_a_term_inside_a_longer_one_is_not_named(self) -> None:
        self.assertFalse(self.cov._names_term("DailyPortfolioPriceMark", "PriceMark"))
        self.assertFalse(self.cov._names_term("see /api/backfill/runs", "/api/backfill/run"))

    def test_a_slash_is_a_boundary_before_but_not_after(self) -> None:
        # A doc writing `docs/api-reference` is referring to the thing after the slash, so `/` on
        # the leading side is a boundary. On the trailing side it is not: `/api/backfill/run/{id}`
        # is a different endpoint, reported separately, and must not credit `/api/backfill/run`.
        self.assertTrue(self.cov._names_term("see docs/api-reference here", "api-reference"))
        self.assertFalse(self.cov._names_term("see /api/backfill/run/{id}", "/api/backfill/run"))

    def test_a_named_term_counts_at_either_edge_of_the_text(self) -> None:
        for text in ("PriceMark", "PriceMark trails", "leads PriceMark"):
            with self.subTest(text=text):
                self.assertTrue(self.cov._names_term(text, "PriceMark"))

    def test_an_empty_term_is_never_named(self) -> None:
        # `_check_endpoint_documentation` strips parameter segments, and a route that is nothing
        # but parameters reduces to "". Without this guard `str.find("")` returns 0 and every such
        # route would count as documented by any text at all.
        self.assertFalse(self.cov._names_term("any text", ""))


class EndpointBoundaryTests(_CoverageModuleTestCase):
    """`_check_endpoint_documentation` matched routes as substrings."""

    def _documented(self, route: str, doc_text: str) -> bool:
        # The check reads two fixed files, so the doc text is supplied by patching the reader.
        self.cov._read_text_safe = lambda _path, _t=doc_text: _t
        item = self.cov.SourceItem(name=route, file_path="x.cs", line=1)
        self.cov._check_endpoint_documentation([item], Path("/nonexistent"))
        return item.documented

    def test_a_route_fragment_is_not_credited_by_a_longer_path(self) -> None:
        # Observed. This scan collects relative fragments from route groups, and a fragment is a
        # substring of almost any documented path: `/complete`, `/reject`, `/{loanId}/activate`,
        # `/{runId}/govern` and four others counted as documented with no doc naming them.
        self.assertFalse(self._documented("/complete", "POST /api/reconciliation/complete-run"))
        self.assertFalse(self._documented("/reject", "see /api/approvals/rejection-policy"))

    def test_a_route_documented_on_its_own_still_counts(self) -> None:
        self.assertTrue(self._documented("/api/backfill/run", "`POST /api/backfill/run` starts"))
        self.assertTrue(self._documented("/api/backfill/run", "documented as api/backfill/run."))

    def test_a_parameterised_route_is_still_credited_by_its_base(self) -> None:
        # Deliberately kept: a section describing the collection is taken to document the item
        # route. Only the *matching* changed, not this rule.
        self.assertTrue(
            self._documented("/api/backfill/schedules/{id}", "see `/api/backfill/schedules`")
        )

    def test_the_base_path_must_also_be_named(self) -> None:
        # The fallback used to substring-match the stripped base, so `/api/backfill/schedules/{id}`
        # was credited by any longer path starting the same way.
        self.assertFalse(
            self._documented("/api/backfill/schedules/{id}", "see `/api/backfill/schedules-legacy`")
        )


class ConfigBoundaryTests(_CoverageModuleTestCase):
    """`_check_config_documentation` matched the last dotted segment as a substring."""

    def _documented(self, key: str, doc_text: str) -> bool:
        self.cov._read_text_safe = lambda _path, _t=doc_text: _t
        item = self.cov.SourceItem(name=key, file_path="x.json", line=1)
        self.cov._check_config_documentation([item], Path("/nonexistent"))
        return item.documented

    def test_a_leaf_segment_is_not_the_key(self) -> None:
        # Observed: `IB.Port` counted as documented because something said "Port". Config leaves
        # are ordinary English — `Enabled`, `Timeout`, `Path` — so a leaf match asks whether a doc
        # mentions a word, not whether it documents a setting.
        self.assertFalse(self._documented("IB.Port", "bind the Port before starting"))
        self.assertFalse(self._documented("Storage.Enabled", "the feature is Enabled by default"))

    def test_the_full_key_counts(self) -> None:
        self.assertTrue(self._documented("IB.Port", "`IB.Port` defaults to 7497"))

    def test_a_key_inside_a_longer_key_is_not_the_key(self) -> None:
        self.assertFalse(self._documented("IB.Port", "see `IB.PortOverride` instead"))


class CoverageReportBoundaryTests(_CoverageModuleTestCase):
    """`generate-coverage.py` had the same substring defect on public type names."""

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

    def test_matching_stays_case_sensitive(self) -> None:
        # C# type names are case-sensitive, and this generator matched them that way before the
        # boundary fix. The sibling dashboard casefolds; this one must not, or `pricemark` in
        # prose would credit the `PriceMark` record.
        self.assertFalse(self._documented("PriceMark", "the pricemark value"))

    def test_a_named_type_is_still_documented(self) -> None:
        self.assertTrue(self._documented("PriceMark", "The `PriceMark` record carries"))
        self.assertTrue(self._documented("PriceMark", "PriceMark."))

    def test_only_the_self_referential_reports_are_excluded(self) -> None:
        # Narrow on purpose. `repository-structure.md` lists every path in the repository and
        # `documentation-coverage.md` is this generator's own output, so both would let a type
        # count as documented for existing or for being reported undocumented. Excluding the
        # whole `docs/generated/` subtree instead — which an earlier revision of this branch did —
        # dropped 41 files and marked 763 genuinely documented types as gaps.
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

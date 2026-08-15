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

    def test_non_contract_doc_roots_stay_excluded(self) -> None:
        # Prose that argues about a symbol is not documentation of it. A draft under docs/product/
        # that described no API flipped an endpoint to Documented on one explanatory sentence
        # (#2703). Pinned as a tuple so widening the corpus is a deliberate edit, not a drift.
        self.assertEqual(("docs/product",), module.NON_CONTRACT_DOC_ROOTS)

    def test_a_prose_root_is_dropped_from_the_corpus(self) -> None:
        import tempfile

        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            route = "/api/auth/accounts/{username}/password-reset"
            prose = root / "docs/product/analysis.md"
            prose.parent.mkdir(parents=True, exist_ok=True)
            prose.write_text(f"The sweep misclassifies `{route}` today.\n", encoding="utf-8")

            self.assertFalse(documents_route(route, module._load_docs_text(root)))

            reference = root / "docs/reference/auth-api.md"
            reference.parent.mkdir(parents=True, exist_ok=True)
            reference.write_text(f"### `POST {route}`\n\nResets a password.\n", encoding="utf-8")

            # The same route in a reference document still counts, so the exclusion narrows the
            # corpus rather than breaking the metric.
            self.assertTrue(documents_route(route, module._load_docs_text(root)))

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


class CoverageCorpusExclusionTests(_CoverageModuleTestCase):
    """The public-type metric shares the corpus flaw, so it takes the same exclusion (#2703)."""

    def test_prose_roots_are_excluded_from_the_type_corpus(self) -> None:
        # Ten types were credited by one draft under docs/product/, four of them appearing only
        # inside a passage arguing that those very classes are not persistent stores.
        self.assertIn("docs/product/", self.cov.DOC_CONTENT_EXCLUDE_PREFIXES)

    def test_reference_roots_stay_in_the_type_corpus(self) -> None:
        # The counterweight: an earlier over-exclusion here dropped 41 files and marked 763
        # genuinely documented types as gaps. These roots carry real reference material.
        # docs/plans/ is on this list on purpose: its credits come from blueprints whose
        # "Interface & API Contracts" sections define the contracts verbatim (#2710 review).
        for kept in ("docs/reference/", "docs/generated/database/", "docs/architecture/", "docs/plans/"):
            self.assertFalse(
                kept.startswith(self.cov.DOC_CONTENT_EXCLUDE_PREFIXES),
                f"{kept} must remain in the documentation corpus",
            )


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

    def test_a_segment_separator_binds_the_term_to_a_further_segment(self) -> None:
        # `.` is not in the boundary sets, because a trailing dot is usually the end of a sentence.
        # For segmented keys that is not enough: `IB.Port` and `IB.Port.Timeout` are different
        # settings, in both directions.
        for text in ("IB.Port.Timeout", "Parent.IB.Port", "a.IB.Port.b"):
            with self.subTest(text=text):
                self.assertFalse(self.cov._names_term(text, "IB.Port", segment_separator="."))

    def test_a_sentence_ending_separator_is_still_a_boundary(self) -> None:
        # The distinction is what lies on the far side of the dot, not the dot itself.
        for text in ("set IB.Port.", "`IB.Port` defaults to 7497", "IB.Port"):
            with self.subTest(text=text):
                self.assertTrue(self.cov._names_term(text, "IB.Port", segment_separator="."))

    def test_routes_do_not_take_a_segment_separator(self) -> None:
        # `/` is already handled asymmetrically by the boundary sets, and a dot in a route's
        # surrounding prose — a version number, a filename — must not suppress a real match.
        self.assertTrue(self.cov._names_term("v1.2 notes /api/foo", "/api/foo"))

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

    def test_a_doc_that_keeps_the_constraint_syntax_still_counts(self) -> None:
        # Both sides get normalised, or neither works. `_scan_endpoints` rewrites
        # `{projectionRunId:guid}` to `{projectionRunId}`, and seven routes in `api-reference.md`
        # are written with the constraint kept — line 270 documents
        # `/api/projections/{projectionRunId:guid}/flows` exactly. Normalising only the scan left
        # those unmatchable, and the parameter-stripped fallback then reduced the route to
        # `/api/projections/flows`, reporting a documented endpoint as a gap.
        self.assertTrue(
            self._documented(
                "/api/projections/{projectionRunId}/flows",
                "| `/api/projections/{projectionRunId:guid}/flows` | GET | Direct Lending |",
            )
        )

    def test_an_unresolved_relative_route_is_not_credited_by_a_bare_segment(self) -> None:
        # `RiskEndpoints.cs` passes its group as a method argument, which the scan cannot resolve,
        # so `/rules` and `/escalations` stay relative. Offering the slashless spelling for them
        # degraded the term to the bare word `rules`, matching the last segment of
        # `/api/risk/rules` — crediting a route the scan could not resolve, and hiding the very
        # unresolved-prefix gap group composition exists to expose.
        self.assertFalse(self._documented("/rules", "| GET | `/api/risk/rules` | List rules |"))
        self.assertFalse(
            self._documented("/escalations", "see `/api/risk/escalations` for the queue")
        )

    def test_a_full_path_still_counts_in_either_spelling(self) -> None:
        # The slashless form exists because the reference writes some routes bare.
        self.assertTrue(self._documented("/api/backfill/run", "documented as api/backfill/run."))
        self.assertTrue(self._documented("/api/backfill/run", "documented as /api/backfill/run."))

    def test_a_root_route_mapped_on_the_app_still_counts(self) -> None:
        # `/health` and `/workstation` are full paths, not fragments — `api-reference.md:650` and
        # `:690` document them as rows of their own.
        self.assertTrue(self._documented("/health", "| GET | `/health` | Health status |"))
        self.assertTrue(self._documented("/workstation", "| GET | `/workstation` | Shell entry |"))

    def test_a_root_relative_route_is_never_credited(self) -> None:
        # `DirectLendingEndpoints.cs:37` maps `/` inside a route group. Its real path is the group
        # prefix, which this scan does not resolve, so all that is left to match is a bare slash —
        # and the corpus is full of separator slashes. It was credited by `` `Spread`/`Imbalance` ``.
        # Undocumented is the honest answer: the scan cannot show that a doc names it.
        for route in ("/", "//", " / "):
            with self.subTest(route=route):
                self.assertFalse(self._documented(route, "`Spread`/`Imbalance` and / everywhere"))


class EndpointGroupCompositionTests(_CoverageModuleTestCase):
    """`_scan_endpoints` records the full route, not the fragment written under a `MapGroup`.

    Without this the boundary rule is unusable for endpoints: `api-reference.md` documents
    `/api/environment-designer/runtime/versions/{versionId}` while the scan held only
    `/runtime/versions/{versionId:guid}`, so a strict match rejected a documented endpoint. 263 of
    319 routes were relative before composition.
    """

    def _routes(self, source: str, tmp_name: str = "Endpoints.cs") -> list:
        import tempfile
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "src").mkdir()
            (root / "src" / tmp_name).write_text(source, encoding="utf-8")
            return [item.name for item in self.cov._scan_endpoints(root)]

    def test_a_group_prefix_is_composed_onto_its_children(self) -> None:
        self.assertEqual(
            ["/api/environment-designer/runtime/versions/{versionId}"],
            self._routes(
                'var group = app.MapGroup("/api/environment-designer");\n'
                'group.MapGet("/runtime/versions/{versionId:guid}", handler);\n'
            ),
        )

    def test_route_constraints_are_normalised(self) -> None:
        # `{versionId:guid}` in source is `{versionId}` in the API reference; without this the
        # composed route can never equal its own documented spelling.
        self.assertEqual(
            ["/api/x/{id}"],
            self._routes('var g = app.MapGroup("/api/x");\ng.MapGet("/{id:guid}", h);\n'),
        )

    def test_groups_nest(self) -> None:
        self.assertEqual(
            ["/api/fund-structure/reporting/packs"],
            self._routes(
                'var group = app.MapGroup("/api/fund-structure");\n'
                'var reportingGroup = group.MapGroup("/reporting");\n'
                'reportingGroup.MapGet("/packs", h);\n'
            ),
        )

    def test_an_empty_nested_group_keeps_its_parent(self) -> None:
        # `FundStructureEndpoints.cs:25` nests `group.MapGroup(string.Empty)`. Treating that as
        # unresolved would drop `/api/fund-structure` along with it.
        for empty in ('""', "string.Empty"):
            with self.subTest(empty=empty):
                self.assertEqual(
                    ["/api/fund-structure/report-packs"],
                    self._routes(
                        'var group = app.MapGroup("/api/fund-structure");\n'
                        f"var legacy = group.MapGroup({empty});\n"
                        'legacy.MapGet("/report-packs", h);\n'
                    ),
                )

    def test_a_group_mapping_the_empty_route_is_the_group_itself(self) -> None:
        # `HistoricalEndpoints.cs:23` maps `""`, meaning the group's own path.
        self.assertEqual(
            ["/api/historical"],
            self._routes('var g = app.MapGroup("/api/historical");\ng.MapGet("", h);\n'),
        )

    def test_the_nearest_preceding_declaration_wins(self) -> None:
        # `HistoricalEndpoints.cs` binds `var group` twice — `/api/historical` at line 20 and `""`
        # at line 173. Keying prefixes by name alone let the second claim the first's endpoints.
        self.assertEqual(
            ["/api/historical/symbols", "/alignment"],
            self._routes(
                'var group = app.MapGroup("/api/historical");\n'
                'group.MapGet("/symbols", h);\n'
                'var group = app.MapGroup("");\n'
                'group.MapGet("/alignment", h);\n'
            ),
        )

    def test_a_route_mapped_on_the_app_takes_no_prefix(self) -> None:
        self.assertEqual(
            ["/health"],
            self._routes('var g = app.MapGroup("/api/x");\napp.MapGet("/health", h);\n'),
        )

    def test_an_unresolved_group_constant_leaves_the_route_relative(self) -> None:
        # Mis-composing is worse than not composing: a wrong prefix silently reports a route that
        # does not exist. The group is skipped and the child keeps its own path.
        self.assertEqual(
            ["/escalations"],
            self._routes('var g = app.MapGroup(SomeUnknownPrefix);\ng.MapGet("/escalations", h);\n'),
        )


class ProviderBoundaryTests(_CoverageModuleTestCase):
    """`_check_provider_documentation` carried the substring defect latently.

    The scan finds no providers in this repository, so regeneration exercises none of this — the
    short-name split, the case folding, or the boundary check. Tested directly for that reason:
    a category that cannot move is a category whose defects stay invisible.
    """

    def _documented(self, provider: str, doc_text: str) -> bool:
        self.cov._read_text_safe = lambda _path, _t=doc_text: _t
        item = self.cov.SourceItem(name=provider, file_path="x.cs", line=1)
        self.cov._check_provider_documentation([item], Path("/nonexistent"))
        return item.documented

    def test_an_exact_provider_name_counts_regardless_of_case(self) -> None:
        # Provider docs use prose capitalisation, so this check folds case where the type-name
        # check does not.
        self.assertTrue(self._documented("Streaming/Alpaca", "The Alpaca adapter streams quotes."))
        self.assertTrue(self._documented("Streaming/alpaca", "Configure ALPACA credentials."))

    def test_a_provider_inside_a_longer_name_is_not_documented(self) -> None:
        self.assertFalse(self._documented("Streaming/Alpaca", "see AlpacaCrypto for the venue"))
        self.assertFalse(self._documented("Historical/Polygon", "the PolygonIo client retries"))

    def test_only_the_short_name_is_matched(self) -> None:
        # `Streaming/Alpaca` is documented by naming `Alpaca`; the directory prefix is scaffolding.
        self.assertTrue(self._documented("Streaming/Alpaca", "Alpaca is supported."))


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

    def test_a_dotted_superset_is_a_different_key(self) -> None:
        # Both directions. `IB.Port` is not documented by a doc describing `IB.Port.Timeout`, nor
        # by one describing `Parent.IB.Port` — each is its own setting and is scanned on its own.
        self.assertFalse(self._documented("IB.Port", "`IB.Port.Timeout` controls the wait"))
        self.assertFalse(self._documented("IB.Port", "override with `Parent.IB.Port`"))

    def test_a_key_ending_a_sentence_still_counts(self) -> None:
        self.assertTrue(self._documented("IB.Port", "The gateway listens on IB.Port."))


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

    def test_the_corpus_excludes_self_referential_reports_and_prose_roots(self) -> None:
        # Two separate reasons, both narrow on purpose.
        #
        # Self-referential: `repository-structure.md` lists every path in the repository and
        # `documentation-coverage.md` is this generator's own output, so both would let a type
        # count as documented for existing or for being reported undocumented. Excluding the
        # whole `docs/generated/` subtree instead — which an earlier revision of this branch did —
        # dropped 41 files and marked 763 genuinely documented types as gaps.
        #
        # Prose: `docs/product/` and `docs/plans/` argue and plan rather than describe contracts,
        # so a type named there is mentioned rather than documented (#2703).
        self.assertEqual(
            (
                "docs/status/",
                "docs/generated/documentation-coverage.md",
                "docs/generated/repository-structure.md",
                "docs/product/",
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

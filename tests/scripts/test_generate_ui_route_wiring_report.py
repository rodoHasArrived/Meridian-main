#!/usr/bin/env python3
"""Regression tests for the UI route wiring report.

The analyzer's value depends entirely on resolving both sides correctly: a missed
`MapGroup` prefix invents routes that no server ever exposes, and a missed call
site reports a wired route as dead work. Each test below pins one resolution rule
that a real endpoint or registry file in this repository depends on.
"""

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

# Lives in tests/scripts rather than beside the generator: only this directory is
# discovered by build/scripts/ci/run-script-tests.py, so a suite anywhere else runs
# in no CI lane at all.
ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "build" / "scripts" / "docs"
MODULE_PATH = SCRIPTS / "generate-ui-route-wiring-report.py"

if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

spec = importlib.util.spec_from_file_location("generate_ui_route_wiring_report", MODULE_PATH)
assert spec is not None and spec.loader is not None
wiring = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = wiring
spec.loader.exec_module(wiring)


class NormalizeTests(unittest.TestCase):
    def test_route_parameters_and_query_strings_collapse(self) -> None:
        self.assertEqual(wiring.normalize("/api/loans/{loanId:guid}/fees"), "/api/loans/{}/fees")
        self.assertEqual(wiring.normalize("/api/backfill/status?symbol=SPY"), "/api/backfill/status")
        self.assertEqual(wiring.normalize("/api/status/"), "/api/status")

    def test_unresolved_call_site_segment_stays_distinct_from_a_parameter(self) -> None:
        # A parameter matches only a parameter; an unresolved interpolation may also
        # stand for a literal segment such as `/seek`.
        self.assertEqual(wiring.normalize(f"/api/replay/{{id}}/{wiring.UNKNOWN_SEGMENT}"), "/api/replay/{}/*")


class RouteMatchingTests(unittest.TestCase):
    def test_wildcard_call_site_matches_a_literal_backend_segment(self) -> None:
        called = {"/api/replay/{}/*"}
        index = wiring.index_called(called)
        self.assertTrue(wiring.route_is_called("/api/replay/{}/seek", called, index))

    def test_parameter_does_not_match_an_unrelated_literal(self) -> None:
        called = {"/api/maintenance/schedules/{}"}
        index = wiring.index_called(called)
        self.assertFalse(wiring.route_is_called("/api/maintenance/presets", called, index))


class CommentStrippingTests(unittest.TestCase):
    def test_route_named_in_a_doc_comment_is_not_a_call_site(self) -> None:
        source = '/** Mirrors `/api/maintenance/*`. */\nconst path = "/api/maintenance/status";\n'
        stripped = wiring.strip_comments(source)
        self.assertNotIn("/api/maintenance/*", stripped)
        self.assertIn('"/api/maintenance/status"', stripped)

    def test_line_comments_are_removed_without_shifting_following_code(self) -> None:
        stripped = wiring.strip_comments('// GET /api/status\nconst x = "/api/config";')
        self.assertNotIn("GET /api/status", stripped)
        self.assertIn('"/api/config"', stripped)


class TemplateResolutionTests(unittest.TestCase):
    def test_interpolation_containing_an_object_literal_resolves(self) -> None:
        symbols = {"MAINTENANCE_API_ENDPOINTS.executions": {"/api/maintenance/executions"}}
        resolved = wiring._resolve_ts_expression(
            "`${MAINTENANCE_API_ENDPOINTS.executions}${queryString({ limit })}`", {}, symbols)
        self.assertEqual(resolved, {"/api/maintenance/executions"})

    def test_ternary_helper_resolves_to_both_paths(self) -> None:
        symbols = {"WORKSTATION_API_ENDPOINTS.workflowPresets": {"/api/workstation/workflows/presets"}}
        resolved = wiring._resolve_ts_expression(
            "presetId\n ? `${WORKSTATION_API_ENDPOINTS.workflowPresets}/${pathSegment(presetId, \"presetId\")}`"
            "\n : WORKSTATION_API_ENDPOINTS.workflowPresets",
            {}, symbols)
        self.assertEqual(
            {wiring.normalize(path) for path in resolved},
            {"/api/workstation/workflows/presets", "/api/workstation/workflows/presets/*"})


class BackendResolutionTests(unittest.TestCase):
    """Resolution against the repository's own endpoint sources."""

    @classmethod
    def setUpClass(cls) -> None:
        cls.constants = wiring.load_route_constants()
        cls.inventory, cls.unresolved = wiring.collect_backend_routes(cls.constants)
        cls.paths = {route["path"] for route in cls.inventory}

    def test_group_prefixes_are_applied(self) -> None:
        self.assertIn("/api/execution/orders", self.paths)
        self.assertIn("/api/fund-accounts/", self.paths)

    def test_prefix_handed_to_a_helper_through_a_group_parameter_is_applied(self) -> None:
        # FundStructureEndpoints passes `reportingGroup` to the tombstone mapper in
        # another file of the same partial class.
        self.assertIn("/api/fund-structure/reporting/packs", self.paths)
        self.assertNotIn("/packs", self.paths)

    def test_group_variables_are_scoped_to_the_method_that_declares_them(self) -> None:
        # HistoricalEndpoints declares `var group` twice with different prefixes.
        self.assertIn("/api/historical/symbols", self.paths)
        self.assertNotIn("/symbols", self.paths)

    def test_subroute_helpers_do_not_double_the_group_prefix(self) -> None:
        self.assertIn("/api/workstation/family-office/overview", self.paths)
        self.assertNotIn("/api/workstation/api/workstation/family-office/overview", self.paths)

    def test_every_mapped_route_resolves_to_an_absolute_path(self) -> None:
        self.assertTrue(all(route["path"].startswith("/") for route in self.inventory))

    def test_unresolved_route_expressions_stay_within_a_known_ceiling(self) -> None:
        # Growth here means a new route-building idiom the analyzer cannot fold,
        # which silently drops routes from the report.
        self.assertLessEqual(len(self.unresolved), 5, self.unresolved)


class ExclusionTests(unittest.TestCase):
    def test_probes_and_webhooks_carry_a_stated_reason(self) -> None:
        self.assertIsNotNone(wiring.excluded_reason("/healthz"))
        self.assertIsNotNone(
            wiring.excluded_reason("/hooks/reporting/distribution/{transportId}/deliveries/{jobId}/receipts"))

    def test_operator_routes_are_not_excluded(self) -> None:
        self.assertIsNone(wiring.excluded_reason("/api/workstation/family-office/overview"))
        self.assertIsNone(wiring.excluded_reason("/api/maintenance/status"))


if __name__ == "__main__":
    unittest.main()

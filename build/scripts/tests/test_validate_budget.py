"""
Tests for build/scripts/validate_budget.py
Run with: python3 -m pytest build/scripts/tests/test_validate_budget.py -v
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path

import pytest

# Allow importing validate_budget from the scripts directory
sys.path.insert(0, str(Path(__file__).parent.parent))
import validate_budget as vb


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

SAMPLE_BUDGETS = [
    {
        "stage_name": "DedupKey_CacheHit",
        "max_allocated_bytes_per_event": 0,
        "max_mean_nanos_per_event": 200,
        "requires_simd": False,
    },
    {
        "stage_name": "DedupKey_CacheMiss",
        "max_allocated_bytes_per_event": 256,
        "max_mean_nanos_per_event": 800,
        "requires_simd": False,
    },
    {
        "stage_name": "NewlineScan_Avx2",
        "max_allocated_bytes_per_event": 0,
        "max_mean_nanos_per_event": 20,
        "requires_simd": True,
    },
]

SAMPLE_BDN_REPORT = {
    "Benchmarks": [
        {
            "FullName": "Meridian.Benchmarks.DeduplicationKeyBenchmarks.IsDuplicate_CacheHit",
            "Statistics": {"Mean": 150.0},
            "Memory": {"BytesAllocatedPerOperation": 0},
        },
        {
            "FullName": "Meridian.Benchmarks.DeduplicationKeyBenchmarks.ComputeKey_CacheMiss",
            "Statistics": {"Mean": 600.0},
            "Memory": {"BytesAllocatedPerOperation": 128},
        },
    ]
}


@pytest.fixture
def budget_file(tmp_path):
    f = tmp_path / "perf-budgets.json"
    f.write_text(json.dumps(SAMPLE_BUDGETS), encoding="utf-8")
    return str(f)


@pytest.fixture
def results_dir_clean(tmp_path):
    d = tmp_path / "results"
    d.mkdir()
    report = d / "Meridian.Benchmarks.DeduplicationKeyBenchmarks-report-full.json"
    report.write_text(json.dumps(SAMPLE_BDN_REPORT), encoding="utf-8")
    return str(d)


@pytest.fixture
def results_dir_violation(tmp_path):
    """Results where DedupKey_CacheHit allocates 64 bytes (over zero budget)."""
    d = tmp_path / "results"
    d.mkdir()
    report_data = {
        "Benchmarks": [
            {
                "FullName": "Meridian.Benchmarks.DeduplicationKeyBenchmarks.IsDuplicate_CacheHit",
                "Statistics": {"Mean": 150.0},
                "Memory": {"BytesAllocatedPerOperation": 64},  # over budget (budget=0)
            },
        ]
    }
    report = d / "Meridian.Benchmarks.DeduplicationKeyBenchmarks-report-full.json"
    report.write_text(json.dumps(report_data), encoding="utf-8")
    return str(d)


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

class TestLoadBudgets:
    def test_parses_json_correctly(self, budget_file):
        budgets = vb.load_budgets(budget_file)

        assert "DedupKey_CacheHit" in budgets
        hit = budgets["DedupKey_CacheHit"]
        assert hit.max_allocated_bytes_per_event == 0
        assert hit.max_mean_nanos_per_event == 200
        assert hit.requires_simd is False

        miss = budgets["DedupKey_CacheMiss"]
        assert miss.max_allocated_bytes_per_event == 256

    def test_missing_file_exits_with_code_2(self, tmp_path):
        with pytest.raises(SystemExit) as exc:
            vb.load_budgets(str(tmp_path / "nonexistent.json"))
        assert exc.value.code == 2

    def test_invalid_json_exits_with_code_2(self, tmp_path):
        f = tmp_path / "bad.json"
        f.write_text("not valid json", encoding="utf-8")
        with pytest.raises(SystemExit) as exc:
            vb.load_budgets(str(f))
        assert exc.value.code == 2


class TestLoadBdnResults:
    def test_parses_results_correctly(self, results_dir_clean):
        results = vb.load_bdn_results(results_dir_clean)
        assert len(results) == 2
        hit = next(r for r in results if "CacheHit" in r.method_name)
        assert hit.allocated_bytes == 0
        assert hit.mean_ns == 150.0

    def test_missing_dir_exits_with_code_2(self, tmp_path):
        # A harness that produced nothing must not be reported as a clean run.
        with pytest.raises(SystemExit) as exc:
            vb.load_bdn_results(str(tmp_path / "no_such_dir"))
        assert exc.value.code == 2

    def test_empty_results_dir_exits_with_code_2(self, tmp_path):
        d = tmp_path / "results"
        d.mkdir()
        with pytest.raises(SystemExit) as exc:
            vb.load_bdn_results(str(d))
        assert exc.value.code == 2

    def test_report_without_benchmarks_exits_with_code_2(self, tmp_path):
        d = tmp_path / "results"
        d.mkdir()
        (d / "Empty-report-full.json").write_text(json.dumps({"Benchmarks": []}), encoding="utf-8")
        with pytest.raises(SystemExit) as exc:
            vb.load_bdn_results(str(d))
        assert exc.value.code == 2

    def test_entry_without_statistics_is_not_treated_as_measured(self, tmp_path):
        # A failed or incomplete BenchmarkDotNet entry carries no Mean/allocation. Defaulting
        # those to zero would present it as an instantaneous, zero-allocation success that
        # satisfies every budget.
        d = tmp_path / "results"
        d.mkdir()
        (d / "Partial-report-full.json").write_text(
            json.dumps(
                {
                    "Benchmarks": [
                        {"FullName": "Meridian.Benchmarks.X.IsDuplicate_CacheHit"},
                        {
                            "FullName": "Meridian.Benchmarks.X.ComputeKey_CacheMiss",
                            "Statistics": {"Mean": 600.0},
                            "Memory": {"BytesAllocatedPerOperation": 128},
                        },
                    ]
                }
            ),
            encoding="utf-8",
        )

        results = vb.load_bdn_results(str(d))

        assert [r.method_name for r in results] == ["Meridian.Benchmarks.X.ComputeKey_CacheMiss"]

    def test_entry_missing_only_allocation_is_not_treated_as_measured(self, tmp_path):
        d = tmp_path / "results"
        d.mkdir()
        (d / "Partial-report-full.json").write_text(
            json.dumps(
                {
                    "Benchmarks": [
                        {
                            "FullName": "Meridian.Benchmarks.X.IsDuplicate_CacheHit",
                            "Statistics": {"Mean": 150.0},
                        },
                        {
                            "FullName": "Meridian.Benchmarks.X.ComputeKey_CacheMiss",
                            "Statistics": {"Mean": 600.0},
                            "Memory": {"BytesAllocatedPerOperation": 128},
                        },
                    ]
                }
            ),
            encoding="utf-8",
        )

        results = vb.load_bdn_results(str(d))

        assert [r.method_name for r in results] == ["Meridian.Benchmarks.X.ComputeKey_CacheMiss"]

    def test_incomplete_entry_makes_its_budget_unmeasured(self, budget_file, tmp_path):
        d = tmp_path / "results"
        d.mkdir()
        (d / "Partial-report-full.json").write_text(
            json.dumps(
                {
                    "Benchmarks": [
                        {"FullName": "Meridian.Benchmarks.X.IsDuplicate_CacheHit"},
                        {
                            "FullName": "Meridian.Benchmarks.X.ComputeKey_CacheMiss",
                            "Statistics": {"Mean": 600.0},
                            "Memory": {"BytesAllocatedPerOperation": 128},
                        },
                    ]
                }
            ),
            encoding="utf-8",
        )
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(str(d))

        assert vb.find_unmeasured_budgets(budgets, results) == ["DedupKey_CacheHit"]


class TestUnmeasuredBudgets:
    def test_all_budgets_measured_reports_none(self, budget_file, results_dir_clean):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_clean)

        assert vb.find_unmeasured_budgets(budgets, results) == []

    def test_budget_without_a_matching_result_is_reported(self, budget_file, results_dir_violation):
        # results_dir_violation only contains a CacheHit row, so CacheMiss is unmeasured.
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_violation)

        assert vb.find_unmeasured_budgets(budgets, results) == ["DedupKey_CacheMiss"]

    def test_simd_budgets_are_not_reported_as_unmeasured(self, budget_file, results_dir_clean):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_clean)

        assert "NewlineScan_Avx2" not in vb.find_unmeasured_budgets(budgets, results)

    def test_declared_stage_is_not_reported(self, budget_file, results_dir_violation):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_violation)

        unmeasured = vb.find_unmeasured_budgets(budgets, results, {"DedupKey_CacheMiss"})

        assert unmeasured == []

    def test_summary_marks_unmeasured_budgets(self, budget_file, results_dir_violation):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_violation)
        violations = vb.check_budgets(budgets, results)
        unmeasured = vb.find_unmeasured_budgets(budgets, results)

        summary = vb.render_summary(violations, budgets, results, unmeasured)

        assert "No benchmark measured this budget" in summary

    def test_summary_shows_measured_value_for_passing_budget(self, budget_file, results_dir_clean):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_clean)
        violations = vb.check_budgets(budgets, results)

        summary = vb.render_summary(violations, budgets, results, [])

        # DedupKey_CacheMiss measured 128 bytes against a 256-byte budget; the previous
        # report printed an em dash for every passing row, hiding the headroom.
        assert "| 128 |" in summary

    def test_evidence_records_measured_and_unmeasured_stages(self, budget_file, results_dir_violation):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_violation)
        violations = vb.check_budgets(budgets, results)
        unmeasured = vb.find_unmeasured_budgets(budgets, results)

        evidence = vb.build_evidence(budgets, results, violations, unmeasured)

        assert evidence["unmeasured_stages"] == ["DedupKey_CacheMiss"]
        by_stage = {stage["stage_name"]: stage for stage in evidence["stages"]}
        assert by_stage["DedupKey_CacheHit"]["status"] == "violation"
        assert by_stage["DedupKey_CacheHit"]["actual_allocated_bytes"] == 64
        assert by_stage["DedupKey_CacheMiss"]["measured"] is False
        assert by_stage["NewlineScan_Avx2"]["status"] == "simd-excluded"

    def test_evidence_counts_a_waived_budget_separately_from_a_measured_one(
        self, budget_file, results_dir_violation
    ):
        # --allow-unmeasured suppresses the failure, not the coverage gap. Reporting the waived
        # stage nowhere made unmeasured_count 0, so a release reviewer read a waiver as coverage.
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_violation)
        violations = vb.check_budgets(budgets, results)
        unmeasured = vb.find_unmeasured_budgets(budgets, results, {"DedupKey_CacheMiss"})

        evidence = vb.build_evidence(budgets, results, violations, unmeasured)

        assert evidence["unmeasured_count"] == 0
        assert evidence["waived_unmeasured_stages"] == ["DedupKey_CacheMiss"]
        assert evidence["waived_unmeasured_count"] == 1
        assert evidence["measured_count"] < evidence["budget_count"]

    def test_summary_does_not_claim_full_coverage_when_a_budget_is_waived(self, budget_file, tmp_path):
        # CacheHit measured and within its budget, CacheMiss absent but waived. The lane has no
        # violation and no failing unmeasured budget, which is exactly when the old summary
        # printed "All budgets measured and within limits" over a stage nothing measured.
        results_dir = tmp_path / "results"
        results_dir.mkdir()
        (results_dir / "Meridian.Benchmarks.DeduplicationKeyBenchmarks-report-full.json").write_text(
            json.dumps(
                {
                    "Benchmarks": [
                        {
                            "FullName": "Meridian.Benchmarks.DeduplicationKeyBenchmarks.IsDuplicate_CacheHit",
                            "Statistics": {"Mean": 150.0},
                            "Memory": {"BytesAllocatedPerOperation": 0},
                        },
                    ]
                }
            ),
            encoding="utf-8",
        )

        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(str(results_dir))
        violations = vb.check_budgets(budgets, results)
        unmeasured = vb.find_unmeasured_budgets(budgets, results, {"DedupKey_CacheMiss"})
        waived = vb.find_waived_budgets(budgets, results, {"DedupKey_CacheMiss"})

        summary = vb.render_summary(violations, budgets, results, unmeasured)

        assert violations == []
        assert unmeasured == []
        assert waived == ["DedupKey_CacheMiss"]
        assert "All budgets measured and within limits" not in summary
        assert "waived with `--allow-unmeasured`" in summary


class TestAmbiguousStageMatching:
    def test_one_result_does_not_satisfy_every_sibling_budget(self):
        # WalChecksumBenchmarks.Small shares the WalChecksum token with all three budgets, so
        # the loose fallback let a run where two benchmarks never executed report no unmeasured
        # stages — defeating the gate this module exists to provide.
        stages = ["WalChecksum_Small", "WalChecksum_Medium_1KB", "WalChecksum_Large_4KB"]
        results = [
            vb.BdnResult(
                method_name="Meridian.Benchmarks.WalChecksumBenchmarks.Small",
                mean_ns=100.0,
                allocated_bytes=0,
            )
        ]

        # Small is the stage this result actually describes, so it counts; the siblings it only
        # shares a family token with do not.
        assert vb.best_result_for("WalChecksum_Small", results, stages) is not None
        assert vb.best_result_for("WalChecksum_Medium_1KB", results, stages) is None
        assert vb.best_result_for("WalChecksum_Large_4KB", results, stages) is None

        # Without the sibling set the old behaviour stands: every sibling claims the row.
        for stage in stages:
            assert vb.best_result_for(stage, results) is not None, stage

    def test_a_complete_family_run_is_not_reported_unmeasured(self):
        # Rejecting every shared-token row was the opposite error: a real
        # NewlineScanBenchmarks.SearchValues_Portable result describes NewlineScan_Portable
        # exactly and NewlineScan_Avx2 only incidentally, so discarding it for both turned a
        # fully measured lane into a failing one under --fail-on-violation.
        stages = ["NewlineScan_Portable", "NewlineScan_Avx2"]
        results = [
            vb.BdnResult(
                method_name="Meridian.Benchmarks.NewlineScanBenchmarks.SearchValues_Portable",
                mean_ns=40.0,
                allocated_bytes=0,
            )
        ]

        assert vb.best_result_for("NewlineScan_Portable", results, stages) is not None
        assert vb.best_result_for("NewlineScan_Avx2", results, stages) is None

    def test_an_abbreviated_stage_name_still_matches_its_benchmark(self):
        # The fallback exists for exactly this: DedupKey vs DeduplicationKey. Tightening it to
        # require every token would have broken this case, so ambiguity is rejected instead.
        stages = ["DedupKey_CacheHit", "DedupKey_CacheMiss"]
        results = [
            vb.BdnResult(
                method_name="Meridian.Benchmarks.DeduplicationKeyBenchmarks.IsDuplicate_CacheHit",
                mean_ns=150.0,
                allocated_bytes=64,
            )
        ]

        assert vb.best_result_for("DedupKey_CacheHit", results, stages) is not None
        assert vb.best_result_for("DedupKey_CacheMiss", results, stages) is None


class TestCheckBudgets:
    def test_no_violations_when_within_limits(self, budget_file, results_dir_clean):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_clean)
        violations = vb.check_budgets(budgets, results)
        assert violations == []

    def test_returns_violation_when_over_limit(self, budget_file, results_dir_violation):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_violation)
        violations = vb.check_budgets(budgets, results)
        assert len(violations) == 1
        assert violations[0].stage_name == "DedupKey_CacheHit"
        assert violations[0].allocated_over_by == 64

    def test_simd_budgets_are_skipped(self, budget_file, results_dir_clean):
        budgets = vb.load_budgets(budget_file)
        # Add a SIMD-only result that would violate the budget if checked
        results = vb.load_bdn_results(results_dir_clean) + [
            vb.BdnResult(
                method_name="Meridian.Benchmarks.NewlineScanBenchmarks.Avx2_VectorNewlineScan",
                mean_ns=100.0,
                allocated_bytes=999_999,  # wildly over budget, but must be skipped
            )
        ]
        violations = vb.check_budgets(budgets, results)
        # The SIMD entry must NOT appear in violations
        assert not any(v.stage_name == "NewlineScan_Avx2" for v in violations)


class TestRenderSummary:
    def test_produces_markdown_table(self, budget_file, results_dir_clean):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_clean)
        violations = vb.check_budgets(budgets, results)
        summary = vb.render_summary(violations, budgets, results, [])
        assert "|" in summary
        assert "DedupKey_CacheHit" in summary
        assert "✅ Pass" in summary

    def test_violation_rows_marked_with_cross(self, budget_file, results_dir_violation):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_violation)
        violations = vb.check_budgets(budgets, results)
        unmeasured = vb.find_unmeasured_budgets(budgets, results)
        summary = vb.render_summary(violations, budgets, results, unmeasured)
        assert "❌" in summary
        assert "DedupKey_CacheHit" in summary


class TestExitCodes:
    def test_exit_0_on_clean(self, budget_file, results_dir_clean, monkeypatch):
        monkeypatch.setattr(sys, "argv", [
            "validate_budget.py",
            "--bdn-results", results_dir_clean,
            "--budget-json", budget_file,
            "--fail-on-violation",
        ])
        assert vb.main() == 0

    def test_exit_1_on_violation_with_flag(self, budget_file, results_dir_violation, monkeypatch):
        monkeypatch.setattr(sys, "argv", [
            "validate_budget.py",
            "--bdn-results", results_dir_violation,
            "--budget-json", budget_file,
            "--fail-on-violation",
        ])
        assert vb.main() == 1

    def test_exit_1_when_a_budget_is_unmeasured(self, budget_file, tmp_path, monkeypatch):
        # A run that measured only the SIMD-excluded stage leaves both real budgets
        # unmeasured. Before this gate that reported green.
        results_dir = tmp_path / "partial"
        results_dir.mkdir()
        (results_dir / "Partial-report-full.json").write_text(
            json.dumps(
                {
                    "Benchmarks": [
                        {
                            "FullName": "Meridian.Benchmarks.NewlineScanBenchmarks.NewlineScan_Avx2",
                            "Statistics": {"Mean": 10.0},
                            "Memory": {"BytesAllocatedPerOperation": 0},
                        }
                    ]
                }
            ),
            encoding="utf-8",
        )
        monkeypatch.setattr(sys, "argv", [
            "validate_budget.py",
            "--bdn-results", str(results_dir),
            "--budget-json", budget_file,
            "--fail-on-violation",
        ])
        assert vb.main() == 1

    def test_exit_2_when_no_results_exist(self, budget_file, tmp_path, monkeypatch):
        monkeypatch.setattr(sys, "argv", [
            "validate_budget.py",
            "--bdn-results", str(tmp_path / "never_ran"),
            "--budget-json", budget_file,
            "--fail-on-violation",
        ])
        with pytest.raises(SystemExit) as exc:
            vb.main()
        assert exc.value.code == 2

    def test_json_output_is_written(self, budget_file, results_dir_clean, tmp_path, monkeypatch):
        output = tmp_path / "evidence" / "budget.json"
        monkeypatch.setattr(sys, "argv", [
            "validate_budget.py",
            "--bdn-results", results_dir_clean,
            "--budget-json", budget_file,
            "--fail-on-violation",
            "--json-output", str(output),
        ])
        assert vb.main() == 0

        evidence = json.loads(output.read_text(encoding="utf-8"))
        assert evidence["violation_count"] == 0
        assert evidence["unmeasured_count"] == 0
        assert evidence["benchmark_result_rows"] == 2


class TestNonFiniteMeasurements:
    """A crashed run can emit NaN, which reads as measured *and* passing."""

    @pytest.mark.parametrize("bad", [float("nan"), float("inf"), float("-inf"), -1.0, True])
    def test_non_finite_or_negative_values_are_not_measurements(self, bad):
        assert vb._is_measurement(bad) is False

    @pytest.mark.parametrize("good", [0, 0.0, 1, 256.9])
    def test_real_numbers_are_measurements(self, good):
        assert vb._is_measurement(good) is True

    def test_json_parses_nan_so_the_guard_must_catch_it(self):
        # Python's json module accepts the non-standard NaN token, and isinstance(nan, float) is
        # True — that is how a crashed benchmark reached the comparison in the first place.
        assert vb._is_measurement(json.loads('{"Mean": NaN}')["Mean"]) is False

    def test_a_nan_row_is_treated_as_unmeasured(self, tmp_path, capsys):
        d = tmp_path / "results"
        d.mkdir()
        (d / "Meridian.Benchmarks.NaNBenchmarks-report-full.json").write_text(
            '{"Benchmarks": [{"FullName": "DedupKey_CacheHit", '
            '"Statistics": {"Mean": NaN}, "Memory": {"BytesAllocatedPerOperation": NaN}}, '
            '{"FullName": "DedupKey_Real", "Statistics": {"Mean": 10.0}, '
            '"Memory": {"BytesAllocatedPerOperation": 0}}]}',
            encoding="utf-8",
        )

        rows = vb.load_bdn_results(str(d))

        # Every comparison against NaN is False, so leaving the row in reported the stage as
        # measured and comfortably within budget, and wrote NaN into the evidence file.
        assert [r.method_name for r in rows] == ["DedupKey_Real"]


class TestPrefixRelatedSiblingBudgets:
    """`Bench.ParseTrade` contains both `Parse` and `Parse_Trade`."""

    RESULT = [vb.BdnResult(method_name="Bench.ParseTrade", mean_ns=1.0, allocated_bytes=0.0)]
    STAGES = ["Parse", "Parse_Trade"]

    def test_the_shorter_prefix_sibling_stays_unmeasured(self):
        # Crediting one result to both stages let the never-executed `Parse` benchmark read as
        # measured and slip past the unmeasured-budget gate.
        assert vb.best_result_for("Parse", self.RESULT, self.STAGES) is None

    def test_the_stage_the_result_describes_still_claims_it(self):
        best = vb.best_result_for("Parse_Trade", self.RESULT, self.STAGES)

        assert best is not None and best.method_name == "Bench.ParseTrade"

    def test_without_sibling_context_behaviour_is_unchanged(self):
        assert vb.best_result_for("Parse", self.RESULT) is not None

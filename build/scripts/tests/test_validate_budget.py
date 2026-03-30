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

    def test_returns_empty_for_missing_dir(self, tmp_path):
        results = vb.load_bdn_results(str(tmp_path / "no_such_dir"))
        assert results == []


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
        summary = vb.render_summary(violations, budgets)
        assert "|" in summary
        assert "DedupKey_CacheHit" in summary
        assert "✅ Pass" in summary

    def test_violation_rows_marked_with_cross(self, budget_file, results_dir_violation):
        budgets = vb.load_budgets(budget_file)
        results = vb.load_bdn_results(results_dir_violation)
        violations = vb.check_budgets(budgets, results)
        summary = vb.render_summary(violations, budgets)
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

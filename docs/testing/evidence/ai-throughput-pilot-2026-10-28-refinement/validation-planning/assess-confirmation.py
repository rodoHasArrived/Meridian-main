"""Recompute the frozen planning threshold from the retained per-trial report."""
import json
from pathlib import Path
import statistics

base = Path(__file__).resolve().parent
protocol = json.loads((base / "confirmation-protocol.json").read_text())
report = json.loads((base / "confirmation-report.json").read_text())
rule = protocol["successRule"]
expected = {task["pair"].replace("vp-", "vc-", 1) for task in protocol["tasks"]}
pairs = {}
for trial in report["trials"]:
    pair = pairs.setdefault(trial["pair"], {})
    assert trial["cohort"] not in pair, "Duplicate cohort"
    pair[trial["cohort"]] = trial
assert set(pairs) == expected, "Missing or unexpected task pair"
rows = []
for name, pair in sorted(pairs.items()):
    assert set(pair) == {"baseline", "treatment"}, "Missing cohort"
    before, after = pair["baseline"], pair["treatment"]
    b, t = before["validatedCompletionSeconds"], after["validatedCompletionSeconds"]
    rows.append({"pair": name, "baselineSeconds": b, "treatmentSeconds": t,
                 "reductionFraction": (b - t) / b if b and t is not None else None,
                 "baselineCorrections": before["correctionCycles"],
                 "treatmentCorrections": after["correctionCycles"],
                 "baselineValidations": before["validationCounts"],
                 "treatmentValidations": after["validationCounts"]})
eligible = [row for row in rows if row["reductionFraction"] is not None]
all_pass = len(report["trials"]) == 2 * rule["pairCount"] and all(
    trial["comparisonEligible"] for trial in report["trials"])
faster = sum(row["reductionFraction"] >= rule["minimumPairReductionFraction"] for row in eligible)
median = statistics.median(row["reductionFraction"] for row in eligible) if eligible else None
corrections_known = all(type(row[key]) is int for row in rows
                        for key in ("baselineCorrections", "treatmentCorrections"))
no_regression = corrections_known and sum(row["treatmentCorrections"] for row in rows) <= sum(
    row["baselineCorrections"] for row in rows)
passed = all_pass and faster >= rule["minimumFasterPairs"] and median is not None and (
    median >= rule["minimumMedianReductionFraction"]) and no_regression
sensitivity = [row for row in eligible if row["pair"] != "vc-pair-4"]
assessment = {
    "result": "threshold-met" if passed else "threshold-not-met", "allTwelveAccepted": all_pass,
    "eligiblePairs": len(eligible), "pairsAtLeast10PercentFaster": faster,
    "medianPairedReductionFraction": median, "noTreatmentCorrectionRegression": no_regression,
    "rows": rows,
    "interpretation": "Only local validation-planning time, not code implementation or passing PR time. All failed attempts and repair times retained.",
    "capacityInterruption": "vc-pair-4-baseline resumed same agent/log after capacity error; see sensitivity below.",
    "sensitivityWithoutCapacityPair": {
        "pairs": len(sensitivity),
        "pairsAtLeast10PercentFaster": sum(row["reductionFraction"] >= rule["minimumPairReductionFraction"]
                                          for row in sensitivity),
        "medianPairedReductionFraction": statistics.median(row["reductionFraction"] for row in sensitivity)
        if sensitivity else None}}
(base / "confirmation-assessment.json").write_text(json.dumps(assessment, indent=2) + "\n")
print(assessment["result"])

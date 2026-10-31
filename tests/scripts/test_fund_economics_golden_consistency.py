"""Internal-consistency checks for the fund-economics golden worked examples.

Recomputes the closed-form figures and checks conservation (distribution tiers sum to the
distributed amount, LP + GP totals match, cumulative totals match) directly from the JSON,
independent of the C#/F# kernels. This validates the fixture, not Meridian behavior; the
kernels are asserted against it by FundEconomicsGoldenWorkedExampleTests (needs dotnet).
"""
from __future__ import annotations

import json
import unittest
from decimal import ROUND_HALF_EVEN, Decimal
from pathlib import Path

FIXTURE = (
    Path(__file__).resolve().parents[1]
    / "fixtures" / "fund-economics" / "golden" / "fund-economics-worked-examples.json"
)
CENT = Decimal("0.01")
TIER_KEYS = ("returnOfCapital", "preferredReturn", "gpCatchUp", "lpCatchUp", "lpCarry", "gpCarry")


def dec(value) -> Decimal:
    return Decimal(str(value))


def cents(value: Decimal) -> Decimal:
    return value.quantize(CENT, rounding=ROUND_HALF_EVEN)


class FundEconomicsGoldenConsistencyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.data = json.loads(FIXTURE.read_text(encoding="utf-8"))

    def test_management_fee_accruals_match_closed_form(self) -> None:
        for case in self.data["managementFeeAccruals"]:
            with self.subTest(case=case["id"]):
                expected = dec(case["netAssets"]) * dec(case["annualRate"]) * case["accrualDays"] / case["yearBasisDays"]
                self.assertEqual(cents(expected), dec(case["expectedFee"]))

    def test_expense_accruals_match_closed_form(self) -> None:
        for case in self.data["expenseAccruals"]:
            with self.subTest(case=case["id"]):
                expected = dec(case["annualAmount"]) * case["accrualDays"] / case["yearBasisDays"]
                self.assertEqual(cents(expected), dec(case["expectedAccrual"]))

    def _assert_distribution_conserved(self, amount, expected) -> None:
        tiers = sum(dec(expected[key]) for key in TIER_KEYS)
        self.assertEqual(dec(amount), tiers, "tiers must sum to the distributed amount")
        lp = dec(expected["returnOfCapital"]) + dec(expected["preferredReturn"]) + dec(expected["lpCatchUp"]) + dec(expected["lpCarry"])
        gp = dec(expected["gpCatchUp"]) + dec(expected["gpCarry"])
        self.assertEqual(lp, dec(expected["lpTotal"]))
        self.assertEqual(gp, dec(expected["gpTotal"]))
        self.assertEqual(dec(amount), lp + gp)

    def test_full_catchup_waterfall_conserves_cash_and_cumulative_totals(self) -> None:
        series = self.data["waterfallSeries"]
        gp_sum = Decimal(0)
        lp_sum = Decimal(0)
        distributed = Decimal(0)
        for distribution in series["distributions"]:
            with self.subTest(distribution=distribution["id"]):
                self._assert_distribution_conserved(distribution["amountToDistribute"], distribution["expected"])
            gp_sum += dec(distribution["expected"]["gpTotal"])
            lp_sum += dec(distribution["expected"]["lpTotal"])
            distributed += dec(distribution["amountToDistribute"])
        cumulative = series["cumulativeExpected"]
        self.assertEqual(dec(cumulative["gpTotal"]), gp_sum)
        self.assertEqual(dec(cumulative["lpTotal"]), lp_sum)
        profit = distributed - dec(series["contributedCapital"])
        self.assertEqual(dec(cumulative["gpShareOfProfit"]), gp_sum / profit)

    def test_partial_catchup_waterfall_conserves_cash(self) -> None:
        case = self.data["partialCatchUpWaterfall"]
        self._assert_distribution_conserved(case["amountToDistribute"], case["expected"])
        # The documented pool-rounding drift is at most one cent against the ideal carry share.
        profit = dec(case["amountToDistribute"]) - dec(case["contributedCapital"])
        ideal_gp = profit * dec(case["carryRate"])
        self.assertLessEqual(abs(dec(case["expected"]["gpTotal"]) - ideal_gp), CENT)

    def test_equalisation_credits_match_closed_form(self) -> None:
        for case in self.data["equalizations"]:
            with self.subTest(case=case["id"]):
                nav = dec(case["navPerUnit"])
                hwm = dec(case["highWaterNavPerUnit"])
                rate = dec(case["performanceFeeRate"])
                units = dec(case["subscriptionUnits"])
                credit = cents(rate * max(nav - hwm, Decimal(0)) * units)
                contingent = cents(rate * max(hwm - nav, Decimal(0)) * units)
                self.assertEqual(credit, dec(case["expectedEqualisationCredit"]))
                self.assertEqual(contingent, dec(case["expectedContingentRedemption"]))


if __name__ == "__main__":
    unittest.main()

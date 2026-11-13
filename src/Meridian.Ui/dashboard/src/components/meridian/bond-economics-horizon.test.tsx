import { fireEvent, render, screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { describe, expect, it } from "vitest";
import { BondEconomicsHorizon } from "./bond-economics-horizon";
import type { BondAnalyticsResult, BondExerciseScenario } from "@/types/bond-analytics.types";

const call: BondExerciseScenario = { kind: "Call", exerciseDate: "2027-06-30", redemptionPrice: "101", yield: "0.04", earliestNoticeDate: "2027-05-01", latestNoticeDate: "2027-05-31" };
const put: BondExerciseScenario = { kind: "Put", exerciseDate: "2027-12-31", redemptionPrice: "105", yield: "0.01" };
const maturity: BondExerciseScenario = { kind: "Maturity", exerciseDate: "2030-06-30", redemptionPrice: "100", yield: "0.05" };
const result: BondAnalyticsResult = {
  securityId: "bond", currency: "USD", modelVersion: "bond-analytics-v2", context: { asOf: "2026-11-12T00:00:00Z", settlementDate: "2026-11-13" },
  cleanPrice: "104", dirtyPrice: "105", accruedInterest: "1", yieldToMaturity: ".05", currentYield: ".05", macaulayDuration: "3", modifiedDuration: "3", convexity: "10", dv01: ".03", yieldToWorst: ".04",
  exerciseYields: [call, put], redemptionScenarios: [call, put, maturity], yieldToWorstScenarios: [call],
  cashFlows: [{ flowId: "flow", paymentDate: "2027-06-30", accrualStart: "2026-12-31", accrualEnd: "2027-06-30", interest: "2.500000000000000001", principal: "0", timeInYears: ".5" }], inputs: [], blockers: [], capabilities: [], diagnostics: [], isBlocked: false
};
describe("bond economics horizon", () => {
  it("uses the supplied governing issuer scenario, and supports scenario and payment keyboard inspection", async () => {
    const { container } = render(<BondEconomicsHorizon result={result} />);
    expect(screen.getByRole("button", { name: /Inspect Call scenario.*governs yield to worst/ })).toBeInTheDocument();
    const alternative = screen.getByRole("button", { name: /Inspect Put scenario/ });
    fireEvent.keyDown(alternative, { key: " " });
    expect(screen.getByText(/Selected Put/)).not.toHaveTextContent(/Governs yield to worst/);
    expect(screen.getByRole("row", { name: /Put 2027-12-31/ })).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole("button", { name: /Inspect projected payment/ }), { key: "Enter" });
    expect(screen.getByText(/Projected payment 2027-06-30:/)).toHaveTextContent("2.500000000000000001 / 100");
    expect((await axe(container)).violations).toEqual([]);
  });
  it("never invents a governing scenario for an older response and hides blocked results", () => {
    const { rerender } = render(<BondEconomicsHorizon result={{ ...result, redemptionScenarios: undefined, yieldToWorstScenarios: undefined }} />);
    expect(screen.getByText(/No governing outcome has been inferred/)).toBeInTheDocument();
    rerender(<BondEconomicsHorizon result={{ ...result, isBlocked: true }} />);
    expect(screen.queryByText("Callable-bond horizon")).not.toBeInTheDocument();
  });
});

import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { axe } from "jest-axe";
import { BondEconomicsHorizon } from "./bond-economics-horizon";
import type { BondAnalyticsResult } from "@/types/bond-analytics.types";

const result = {
  securityId: "synthetic-bond", currency: "USD", context: { asOf: "2026-11-12T00:00:00Z" }, isBlocked: false,
  maturityDate: "2028-01-15", yieldToMaturity: "0.04", yieldToWorst: "0.04",
  governingYieldScenarios: [{ kind: "Maturity", date: "2028-01-15", yield: "0.0400" }, { kind: "Call", date: "2027-01-15", yield: "0.04" }],
  exerciseYields: [
    { kind: "Call", exerciseDate: "2027-01-15", yield: "0.04", redemptionPrice: "101.123456789012", earliestNoticeDate: "2026-10-01", latestNoticeDate: "2026-11-01" },
    { kind: "Put", exerciseDate: "2027-02-15", yield: "-0.01", redemptionPrice: "105", latestNoticeDate: null }
  ],
  cashFlows: [{ flowId: "a", paymentDate: "2028-01-17", interest: "2.5", principal: "100" }]
} as BondAnalyticsResult;

describe("bond economics horizon", () => {
  it("keeps negative interest and principal visible below separate zero lines and inside the payment hit area", () => {
    const { container } = render(<BondEconomicsHorizon result={{ ...result, cashFlows: [
      ...result.cashFlows,
      { ...result.cashFlows[0], flowId: "negative", paymentDate: "2027-01-17", interest: "-5.125", principal: "-200.0001" }
    ] }} />);
    const payment = screen.getByRole("button", { name: "Inspect projected payment 2027-01-17" });
    const [hitArea, interest, principal] = payment.querySelectorAll("rect");
    const bottom = (rect: Element) => Number(rect.getAttribute("y")) + Number(rect.getAttribute("height"));
    for (const [bar, axis] of [[interest, "Interest / 100"], [principal, "Principal / 100"]] as const) {
      expect(Number(bar.getAttribute("height"))).toBeGreaterThan(0);
      expect(Number(bar.getAttribute("y"))).toBe(Number(screen.getByText(axis).getAttribute("y")));
      expect(Number(bar.getAttribute("y"))).toBeGreaterThanOrEqual(Number(hitArea.getAttribute("y")));
      expect(bottom(bar)).toBeLessThanOrEqual(bottom(hitArea));
    }
    expect(bottom(interest)).toBeLessThan(Number(screen.getByText("Principal / 100").getAttribute("y")) - 28);
    expect(bottom(principal)).toBeLessThan(Number(container.querySelector("svg")!.getAttribute("viewBox")!.split(" ")[3]));
    fireEvent.keyDown(payment, { key: "Enter" });
    expect(screen.getByRole("status")).toHaveTextContent("interest -5.125 / 100 · principal -200.0001 / 100");
  });

  it("highlights every supplied governing scenario and retains holder puts and exact notice evidence", async () => {
    const { container } = render(<BondEconomicsHorizon result={result} />);
    expect(screen.getByRole("button", { name: "Maturity 2028-01-15 · YTW" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Call 2027-01-15 · YTW" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Put 2027-02-15" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Call 2027-01-15 · YTW" }));
    expect(screen.getByText("Redemption price per 100: 101.123456789012")).toBeInTheDocument();
    expect(screen.getByText(/Earliest notice: 2026-10-01 · Latest notice: 2026-11-01/)).toBeInTheDocument();
    expect(screen.getByText(/Actual exercise status requires retained notice evidence/)).toBeInTheDocument();
    expect(screen.getByText(/Interest and principal have independent vertical scales/)).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
  it("does not infer contractual maturity from the final adjusted payment date", () => {
    render(<BondEconomicsHorizon result={{ ...result, maturityDate: undefined }} />);
    expect(screen.queryByRole("group", { name: /Exercise yield scenarios/ })).not.toBeInTheDocument();
    expect(screen.getByText(/did not supply contractual maturity/)).toBeInTheDocument();
  });
  it("does not infer governing scenarios from a lower holder-put yield", () => {
    render(<BondEconomicsHorizon result={{ ...result, governingYieldScenarios: undefined }} />);
    expect(screen.getByText(/no governing point has been inferred/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /· YTW/ })).not.toBeInTheDocument();
  });
  it("suppresses charts for blocked results", () => {
    const { container } = render(<BondEconomicsHorizon result={{ ...result, isBlocked: true }} />);
    expect(container).toBeEmptyDOMElement();
  });
});

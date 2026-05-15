import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RiskControlPanel } from "@/components/ui/risk-control-panel";
import * as api from "@/lib/api";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getRiskRules: vi.fn().mockResolvedValue([
      {
        ruleName: "PositionLimit",
        state: "Healthy",
        summary: "No position breaches.",
        isBreached: false,
        threshold: "5000",
        currentValue: "2500",
        asOf: "2026-05-01T00:00:00Z",
        recentViolations: []
      },
      {
        ruleName: "OrderRateThrottle",
        state: "Observe",
        summary: "Approaching throughput limit.",
        isBreached: false,
        threshold: "60 orders/minute",
        currentValue: "50 orders/minute",
        asOf: "2026-05-01T00:00:00Z",
        recentViolations: ["Observed 50 orders in the last minute."]
      }
    ]),
    getRiskRuleConfig: vi.fn().mockResolvedValue({
      ruleName: "DrawdownCircuitBreaker",
      defaultMaxPositionSize: null,
      symbolPositionLimits: null,
      maxDrawdownPercent: 5,
      maxOrdersPerMinute: null
    }),
    updateRiskRuleConfig: vi.fn().mockResolvedValue({
      ruleName: "DrawdownCircuitBreaker",
      defaultMaxPositionSize: null,
      symbolPositionLimits: null,
      maxDrawdownPercent: 6,
      maxOrdersPerMinute: null
    })
  };
});

describe("RiskControlPanel", () => {
  it("renders risk rules and saves drawdown threshold updates", async () => {
    const user = userEvent.setup();
    render(<RiskControlPanel />);

    await screen.findByText("PositionLimit");
    expect(screen.getByText("OrderRateThrottle")).toBeInTheDocument();
    expect(screen.getByText(/Rule violation timeline/i)).toBeInTheDocument();

    const input = screen.getByLabelText("Drawdown threshold percent");
    await user.clear(input);
    await user.type(input, "6");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(api.updateRiskRuleConfig).toHaveBeenCalledWith("DrawdownCircuitBreaker", {
        maxDrawdownPercent: 6,
        reason: "Updated from risk control panel."
      });
    });
  });
});

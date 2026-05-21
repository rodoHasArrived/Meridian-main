import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RiskControlPanel } from "@/components/ui/risk-control-panel";
import * as api from "@/lib/api";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";

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
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders risk rules and saves drawdown threshold updates", async () => {
    const user = userEvent.setup();
    render(<RiskControlPanel />);

    await screen.findByText("PositionLimit");
    expect(screen.getByRole("region", { name: "Trading risk controls" })).toHaveAttribute("aria-busy", "false");
    expect(screen.getByText("OrderRateThrottle")).toBeInTheDocument();
    expect(screen.getByText(/Rule violation timeline/i)).toBeInTheDocument();

    const input = screen.getByLabelText("Drawdown threshold percent");
    expect(input).toHaveAttribute("aria-describedby", "risk-drawdown-threshold-help risk-control-status");
    await user.clear(input);
    await user.type(input, "6.0");
    await user.click(screen.getByRole("button", { name: "Save drawdown threshold" }));

    await waitFor(() => {
      expect(api.updateRiskRuleConfig).toHaveBeenCalledWith("DrawdownCircuitBreaker", {
        maxDrawdownPercent: 6,
        reason: "Updated from risk control panel."
      });
    });
    expect(await screen.findByRole("status")).toHaveTextContent("Drawdown threshold saved.");
    expect(input).toHaveValue("6");
    expect(document.getElementById("risk-control-status")).toHaveTextContent("Drawdown threshold saved.");
  });

  it("renders structured API error details when a risk update fails", async () => {
    const user = userEvent.setup();
    vi.mocked(api.updateRiskRuleConfig).mockRejectedValueOnce(
      createApiErrorFromResponseBody(
        "/api/risk/rules/DrawdownCircuitBreaker/config",
        409,
        JSON.stringify({
          title: "Risk configuration rejected",
          detail: "The proposed threshold conflicts with the active governance policy.",
          errors: {
            maxDrawdownPercent: ["Lower the threshold or obtain approval before retrying."]
          }
        })
      )
    );

    render(<RiskControlPanel />);

    await screen.findByText("PositionLimit");

    const input = screen.getByLabelText("Drawdown threshold percent");
    await user.clear(input);
    await user.type(input, "6");
    await user.click(screen.getByRole("button", { name: "Save drawdown threshold" }));

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("The proposed threshold conflicts with the active governance policy.")).toBeInTheDocument();
    expect(within(alert).getByText("Endpoint returned 409 for /api/risk/rules/DrawdownCircuitBreaker/config.")).toBeInTheDocument();
    expect(within(alert).getByText("Risk configuration rejected")).toBeInTheDocument();
    expect(within(alert).getByText("maxDrawdownPercent: Lower the threshold or obtain approval before retrying.")).toBeInTheDocument();
  });

  it("keeps save disabled with a reason when the drawdown value is empty", async () => {
    const user = userEvent.setup();
    render(<RiskControlPanel />);

    await screen.findByText("PositionLimit");

    const input = screen.getByLabelText("Drawdown threshold percent");
    await user.clear(input);

    const save = screen.getByRole("button", {
      name: "Save drawdown threshold unavailable: Enter a drawdown threshold before saving."
    });
    expect(save).toBeDisabled();
    expect(save).toHaveAttribute("title", "Enter a drawdown threshold before saving.");
  });

  it("surfaces load failures with a recovery action", async () => {
    vi.mocked(api.getRiskRules).mockRejectedValueOnce(
      createApiErrorFromResponseBody(
        "/api/risk/rules",
        503,
        JSON.stringify({
          title: "Risk runtime unavailable",
          detail: "The risk service is restarting."
        })
      )
    );

    render(<RiskControlPanel />);

    expect(await screen.findByRole("alert")).toHaveTextContent("The risk service is restarting.");
    expect(screen.getByText("Risk runtime unavailable")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh risk controls" })).toBeEnabled();
    expect(screen.getByText("No risk rules are currently available.")).toBeInTheDocument();
  });
});

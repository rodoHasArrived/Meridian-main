import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RiskControlPanel } from "@/components/ui/risk-control-panel";
import { useRiskControlPanelViewModel } from "@/components/ui/risk-control-panel.view-model";

vi.mock("@/components/ui/risk-control-panel.view-model", () => ({
  useRiskControlPanelViewModel: vi.fn()
}));

const mockedUseRiskControlPanelViewModel = vi.mocked(useRiskControlPanelViewModel);

describe("RiskControlPanel", () => {
  it("renders risk rule cards and saves config", async () => {
    const saveRuleConfig = vi.fn(async () => undefined);

    mockedUseRiskControlPanelViewModel.mockReturnValue({
      loading: false,
      errorText: null,
      rows: [
        {
          status: {
            ruleName: "PositionLimit",
            displayName: "Position Limit",
            state: "Observe",
            breached: false,
            summary: "Position nearing threshold",
            thresholdLabel: "Max position",
            thresholdValue: "100",
            currentValueLabel: "Largest position",
            currentValue: "85",
            lastEvaluatedAt: "2026-05-15T00:00:00Z"
          },
          config: {
            ruleName: "PositionLimit",
            config: {
              maxPositionSize: "100"
            },
            updatedAt: "2026-05-15T00:00:00Z",
            updatedBy: "ops",
            reason: "Updated"
          },
          draftConfig: {
            maxPositionSize: "100"
          },
          saving: false
        }
      ],
      refresh: vi.fn(async () => undefined),
      updateDraftValue: vi.fn(),
      saveRuleConfig
    });

    render(<RiskControlPanel />);

    expect(screen.getByText("Active risk guardrails")).toBeInTheDocument();
    expect(screen.getByText("Position nearing threshold")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(saveRuleConfig).toHaveBeenCalledWith("PositionLimit");
  });
});

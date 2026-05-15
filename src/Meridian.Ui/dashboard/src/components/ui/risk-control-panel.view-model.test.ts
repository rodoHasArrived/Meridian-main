import { act, renderHook, waitFor } from "@testing-library/react";
import { useRiskControlPanelViewModel } from "@/components/ui/risk-control-panel.view-model";
import type { RiskRuleConfig, RiskRuleStatus } from "@/types";

const positionRule: RiskRuleStatus = {
  ruleName: "PositionLimit",
  displayName: "Position Limit",
  state: "Healthy",
  breached: false,
  summary: "Position within threshold",
  thresholdLabel: "Max position",
  thresholdValue: "100",
  currentValueLabel: "Largest position",
  currentValue: "40",
  lastEvaluatedAt: "2026-05-15T00:00:00Z"
};

const positionConfig: RiskRuleConfig = {
  ruleName: "PositionLimit",
  config: {
    maxPositionSize: "100"
  },
  updatedAt: "2026-05-15T00:00:00Z",
  updatedBy: "system",
  reason: "Initial defaults"
};

describe("risk-control-panel.view-model", () => {
  it("loads risk rule statuses and configs", async () => {
    const services = {
      getRiskRules: vi.fn(async () => [positionRule]),
      getRiskRuleConfig: vi.fn(async () => positionConfig),
      updateRiskRuleConfig: vi.fn(async () => positionConfig)
    };

    const { result } = renderHook(() => useRiskControlPanelViewModel(services));

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    expect(result.current.errorText).toBeNull();
    expect(result.current.rows).toHaveLength(1);
    expect(result.current.rows[0].status.ruleName).toBe("PositionLimit");
    expect(result.current.rows[0].draftConfig.maxPositionSize).toBe("100");
  });

  it("updates and saves rule config drafts", async () => {
    const updatedConfig: RiskRuleConfig = {
      ...positionConfig,
      config: { maxPositionSize: "125" }
    };

    const services = {
      getRiskRules: vi.fn(async () => [positionRule]),
      getRiskRuleConfig: vi.fn(async () => updatedConfig),
      updateRiskRuleConfig: vi.fn(async () => updatedConfig)
    };

    const { result } = renderHook(() => useRiskControlPanelViewModel(services));

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    act(() => {
      result.current.updateDraftValue("PositionLimit", "maxPositionSize", "125");
    });

    await act(async () => {
      await result.current.saveRuleConfig("PositionLimit");
    });

    expect(services.updateRiskRuleConfig).toHaveBeenCalledWith("PositionLimit", {
      config: {
        maxPositionSize: "125"
      }
    });
    expect(result.current.rows[0].draftConfig.maxPositionSize).toBe("125");
  });
});

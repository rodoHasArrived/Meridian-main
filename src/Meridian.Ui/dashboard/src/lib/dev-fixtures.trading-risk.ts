import type { TradingRiskState } from "@/types";

/**
 * Trading risk fixture mirroring the live rule-registry payload shape:
 * flat guardrail strings plus structured utilization-bar entries.
 */
export const fixtureTradingRisk: TradingRiskState = {
  state: "Observe",
  summary: "Guardrails are active.",
  netExposure: "$120,000",
  grossExposure: "$150,000",
  var95: "$9,000",
  maxDrawdown: "-1.1%",
  buyingPowerUsed: "58%",
  activeGuardrails: [
    "SymbolConcentration: NVDA 26.40% (threshold 30.00%, state Observe).",
    "OrderRateThrottle: 41 orders/minute (threshold 60 orders/minute, state Healthy)."
  ],
  guardrails: [
    {
      ruleName: "SymbolConcentration",
      state: "Observe",
      currentValue: "NVDA 26.40%",
      threshold: "30.00%",
      utilizationPercent: 88,
      severity: "Error"
    },
    {
      ruleName: "GrossExposure",
      state: "Healthy",
      currentValue: "150000.00",
      threshold: "400000",
      utilizationPercent: 37.5,
      severity: "Critical"
    },
    {
      ruleName: "OrderRateThrottle",
      state: "Healthy",
      currentValue: "41 orders/minute",
      threshold: "60 orders/minute",
      utilizationPercent: 68.33,
      severity: "Error"
    },
    {
      ruleName: "OrderNotional",
      state: "Healthy",
      currentValue: "0 pending approval(s)",
      threshold: "escalate ≥ 50000, reject > 250000",
      utilizationPercent: null,
      severity: "Escalate"
    }
  ]
};

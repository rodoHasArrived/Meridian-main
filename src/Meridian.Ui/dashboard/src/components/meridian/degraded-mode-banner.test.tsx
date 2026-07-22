import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { DegradedModeBanner } from "@/components/meridian/degraded-mode-banner";
import type { DegradedModeStatus } from "@/types";

function status(overrides: Partial<DegradedModeStatus> = {}): DegradedModeStatus {
  return {
    marketDataMode: "live",
    marketDataDetail: null,
    persistenceMode: "configured",
    missingPersistenceDomains: [],
    ...overrides
  };
}

describe("DegradedModeBanner", () => {
  it("renders nothing when the payload is absent", () => {
    const { container } = render(<DegradedModeBanner degradedMode={null} />);

    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing when market data is live and persistence is configured", () => {
    const { container } = render(<DegradedModeBanner degradedMode={status()} />);

    expect(container).toBeEmptyDOMElement();
  });

  it("shows a red alert when market data is simulated", () => {
    render(
      <DegradedModeBanner
        degradedMode={status({
          marketDataMode: "simulated",
          marketDataDetail: "Streaming source 'ib' runs as a random-walk simulator in this build."
        })}
      />
    );

    const alert = screen.getByTestId("degraded-mode-simulated-market-data");
    expect(alert).toHaveAttribute("role", "alert");
    expect(alert).toHaveTextContent("SIMULATED MARKET DATA");
    expect(alert).toHaveTextContent("random-walk simulator");
  });

  it("shows a persistence alert listing the domains without a database", () => {
    render(
      <DegradedModeBanner
        degradedMode={status({
          persistenceMode: "none",
          missingPersistenceDomains: ["ledger", "banking", "fund-accounts"]
        })}
      />
    );

    const alert = screen.getByTestId("degraded-mode-persistence");
    expect(alert).toHaveTextContent("PERSISTENCE: NONE");
    expect(alert).toHaveTextContent("ledger, banking, fund-accounts");
    expect(alert).toHaveTextContent("MERIDIAN_DATABASE_URL");
  });

  it("shows both alerts when simulated data and partial persistence overlap", () => {
    render(
      <DegradedModeBanner
        degradedMode={status({
          marketDataMode: "simulated",
          persistenceMode: "partial",
          missingPersistenceDomains: ["banking"]
        })}
      />
    );

    expect(screen.getByTestId("degraded-mode-simulated-market-data")).toBeInTheDocument();
    expect(screen.getByTestId("degraded-mode-persistence")).toHaveTextContent("PERSISTENCE: PARTIAL");
  });
});

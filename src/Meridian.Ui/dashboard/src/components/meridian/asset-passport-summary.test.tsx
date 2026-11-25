import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { axe } from "jest-axe";
import { AssetPassportSummary } from "./asset-passport-summary";
import { getSecurityInstrumentPassport } from "@/lib/api";
import type { InstrumentPassport } from "@/types";

vi.mock("@/lib/api", async () => ({
  ...(await vi.importActual<typeof import("@/lib/api")>("@/lib/api")),
  getSecurityInstrumentPassport: vi.fn()
}));

function passport(securityId = "sec-a", overrides: Partial<InstrumentPassport> = {}): InstrumentPassport {
  return {
    securityId,
    identity: {
      securityId,
      displayName: "Security A",
      assetClass: "Bond",
      status: "Active",
      version: 2,
      effectiveFrom: "2026-01-01",
      effectiveTo: null,
      identifiers: [],
      aliases: []
    },
    economicDefinition: {
      assetClass: "Bond",
      riskCountry: "US",
      winningSourceSystem: "Vendor record",
      winningSourceAsOf: "2026-11-20T10:00:00Z"
    },
    identifierSummary: { summary: "Identifiers aligned" },
    providerMappings: [],
    lifecycleEvents: [],
    corporateActions: [],
    pricing: {
      status: "Ready",
      summary: "Trading parameters available",
      tradingParameters: null,
      lotSize: 100,
      tickSize: null,
      contractMultiplier: null,
      tradingHoursUtc: null,
      circuitBreakerThresholdPct: null
    },
    usage: { summary: "Not provided" },
    trustPosture: {
      tone: "Review",
      summary: "Review the retained source evidence."
    },
    providerConfidence: [],
    retrievedAtUtc: "2026-11-21T10:00:00Z",
    ...overrides
  };
}

describe("AssetPassportSummary", () => {
  beforeEach(() => vi.resetAllMocks());

  it("keeps trust separate from instrument status and does not invent missing operational assessments", async () => {
    vi.mocked(getSecurityInstrumentPassport).mockResolvedValue(passport());
    const open = vi.fn();
    const { container } = render(<AssetPassportSummary securityId="sec-a" onOpenPassport={open} />);
    expect(await screen.findByText("Review the retained source evidence.")).toBeInTheDocument();
    expect(screen.getByText("Review", { exact: true })).toBeInTheDocument();
    expect(screen.getByText("No provider evidence")).toBeInTheDocument();
    expect(screen.getAllByText("Not assessed")).toHaveLength(2);
    expect(screen.getByLabelText("Instrument context")).toHaveTextContent("Risk countryUS");
    expect(screen.getByText(/Economic source Vendor record/)).toHaveTextContent("Source as of 2026-11-20T10:00:00Z");
    await userEvent.click(screen.getByRole("button", { name: "Open governed passport" }));
    expect(open).toHaveBeenCalledOnce();
    expect((await axe(container)).violations).toHaveLength(0);
  });

  it("counts unique conflicts and reveals server-provided corrective evidence", async () => {
    const provider = {
      provider: "Source A",
      providerSource: "feed-a",
      mappingKind: "Ticker",
      symbol: "A",
      normalizedSymbol: "A",
      isPrimary: true,
      isActive: true,
      freshnessAsOf: null,
      freshnessMinutes: null,
      confidenceScore: 0.5,
      confidenceReason: "Review",
      identifierConflictIds: ["conflict-1"],
      identifierConflictSummaries: ["CUSIP differs between sources."],
      overrideHistory: []
    };
    vi.mocked(getSecurityInstrumentPassport).mockResolvedValue(
      passport("sec-a", {
        providerConfidence: [provider, { ...provider, provider: "Source B" }],
        operationsWorkbench: {
          status: "Review",
          summary: "One check requires review",
          panels: [],
          handoffs: [],
          readiness: [
            {
              readinessId: "pricing",
              label: "Price readiness",
              status: "Blocked",
              isReady: false,
              summary: "Price evidence is missing.",
              evidenceCount: 0,
              blockingIssueCount: 1,
              nextAction: "Retain a price observation."
            }
          ]
        }
      })
    );
    render(<AssetPassportSummary securityId="sec-a" onOpenPassport={vi.fn()} />);
    await screen.findByText("Review the retained source evidence.");
    await userEvent.click(screen.getByText("Inspect readiness evidence"));
    expect(screen.getByText("CUSIP differs between sources.")).toBeVisible();
    expect(screen.getByText("Retain a price observation.")).toBeVisible();
    const conflictCount = screen.getByText("Provider identifier conflicts").parentElement;
    expect(conflictCount).toHaveTextContent("Provider identifier conflicts1");
  });

  it("offers retry after an unavailable passport and rejects a foreign identity", async () => {
    vi.mocked(getSecurityInstrumentPassport)
      .mockRejectedValueOnce(new Error("Unavailable"))
      .mockResolvedValueOnce(passport("foreign"))
      .mockResolvedValueOnce(passport());
    render(<AssetPassportSummary securityId="sec-a" onOpenPassport={vi.fn()} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Couldn’t load readiness");
    await userEvent.click(screen.getByRole("button", { name: "Retry readiness" }));
    await waitFor(() => expect(getSecurityInstrumentPassport).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.queryByText("Review the retained source evidence.")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Retry readiness" }));
    expect(await screen.findByText("Review the retained source evidence.")).toBeInTheDocument();
  });

  it("never restores a delayed passport for the previous security", async () => {
    let resolveFirst!: (result: InstrumentPassport) => void;
    vi.mocked(getSecurityInstrumentPassport)
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolveFirst = resolve;
        })
      )
      .mockResolvedValueOnce(
        passport("sec-b", {
          trustPosture: {
            tone: "Blocked",
            summary: "Security B needs evidence."
          }
        })
      );
    const { rerender } = render(<AssetPassportSummary securityId="sec-a" onOpenPassport={vi.fn()} />);
    rerender(<AssetPassportSummary securityId="sec-b" onOpenPassport={vi.fn()} />);
    expect(await screen.findByText("Security B needs evidence.")).toBeInTheDocument();
    await act(async () => resolveFirst(passport()));
    expect(screen.getByText("Security B needs evidence.")).toBeInTheDocument();
    expect(screen.queryByText("Review the retained source evidence.")).not.toBeInTheDocument();
  });
});

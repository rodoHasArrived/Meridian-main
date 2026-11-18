import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { CrossProviderTolerancePanel, policyLoosens } from "@/screens/data-quality-watch.cross-provider-policy";
import type { CrossProviderTolerancePolicy, CrossProviderTolerancePolicyResponse } from "@/types/data-quality-watch.types";

const defaults: CrossProviderTolerancePolicy = {
  priceTolerancePercent: 0.5, quoteTolerancePercent: 0.5, volumeTolerancePercent: 10, volumeMode: "Alert",
  backfillBarsToCompare: 5, backfillCloseTolerancePercent: 1, assetClassOverrides: null
};
const response: CrossProviderTolerancePolicyResponse = {
  policy: defaults, defaults,
  bounds: { maxPriceTolerancePercent: 5, maxVolumeTolerancePercent: 50, maxBackfillBarsToCompare: 10000, maxAssetClassOverrides: 32, maxPreviewDays: 30 },
  audit: [], previewProviders: ["stooq", "tiingo"], providerCallsPerPreview: 2
};
const liveImpact = {
  impact: "Live", changes: [], pendingChanges: [], savedAtUtc: "2026-11-18T10:05:00Z", requiresRestart: false, requiresReconnect: false,
  lastApply: { appliedAtUtc: "2026-11-18T10:05:00Z", entries: [{ path: "dataQuality.crossProvider.priceTolerancePercent", kind: "AppliedLive", detail: "Cross-provider comparisons now use the saved tolerances." }] }
} as const;

function makeApi() {
  return {
    fetchPolicy: vi.fn().mockResolvedValue(response),
    savePolicy: vi.fn().mockImplementation(async (policy: CrossProviderTolerancePolicy, reason: string) => ({
      policy, changeImpact: liveImpact,
      audit: { id: "a1", changedAt: "2026-11-18T10:05:00Z", actor: "quant", reason: reason || null, loosened: policyLoosens(policy, defaults), before: defaults, after: policy }
    })),
    previewPolicy: vi.fn().mockResolvedValue({
      symbol: "SPY", from: "2026-10-19", to: "2026-11-17", baselineProvider: "stooq", comparisonProvider: "tiingo",
      flaggedDays: 3, flagged: [{ sessionDate: "2026-11-02", baselineClose: 500, comparisonClose: 500.75, closeDifferencePercent: 0.15, volumeDifferencePercent: null, flaggedBy: "Price" }],
      informationalDays: 1, missingSessions: 0, providerErrors: [], providerCallsUsed: 2
    })
  };
}

async function open(api = makeApi()) {
  const user = userEvent.setup();
  renderWithRouter(<CrossProviderTolerancePanel {...api} />);
  expect(api.fetchPolicy).not.toHaveBeenCalled();
  await user.click(screen.getByText("Cross-provider tolerances"));
  await screen.findByRole("button", { name: "Save tolerances" });
  return { user, api };
}

describe("policyLoosens", () => {
  it("treats wider bands, weaker volume modes and fewer bars as loosening", () => {
    expect(policyLoosens({ ...defaults, priceTolerancePercent: 0.05 }, defaults)).toBe(false);
    expect(policyLoosens({ ...defaults, priceTolerancePercent: 1 }, defaults)).toBe(true);
    expect(policyLoosens({ ...defaults, volumeMode: "Informational" }, defaults)).toBe(true);
    expect(policyLoosens({ ...defaults, backfillBarsToCompare: null }, defaults)).toBe(false);
    expect(policyLoosens({ ...defaults, backfillBarsToCompare: 2 }, defaults)).toBe(true);
    expect(policyLoosens({ ...defaults, assetClassOverrides: { preferred: { priceTolerancePercent: 2 } } }, defaults)).toBe(true);
  });
});

describe("CrossProviderTolerancePanel", () => {
  it("saves a stricter 5 bps policy without a reason and reports the live apply", async () => {
    const { user, api } = await open();
    const price = screen.getByRole("spinbutton", { name: "Trade price tolerance (%)" });
    await user.clear(price); await user.type(price, "0.05");
    expect(screen.getByRole("textbox", { name: "Change reason (optional)" })).not.toBeRequired();
    await user.click(screen.getByRole("button", { name: "Save tolerances" }));
    expect(api.savePolicy).toHaveBeenCalledWith(expect.objectContaining({ priceTolerancePercent: 0.05 }), "", expect.anything());
    expect(await screen.findByText("Applied live")).toBeInTheDocument();
    expect(screen.queryByText("Pending host restart")).not.toBeInTheDocument();
  });

  it("requires a reason before loosening and keeps the save disabled until given", async () => {
    const { user, api } = await open();
    await user.selectOptions(screen.getByRole("combobox", { name: "Volume differences" }), "Informational");
    expect(screen.getByText(/flags fewer discrepancies/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save tolerances" })).toBeDisabled();
    await user.type(screen.getByRole("textbox", { name: "Change reason (required)" }), "Volume differs by venue coverage");
    await user.click(screen.getByRole("button", { name: "Save tolerances" }));
    expect(api.savePolicy).toHaveBeenCalledWith(expect.objectContaining({ volumeMode: "Informational" }), "Volume differs by venue coverage", expect.anything());
  });

  it("adds an asset-class override for preferreds", async () => {
    const { user, api } = await open();
    await user.type(screen.getByRole("textbox", { name: "New asset class" }), "preferred");
    await user.click(screen.getByRole("button", { name: "Add override" }));
    const override = screen.getByRole("group", { name: "Override for preferred" });
    expect(override).toBeInTheDocument();
    await user.type(screen.getByRole("spinbutton", { name: "Price (%)" }), "2");
    await user.type(screen.getByRole("textbox", { name: "Change reason (required)" }), "Illiquid preferreds");
    await user.click(screen.getByRole("button", { name: "Save tolerances" }));
    expect(api.savePolicy).toHaveBeenCalledWith(
      expect.objectContaining({ assetClassOverrides: { preferred: { priceTolerancePercent: 2 } } }), "Illiquid preferreds", expect.anything());
  });

  it("previews the draft against two providers and states the provider-call cost", async () => {
    const { user, api } = await open();
    expect(screen.getByText(/about 2 rate-limited provider calls/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Preview last 30 days" }));
    expect(api.previewPolicy).toHaveBeenCalledWith(expect.objectContaining({ symbol: "SPY", baselineProvider: "stooq", comparisonProvider: "tiingo", days: 30 }), expect.anything());
    expect(await screen.findByText(/Would have flagged 3 of the last 30 days for SPY/)).toBeInTheDocument();
    expect(screen.getByText(/1 more day\(s\) differ only in volume/)).toBeInTheDocument();
  });

  it("shows a load failure without inventing tolerances", async () => {
    const api = makeApi(); api.fetchPolicy.mockRejectedValue(new Error("Quality host offline."));
    const user = userEvent.setup();
    renderWithRouter(<CrossProviderTolerancePanel {...api} />);
    await user.click(screen.getByText("Cross-provider tolerances"));
    expect(await screen.findByRole("alert")).toHaveTextContent("Quality host offline.");
    expect(screen.queryByRole("button", { name: "Save tolerances" })).not.toBeInTheDocument();
  });
});

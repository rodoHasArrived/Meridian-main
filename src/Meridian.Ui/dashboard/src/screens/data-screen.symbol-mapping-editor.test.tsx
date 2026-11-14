import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SymbolMappingEditor } from "@/screens/data-screen.symbol-mapping-editor";
import { renderWithRouter } from "@/test/render";
import type { symbolMappingApi, SymbolMappingRollout } from "@/lib/api/symbol-mapping.api";

const snapshot: SymbolMappingRollout = { mismatchCount: 1, providers: ["ib", "alpaca", "polygon", "yahoo"].map(provider => ({ provider, savedMode: "Compare", activeMode: "Compare", pendingRestart: false, recentMismatches: provider === "yahoo" ? [{ input: "BRK.B", fromProvider: "alpaca", toProvider: "yahoo", legacyResult: "BRK.B", canonicalResult: "BRK-B", securityId: null, observedAt: "2026-11-13T10:00:00Z" }] : [] })) };
function apiFixture(): typeof symbolMappingApi {
  return {
    list: vi.fn().mockResolvedValue([{ canonicalSymbol: "BRK.B", ibSymbol: null, alpacaSymbol: null, polygonSymbol: null, yahooSymbol: "BRK.B", name: "Berkshire Hathaway", figi: null }]),
    rollout: vi.fn().mockResolvedValue(snapshot),
    save: vi.fn().mockResolvedValue(undefined),
    remove: vi.fn().mockResolvedValue(undefined),
    importCsv: vi.fn().mockResolvedValue({ imported: 1 }),
    saveRollout: vi.fn().mockResolvedValue({ pendingRestart: true })
  };
}

describe("SymbolMappingEditor", () => {
  it("edits a Yahoo alias through the existing CRUD route without changing identity", async () => {
    const api = apiFixture(), user = userEvent.setup();
    renderWithRouter(<SymbolMappingEditor api={api} />);
    await screen.findByText("Retained yahoo comparison mismatches");
    await user.selectOptions(screen.getByLabelText("Existing mapping"), "BRK.B");
    await user.clear(screen.getByLabelText("Yahoo symbol"));
    await user.type(screen.getByLabelText("Yahoo symbol"), "BRK-B");
    await user.click(screen.getByRole("button", { name: "Save symbol mapping" }));
    await waitFor(() => expect(api.save).toHaveBeenCalledWith(expect.objectContaining({ canonicalSymbol: "BRK.B", yahooSymbol: "BRK-B", name: "Berkshire Hathaway" }), expect.objectContaining({ signal: expect.any(AbortSignal) })));
    expect(await screen.findByText(/Registry mapping saved/)).toBeInTheDocument();
  });

  it("requires evidence review, both archive acknowledgements, and a recorded reason before per-provider promotion", async () => {
    const api = apiFixture(), user = userEvent.setup();
    renderWithRouter(<SymbolMappingEditor api={api} />);
    await screen.findByText("Retained yahoo comparison mismatches");
    const promote = screen.getByRole("button", { name: "Promote yahoo to Canonical" });
    expect(promote).toBeDisabled();
    expect(screen.getByText("BRK.B: BRK.B → BRK-B")).toBeInTheDocument();
    await user.click(screen.getByLabelText(/I reviewed this provider/));
    await user.click(screen.getByLabelText(/existing archives are not rewritten/));
    await user.click(screen.getByLabelText(/keep raw and adjusted series separate/));
    expect(promote).toBeDisabled();
    await user.type(screen.getByLabelText("Promotion reason"), "Yahoo aliases checked against compare evidence");
    await user.click(promote);
    await waitFor(() => expect(api.saveRollout).toHaveBeenCalledWith("yahoo", expect.objectContaining({ mode: "Canonical", expectedMode: "Compare", reviewedMismatchCount: 1, evidenceReviewed: true, confirmArchivesNotRewritten: true, confirmSeriesRemainSeparate: true }), expect.any(Object)));
    expect(await screen.findByText(/Canonical mode saved for this provider only/)).toBeInTheDocument();
  });

  it("clears prior evidence review when changing the provider", async () => {
    const user = userEvent.setup();
    renderWithRouter(<SymbolMappingEditor api={apiFixture()} />);
    await screen.findByText("Retained yahoo comparison mismatches");
    await user.click(screen.getByLabelText(/I reviewed this provider/));
    await user.selectOptions(screen.getByLabelText("Rollout provider"), "alpaca");
    expect(screen.getByLabelText(/I reviewed this provider/)).not.toBeChecked();
    expect(screen.getByRole("button", { name: "Promote alpaca to Canonical" })).toBeDisabled();
  });

  it("fails closed after a save error and retains the operator's draft", async () => {
    const api = apiFixture(), user = userEvent.setup();
    vi.mocked(api.save).mockRejectedValue(new Error("Write outcome uncertain"));
    renderWithRouter(<SymbolMappingEditor api={api} />);
    await screen.findByText("Retained yahoo comparison mismatches");
    await user.type(screen.getByLabelText("Canonical symbol"), "AAPL");
    await user.click(screen.getByRole("button", { name: "Save symbol mapping" }));
    expect(await screen.findByText("Mapping change unavailable")).toBeInTheDocument();
    expect(screen.getByLabelText("Canonical symbol")).toHaveValue("AAPL");
    expect(screen.getByRole("button", { name: "Save symbol mapping" })).toBeDisabled();
    expect(screen.queryByText("Mapping change saved")).not.toBeInTheDocument();
  });

  it("uses the existing CSV import contract", async () => {
    const api = apiFixture(), user = userEvent.setup();
    renderWithRouter(<SymbolMappingEditor api={api} />);
    await screen.findByText("Retained yahoo comparison mismatches");
    const file = new File(["canonical,yahoo\nBRK.B,BRK-B"], "symbols.csv", { type: "text/csv" });
    await user.upload(screen.getByLabelText("Mapping CSV file"), file);
    await user.click(screen.getByRole("button", { name: "Import mappings" }));
    await waitFor(() => expect(api.importCsv).toHaveBeenCalledWith(file, expect.objectContaining({ signal: expect.any(AbortSignal) })));
  });
});

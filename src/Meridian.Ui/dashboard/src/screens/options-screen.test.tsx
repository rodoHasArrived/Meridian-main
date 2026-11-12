import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { renderWithRouter } from "@/test/render";
import { OptionsScreen } from "./options-screen";
import { DEFAULT_OPTION_CRITERIA } from "./options-screen.view-model";
import type { OptionScreenCandidate, OptionScreenResult } from "@/types/option-visualization.types";

const mocks = vi.hoisted(() => ({ screen: vi.fn(), history: vi.fn(), preview: vi.fn(), accept: vi.fn() }));
vi.mock("@/lib/api/option-visualization.api", () => ({ screenOptions: mocks.screen, listOptionAssessments: mocks.history,
  previewOptionAssessment: mocks.preview, acceptOptionAssessment: mocks.accept, optionAssessmentSourceUrl: () => "/source" }));
const call: OptionScreenCandidate = { id: "call", symbol: "XYZ-C100", underlyingSymbol: "XYZ", right: "Call", expiration: "2027-01-15",
  daysToExpiration: 64, strike: "100", delta: "0.3", bid: "1", ask: "1.1", mid: "1.05", spreadFraction: "0.095",
  openInterest: 1000, volume: 200, multiplier: "100", currency: "USD", exchange: "SMART", style: "American",
  source: "synthetic", observedAt: "2026-11-12T12:00:00Z", passesScreen: true, rejectionReasons: [] };
const put: OptionScreenCandidate = { ...call, id: "put", symbol: "XYZ-P100", right: "Put", delta: "-0.3", passesScreen: false, rejectionReasons: ["Volume below minimum"] };
const result: OptionScreenResult = { criteria: { ...DEFAULT_OPTION_CRITERIA, underlyingSymbol: "XYZ" }, screenedAt: "2026-11-12T12:00:00Z",
  candidates: [call, put], loadedExpirations: ["2027-01-15"], failedExpirations: [], omittedExpirations: [], strikeRange: 20,
  coverageNote: "Partial provider scope", authorityNote: "Screen pass is not account permission." };

describe("OptionsScreen", () => {
  beforeEach(() => { vi.clearAllMocks(); mocks.screen.mockResolvedValue(result); mocks.history.mockResolvedValue([]); });
  it("keeps initial data absent and exposes general call/put criteria and basket controls", () => {
    renderWithRouter(<OptionsScreen />, { initialEntries: ["/data/quotes?view=options"] });
    expect(mocks.screen).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Right")).toHaveValue("Both");
    expect(screen.getByText(/Arbitrary call, put and equity legs/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Preview evidence import" })).toBeDisabled();
  });
  it("synchronizes signed-delta map selection, table and inspector without inferring broker acceptance", async () => {
    const user = userEvent.setup(); renderWithRouter(<OptionsScreen />, { initialEntries: ["/data/quotes?view=options&symbol=XYZ"] });
    await user.click(screen.getByRole("button", { name: "Screen contracts" }));
    await screen.findByRole("table", { name: "Options screening contracts" });
    await user.click(screen.getByRole("button", { name: "XYZ-P100, Put, rejected" }));
    const inspector = screen.getByRole("region", { name: "Selected option contract" });
    expect(within(inspector).getByText(/Volume below minimum/)).toBeInTheDocument();
    await user.click(within(inspector).getByRole("button", { name: "Use selected contract" }));
    expect(screen.getByRole("group", { name: "leg-1 · Option" })).toBeInTheDocument();
    await user.selectOptions(within(screen.getByRole("group", { name: "leg-1 · Option" })).getByLabelText("Side", { exact: true }), "Sell");
    expect(within(screen.getByRole("group", { name: "leg-1 · Option" })).getByLabelText("Side", { exact: true })).toHaveValue("Sell");
    expect(screen.getByLabelText("Exact deliverable / settlement terms")).toHaveValue("");
    expect(mocks.accept).not.toHaveBeenCalled();
  });
  it("discards a delayed response after criteria change even when transport ignores abort", async () => {
    let resolve: (value: OptionScreenResult) => void;
    mocks.screen.mockReturnValue(new Promise<OptionScreenResult>(done => { resolve = done; }));
    const user = userEvent.setup(); renderWithRouter(<OptionsScreen />, { initialEntries: ["/data/quotes?view=options&symbol=XYZ"] });
    await user.click(screen.getByRole("button", { name: "Screen contracts" }));
    await user.clear(screen.getByLabelText("Underlying")); await user.type(screen.getByLabelText("Underlying"), "OTHER");
    await act(async () => resolve(result));
    await waitFor(() => expect(screen.queryByRole("table", { name: "Options screening contracts" })).not.toBeInTheDocument());
    expect(screen.getByLabelText("Underlying")).toHaveValue("OTHER");
  });
  it("keeps signed-delta numeric drafts editable through an empty intermediate value", async () => {
    const user = userEvent.setup(); renderWithRouter(<OptionsScreen />, { initialEntries: ["/data/quotes?view=options&symbol=XYZ"] });
    const minimum = screen.getByLabelText("Minimum signed delta");
    await user.clear(minimum); await user.type(minimum, "-0.25");
    expect(minimum).toHaveValue(-0.25);
    await user.click(screen.getByRole("button", { name: "Screen contracts" }));
    await waitFor(() => expect(mocks.screen).toHaveBeenCalled());
    expect(mocks.screen.mock.calls[0][0].minDelta).toBe(-0.25);
  });
  it("has no basic accessibility violations with the map, table and evidence-backed import form rendered", async () => {
    const user = userEvent.setup(); const { container } = renderWithRouter(<OptionsScreen />, { initialEntries: ["/data/quotes?view=options&symbol=XYZ"] });
    await user.click(screen.getByRole("button", { name: "Screen contracts" }));
    await screen.findByRole("table", { name: "Options screening contracts" });
    const results = await axe(container);
    expect(results.violations.map(violation => ({ id: violation.id, targets: violation.nodes.map(node => node.target) }))).toEqual([]);
  });
});

import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { renderWithRouter } from "@/test/render";
import { OptionsScreen } from "./options-screen";
import { candidateLeg, DEFAULT_OPTION_CRITERIA, EMPTY_OPTION_BASKET, emptyAssessment, newEquityLeg } from "./options-screen.view-model";
import type { OptionScreenCandidate, OptionScreenResult, RetainedOptionAssessment } from "@/types/option-visualization.types";

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
  it.each(["Option", "Equity"] as const)("allocates a distinct %s leg after loading a retained basket and keeps edits and removals independent", async (kind) => {
    const account = "f3550de4-a0d8-459d-9c8a-cb2eb3eb6f98";
    const basket = { ...EMPTY_OPTION_BASKET, legs: [
      { ...candidateLeg(call, "leg-1"), symbol: "RETAINED-CALL", ratio: "2" },
      { ...newEquityLeg("leg-3"), symbol: "RETAINED-SHARES" }
    ] };
    const retained: RetainedOptionAssessment = {
      assessmentId: "retained-record", assessment: { ...emptyAssessment(account, basket), providerId: "synthetic", externalAccountId: "synthetic-account",
        assessedAt: "2026-11-12T12:00:00Z", brokerReference: "retained-report", rawBrokerStatus: "Reported" },
      basketFingerprint: "basket-hash", contentSha256: "content-hash", recordedAt: "2026-11-12T12:05:00Z", recordedBy: "operator",
      sourceVaultId: "source", sourceDocumentId: "source-document", sourceSha256: "source-hash",
      assessmentVaultId: "assessment", assessmentDocumentId: "assessment-document", correctsAssessmentId: null, isSuperseded: false
    };
    mocks.history.mockResolvedValue([retained]);
    const user = userEvent.setup();
    renderWithRouter(<OptionsScreen />, { initialEntries: [`/data/quotes?view=options&symbol=XYZ&fundAccountId=${account}`] });
    await user.click(screen.getByRole("button", { name: "Load assessment history" }));
    await user.click(await screen.findByRole("button", { name: "Use exact assessed basket" }));
    const retainedOption = screen.getByRole("group", { name: "leg-1 · Option" });
    const retainedEquity = screen.getByRole("group", { name: "leg-3 · Equity" });
    if (kind === "Option") {
      await user.click(screen.getByRole("button", { name: "Screen contracts" }));
      await screen.findByRole("table", { name: "Options screening contracts" });
      await user.click(screen.getByRole("button", { name: "XYZ-P100, Put, rejected" }));
      await user.click(screen.getByRole("button", { name: "Use selected contract" }));
    } else await user.click(screen.getByRole("button", { name: "Add equity leg" }));

    const groups = screen.getAllByRole("group", { name: /^leg-\d+ · (Option|Equity)$/ });
    expect(groups).toHaveLength(3);
    const legIds = groups.map(group => group.querySelector("legend")!.textContent!.split(" · ")[0]);
    expect(new Set(legIds).size).toBe(3);
    const added = groups.find(group => group !== retainedOption && group !== retainedEquity)!;
    expect(added.querySelector("legend")).toHaveTextContent(kind);
    const symbol = within(added).getByLabelText("Symbol", { exact: true });
    await user.clear(symbol); await user.type(symbol, "NEW-ONLY");
    expect(within(retainedOption).getByLabelText("Symbol", { exact: true })).toHaveValue("RETAINED-CALL");
    expect(within(retainedEquity).getByLabelText("Symbol", { exact: true })).toHaveValue("RETAINED-SHARES");
    const retainedRatio = within(retainedOption).getByLabelText("Ratio quantity");
    await user.clear(retainedRatio); await user.type(retainedRatio, "7");
    expect(within(added).getByLabelText("Ratio quantity")).toHaveValue(kind === "Option" ? "1" : "100");
    expect(within(retainedEquity).getByLabelText("Ratio quantity")).toHaveValue("100");
    await user.click(within(added).getByRole("button", { name: /^Remove leg-/ }));
    expect(screen.getAllByRole("group", { name: /^leg-\d+ · (Option|Equity)$/ })).toHaveLength(2);
    expect(within(retainedOption).getByLabelText("Ratio quantity")).toHaveValue("7");
    await user.click(within(retainedOption).getByRole("button", { name: "Remove leg-1" }));
    expect(screen.queryByRole("group", { name: "leg-1 · Option" })).not.toBeInTheDocument();
    expect(within(retainedEquity).getByLabelText("Symbol", { exact: true })).toHaveValue("RETAINED-SHARES");
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

import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter as render } from "@/test/render";
import { CancelAllIncidentPanel } from "@/screens/trading-screen.cancel-incidents";
import { getCancelAllIncident, getCancelAllIncidents } from "@/lib/api/cancel-incidents.api";
import { downloadShownXlsx, shownExportXlsxSheets } from "@/lib/table-export";
import type { CancelAllIncident, CancelAllIncidentHistory, CancelAllIncidentSummary } from "@/types/cancel-incidents.types";

vi.mock("@/lib/api/cancel-incidents.api", () => ({ getCancelAllIncident: vi.fn(), getCancelAllIncidents: vi.fn() }));
vi.mock("@/lib/table-export", async importOriginal => {
  const actual = await importOriginal<typeof import("@/lib/table-export")>();
  return { ...actual, downloadShownXlsx: vi.fn() };
});
const list = vi.mocked(getCancelAllIncidents);
const read = vi.mocked(getCancelAllIncident);
const summary: CancelAllIncidentSummary = {
  incidentId: "incident-1", fundAccountId: "account-a", startedAt: "2026-11-23T14:00:00Z",
  completedAt: "2026-11-23T14:00:02Z", actor: "operator", gatewayId: "Paper", executionMode: "Paper",
  evidenceStatus: "Complete", brokerViewUnavailable: false, orderCount: 12, unresolvedOrderCount: 1
};
const history: CancelAllIncidentHistory = {
  asOf: "2026-11-23T14:05:00Z", returned: 1, limit: 50, hasMore: false,
  coverageNote: "Retained observations for this account; not current exposure.", incidents: [summary]
};
const incident: CancelAllIncident = {
  summary, correlationId: "action-1", orders: Array.from({ length: 12 }, (_, index) => ({
    observationKey: `local:order-${index}`, orderId: `order-${index}`, symbol: `SYM${index}`, side: "Buy",
    operation: "Cancel", disposition: index === 11 ? "Unknown" : "ConfirmedCancelled",
    status: index === 11 ? "PendingCancel" : "Cancelled", quantity: "10", filledQuantity: index === 11 ? "2.1234567890123456789012345" : "0",
    remainingQuantity: index === 11 ? null : "0", cancellationAttempted: true,
    requiresReconciliation: index === 11, hasUnconfirmedReplacement: index === 11,
    candidateBrokerOrderId: index === 11 ? "replace-candidate" : null,
    valuationCoverage: "Unavailable", observedAt: "2026-11-23T14:00:02Z", detail: index === 11 ? "Cancellation acknowledgement missing." : "Cancellation confirmed."
  }))
};

afterEach(() => vi.resetAllMocks());

describe("CancelAllIncidentPanel", () => {
  it("requires account scope before making any evidence request", () => {
    render(<CancelAllIncidentPanel />);
    expect(screen.getByText("Select an execution account")).toBeInTheDocument();
    expect(list).not.toHaveBeenCalled();
    expect(read).not.toHaveBeenCalled();
  });

  it("reopens every retained order and preserves unknown quantities and exact financial text", async () => {
    list.mockResolvedValue(history);
    read.mockResolvedValue(incident);
    const user = userEvent.setup();
    render(<CancelAllIncidentPanel fundAccountId="account-a" />);
    const table = await screen.findByLabelText("Cancel-all order outcomes");
    expect(within(table).getByText("order-11")).toBeInTheDocument();
    expect(within(table).getByText("2.1234567890123456789012345")).toBeInTheDocument();
    expect(within(table).getByText("PendingCancel · Reconciliation required")).toBeInTheDocument();
    expect(screen.getByText("1 unresolved order")).toBeInTheDocument();
    const row = within(table).getByText("order-11").closest("tr")!;
    expect(within(row).getAllByText("Unknown").length).toBeGreaterThan(0);
    await user.click(within(row).getByText("Observation details"));
    expect(within(row).getByText(/replace-candidate/)).toBeVisible();
    expect(read).toHaveBeenCalledWith("incident-1", "account-a", { signal: expect.any(AbortSignal) });
  });

  it("exports fractional quantities without displaying zero and keeps unknown or oversized decimals explicit in XLSX", async () => {
    list.mockResolvedValue(history);
    read.mockResolvedValue({ ...incident, summary: { ...summary, orderCount: 3 }, orders: [
      { ...incident.orders[0]!, filledQuantity: "0.000000001", remainingQuantity: "0.0000000000000000000000000001" },
      { ...incident.orders[11]!, filledQuantity: null, remainingQuantity: null },
      { ...incident.orders[1]!, filledQuantity: "9007199254740993.123456789", remainingQuantity: "1" }
    ] });
    vi.mocked(downloadShownXlsx).mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<CancelAllIncidentPanel fundAccountId="account-a" />);
    await screen.findByLabelText("Cancel-all order outcomes");
    await user.click(screen.getByLabelText("Cancel-all order outcomes more table actions"));
    await user.click(screen.getByRole("button", { name: "Export as shown · XLSX" }));

    await waitFor(() => expect(downloadShownXlsx).toHaveBeenCalledTimes(1));
    const [filename, model, context, sheetName] = vi.mocked(downloadShownXlsx).mock.calls[0]!;
    expect(filename).toMatch(/^trading-cancel-incident-orders-.*\.xlsx$/);
    expect(context).toMatchObject({ fundAccountId: "account-a", incidentId: "incident-1", evidenceStatus: "Complete" });
    const filledColumn = model.headers.indexOf("Filled quantity");
    const remainingColumn = model.headers.indexOf("Possible remaining");
    expect(filledColumn).toBeGreaterThanOrEqual(0);
    expect(remainingColumn).toBeGreaterThanOrEqual(0);
    const sheets = shownExportXlsxSheets(model, context, sheetName);
    const orders = sheets[0]!.data;
    expect(orders[1]![filledColumn]).toEqual({ value: 0.000000001, type: Number, format: "0.############################" });
    expect(orders[1]![remainingColumn]).toEqual({ value: 0.0000000000000000000000000001, type: Number, format: "0.############################" });
    expect(orders[2]![filledColumn]).toEqual({ value: "Unknown", type: String });
    expect(orders[2]![remainingColumn]).toEqual({ value: "Unknown", type: String });
    expect(orders[3]![filledColumn]).toEqual({ value: "9007199254740993.123456789", type: String });
  });

  it("keeps missing broker coverage and interrupted retention visible", async () => {
    list.mockResolvedValue(history);
    read.mockResolvedValue({ ...incident, summary: { ...summary, evidenceStatus: "Interrupted", completedAt: null,
      evidenceError: "Completion was not retained.", brokerViewUnavailable: true }, brokerViewError: "Broker enumeration failed." });
    render(<CancelAllIncidentPanel fundAccountId="account-a" />);
    expect(await screen.findByText("Evidence interrupted")).toBeInTheDocument();
    expect(screen.getByText("No retained completion")).toBeInTheDocument();
    expect(screen.getByText("Completion was not retained.")).toBeInTheDocument();
    expect(screen.getByText("Broker enumeration failed.")).toBeInTheDocument();
  });

  it("distinguishes a failed history read from a genuinely empty retained history", async () => {
    list.mockRejectedValue(new Error("Evidence service is unavailable."));
    const user = userEvent.setup();
    render(<CancelAllIncidentPanel fundAccountId="account-a" />);
    expect(await screen.findByText("Cancel-all history unavailable")).toBeInTheDocument();
    expect(screen.queryByText("No retained incidents for this account")).not.toBeInTheDocument();
    list.mockResolvedValue({ ...history, returned: 0, incidents: [] });
    await user.click(screen.getByRole("button", { name: "Retry loading cancel-all incident history" }));
    expect(await screen.findByText("No retained incidents for this account")).toBeInTheDocument();
    expect(screen.getByText(/does not establish that the account has no working orders/)).toBeInTheDocument();
  });

  it("discards delayed history after the selected account changes", async () => {
    let finishOld!: (value: CancelAllIncidentHistory) => void;
    list.mockImplementation(accountId => accountId === "account-a" ? new Promise(resolve => { finishOld = resolve; })
      : Promise.resolve({ ...history, incidents: [{ ...summary, incidentId: "incident-b", fundAccountId: "account-b" }] }));
    read.mockResolvedValue({ ...incident, summary: { ...summary, incidentId: "incident-b", fundAccountId: "account-b" } });
    const view = render(<CancelAllIncidentPanel fundAccountId="account-a" />);
    const oldSignal = list.mock.calls[0]![1]!.signal!;
    view.rerender(<CancelAllIncidentPanel fundAccountId="account-b" />);
    await screen.findByLabelText("Cancel-all order outcomes");
    await act(async () => finishOld(history));
    expect(oldSignal.aborted).toBe(true);
    expect(read).toHaveBeenCalledTimes(1);
    expect(read).toHaveBeenCalledWith("incident-b", "account-b", { signal: expect.any(AbortSignal) });
  });

  it("opens older incidents by exact identity and suppresses a previous delayed detail", async () => {
    let finishOld!: (value: CancelAllIncident) => void;
    list.mockResolvedValue({ ...history, hasMore: true });
    read.mockImplementation(id => id === "incident-1" ? new Promise(resolve => { finishOld = resolve; })
      : Promise.resolve({ ...incident, summary: { ...summary, incidentId: id, actor: "older-operator" } }));
    const user = userEvent.setup();
    render(<CancelAllIncidentPanel fundAccountId="account-a" />);
    await waitFor(() => expect(read).toHaveBeenCalledTimes(1));
    const oldSignal = read.mock.calls[0]![2]!.signal!;
    await user.click(screen.getByText("Find an older incident"));
    await user.type(screen.getByLabelText("Retained incident ID"), "retained-older");
    await user.click(screen.getByRole("button", { name: "Open incident" }));
    await screen.findByText(/older-operator/);
    await act(async () => finishOld(incident));
    expect(oldSignal.aborted).toBe(true);
    expect(screen.getByText(/older-operator/)).toBeInTheDocument();
    expect(read).toHaveBeenLastCalledWith("retained-older", "account-a", { signal: expect.any(AbortSignal) });
  });
});

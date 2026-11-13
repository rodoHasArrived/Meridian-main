import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { EntityInspectorProvider, EntityLink, type EntityInspectorRecord } from "./entity-inspector";
import { getFinancialRecordExplorerRecord } from "@/lib/api";
import type { FinancialRecordExplorerDto, FinancialRecordExplorerSelectedRecordDto } from "@/types";
vi.mock("@/lib/api", () => ({ getFinancialRecordExplorerRecord: vi.fn() }));
const session = {};
const record: EntityInspectorRecord = {
  kind: "symbol", id: "MSFT", label: "MSFT", workspaceHref: "/portfolio?symbol=MSFT",
  summary: [{ label: "Quantity", value: "20" }]
};
const detail: FinancialRecordExplorerSelectedRecordDto = { recordId: "exact-record", recordType: "Security", title: "MSFT", subtitle: "Loaded security", description: "Security record", tone: "Info", fields: [], proofActions: [], usedIn: [], impacts: [], fullRecordHref: "/accounting/security-master/detail" };
const explorer: FinancialRecordExplorerDto = { explorerId: "security-instrument", title: "Securities", description: "", sourceState: "Loaded", isBlocked: false, blockedReason: "", scopeItems: [], savedViews: [], summaryItems: [], filters: [], columns: [], rows: [{ recordId: detail.recordId, recordType: "Security", label: "MSFT", source: "Shared service", status: "Loaded", tone: "Info", cells: [], detail }], selectedRecord: detail, proofActions: [], recordGraph: { nodes: [], edges: [] } };
function Harness({ scopeKey = "scope-a", sessionIdentity = session, selected = record }: { scopeKey?: string; sessionIdentity?: unknown; selected?: EntityInspectorRecord }) {
  return <MemoryRouter><EntityInspectorProvider scopeKey={scopeKey} sessionIdentity={sessionIdentity}><EntityLink record={selected} /></EntityInspectorProvider></MemoryRouter>;
}
beforeEach(() => vi.clearAllMocks());

describe("shared entity inspector", () => {
  it("shows loaded facts without inventing history or proof, and restores trigger focus", async () => {
    const user = userEvent.setup();
    const { container } = render(<Harness />);
    const trigger = screen.getByRole("link", { name: "Inspect MSFT" });
    await user.click(trigger);
    expect(screen.getByRole("dialog", { name: "MSFT details" })).toBeInTheDocument();
    expect(screen.getByText("20")).toBeInTheDocument();
    expect(screen.getByText("This source has not supplied activity history.")).toBeInTheDocument();
    expect(screen.getByText(/snapshot does not establish provenance completeness/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open in workspace" })).toHaveAttribute("href", "/portfolio?symbol=MSFT");
    expect(getFinancialRecordExplorerRecord).not.toHaveBeenCalled();
    expect((await axe(container)).violations).toEqual([]);
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });
  it.each(["scope", "session"])("invalidates selected details when %s changes", async (change) => {
    const { rerender } = render(<Harness />);
    await userEvent.click(screen.getByRole("link", { name: "Inspect MSFT" }));
    rerender(<Harness scopeKey={change === "scope" ? "scope-b" : "scope-a"} sessionIdentity={change === "session" ? {} : session} />);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
  it("retains ordinary scoped navigation when no provider is mounted", () => {
    render(<MemoryRouter basename="/workstation" initialEntries={["/workstation/"]}><EntityLink record={record} /></MemoryRouter>);
    expect(screen.getByRole("link", { name: "Inspect MSFT" })).toHaveAttribute("href", "/workstation/portfolio?symbol=MSFT");
  });
  it("leaves modified clicks as ordinary links", () => {
    render(<Harness />);
    fireEvent.click(screen.getByRole("link", { name: "Inspect MSFT" }), { ctrlKey: true });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
  it("withholds mismatched proof responses and disables fixture fallback", async () => {
    vi.mocked(getFinancialRecordExplorerRecord).mockResolvedValue({ ...detail, recordId: "foreign-record" });
    render(<Harness selected={{ ...record, proof: { explorer, recordId: detail.recordId } }} />);
    await userEvent.click(screen.getByRole("link", { name: "Inspect MSFT" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("does not match this exact record");
    expect(getFinancialRecordExplorerRecord).toHaveBeenCalledWith("security-instrument", "exact-record", expect.objectContaining({ allowDevelopmentFallback: false, signal: expect.any(AbortSignal) }));
    expect(screen.queryByRole("region", { name: "MSFT proof detail" })).not.toBeInTheDocument();
  });
  it("refuses proof identities absent from the authorized explorer rows", async () => {
    render(<Harness selected={{ ...record, proof: { explorer, recordId: "guessed-from-label" } }} />);
    await userEvent.click(screen.getByRole("link", { name: "Inspect MSFT" }));
    expect(screen.getByRole("alert")).toHaveTextContent("missing from the authorized source");
    expect(getFinancialRecordExplorerRecord).not.toHaveBeenCalled();
  });
  it("aborts pending proof and cannot resurrect it after a scope change", async () => {
    let resolve!: (value: FinancialRecordExplorerSelectedRecordDto) => void;
    vi.mocked(getFinancialRecordExplorerRecord).mockImplementation(() => new Promise((done) => { resolve = done; }));
    const selected = { ...record, proof: { explorer, recordId: detail.recordId } };
    const { rerender } = render(<Harness selected={selected} />);
    await userEvent.click(screen.getByRole("link", { name: "Inspect MSFT" }));
    await waitFor(() => expect(getFinancialRecordExplorerRecord).toHaveBeenCalled());
    const signal = vi.mocked(getFinancialRecordExplorerRecord).mock.calls[0][2]?.signal;
    rerender(<Harness scopeKey="scope-b" selected={selected} />);
    expect(signal?.aborted).toBe(true);
    await act(async () => resolve(detail));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});

it("renders the existing proof component only after an exact canonical response", async () => {
  vi.mocked(getFinancialRecordExplorerRecord).mockResolvedValue(detail);
  render(<Harness selected={{ ...record, proof: { explorer, recordId: detail.recordId } }} />);
  await userEvent.click(screen.getByRole("link", { name: "Inspect MSFT" }));
  expect(await screen.findByRole("region", { name: "MSFT proof detail" })).toBeInTheDocument();
  expect(screen.getByText("Number Passport")).toBeInTheDocument();
  expect(screen.getByText("Review required: no structured freshness marker")).toBeInTheDocument();
});

it("withholds unsafe navigation and empty identities", () => {
  const { rerender } = render(<MemoryRouter><EntityLink record={{ ...record, id: "" }} /></MemoryRouter>);
  expect(screen.queryByRole("link")).not.toBeInTheDocument();
  rerender(<MemoryRouter><EntityLink record={{ ...record, workspaceHref: "//external.test/" }} /></MemoryRouter>);
  expect(screen.queryByRole("link")).not.toBeInTheDocument();
});

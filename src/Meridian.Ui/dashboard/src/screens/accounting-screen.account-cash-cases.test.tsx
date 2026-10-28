import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as oversight from "@/lib/api/account-oversight.api";
import { ApiError } from "@/lib/api-errors";
import type { AccountCashBalanceSource, AccountCashReconciliationCase, AccountCashReconciliationRequest, AccountCashReconciliationRun, OversightContext, OversightScope } from "@/types/account-oversight";
import { AccountCashCasesPanel } from "./accounting-screen.account-cash-cases";

vi.mock("@/lib/api/account-oversight.api", () => ({
  listCashCases: vi.fn(), runCashComparison: vi.fn(), replayCashRun: vi.fn(), assignCashCase: vi.fn(), parseCashComparisonImport: vi.fn()
}));
const api = vi.mocked(oversight);
const scope: OversightScope = { accountId: "account-a", ledgerBookId: "book-a", baseCurrency: "USD" };
const context: OversightContext = { accountId: "account-a", bookId: "book-a", baseCurrency: "USD", bookName: "Account A", actor: "reviewer", canWrite: true, canReadPrices: true, canManagePrices: true, canProvision: false };
const caseScope = { tenantId: "tenant-a", companyId: "company-a", accountId: scope.accountId, ledgerBookId: scope.ledgerBookId };
const exactAmount = "9007199254740993.0123456789";
function source(sourceId: string, sourceType: 1 | 3): AccountCashBalanceSource {
  return { sourceId, sourceType, version: "source-v2", evidenceReference: `evidence://${sourceId}`, capturedAtUtc: "2026-10-13T10:00:00Z", completePopulation: true, rows: [{ row: { cashEntryId: `${sourceId}-row`, accountId: scope.accountId, amount: exactAmount, currency: "USD", amountBase: exactAmount, baseCurrency: "USD", postedAtUtc: "2026-10-13T09:00:00Z", accountingPeriod: "2026-10-13", sourceReference: `source://${sourceId}` }, originalPayload: `original ${sourceId} balance ${exactAmount}` }] };
}
function request(runId = "run-2"): AccountCashReconciliationRequest {
  return { runId, scope: caseScope, businessDate: "2026-10-13", observedAtUtc: "2026-10-13T11:00:00Z", left: source("custodian", 1), right: source("ledger", 3), tolerance: { profileId: "cash-pilot", version: 4, cashRules: [{ ruleId: "absolute-only", absoluteCashTolerance: "0.01", basisPointCashTolerance: null, settlementDateTolerance: "00:05:00" }], positionRules: [], transactionRules: [] }, initialOwner: "analyst-a" };
}
function run(runId = "run-2"): AccountCashReconciliationRun {
  return { input: request(runId), inputHash: `input-hash-${runId}`, resultHash: `result-hash-${runId}`, comparisons: [{ currency: "USD", observationState: "SourceCleared", matches: [], breaks: [], evidence: [{ evidenceId: "proof-a", ruleId: "absolute-only", stage: "Exact", narrative: "Frozen balances agree.", numericDelta: "0.00", attributes: { source: "retained" } }] }] };
}
function cashCase(overrides: Partial<AccountCashReconciliationCase> = {}): AccountCashReconciliationCase {
  return { caseId: "stable-cash-case", scope: caseScope, leftSourceId: "custodian", rightSourceId: "ledger", currency: "USD", owner: "analyst-a", version: 3, firstObservedAtUtc: "2026-10-12T11:00:00Z", lastObservedAtUtc: "2026-10-13T11:00:00Z", state: "SourceCleared", resolvedAtUtc: "2026-10-13T11:00:00Z", resolutionRunId: "run-2", agingDays: 1, history: [{ runId: "run-1", observedAtUtc: "2026-10-12T11:00:00Z", state: "Open", resultHash: "result-hash-run-1" }, { runId: "run-2", observedAtUtc: "2026-10-13T11:00:00Z", state: "SourceCleared", resultHash: "result-hash-run-2" }], assignments: [], ...overrides };
}
async function selectCase() {
  await userEvent.click(await screen.findByRole("row", { name: /USD cash case stable-cash-case/ }));
  await screen.findByRole("region", { name: "Cash case stable-cash-case" });
}
async function importFile() {
  await userEvent.click(screen.getByText("Import next cash comparison"));
  await userEvent.upload(screen.getByLabelText("Reconciliation handoff (.json)"), new File(['{"typed":"handoff"}'], "cash-handoff.json", { type: "application/json" }));
  return screen.findByRole("region", { name: "Cash comparison preview" });
}
beforeEach(() => {
  vi.resetAllMocks();
  api.listCashCases.mockResolvedValue([cashCase()]);
  api.replayCashRun.mockImplementation(async (_scope, runId) => run(runId));
  api.parseCashComparisonImport.mockImplementation(() => request());
  api.runCashComparison.mockResolvedValue(run());
  api.assignCashCase.mockResolvedValue(cashCase({ owner: "analyst-b", version: 4, assignments: [{ owner: "analyst-b", actor: "reviewer", reason: "Review corrected balance", assignedAtUtc: "2026-10-14T00:00:00Z" }] }));
});

describe("account cash continuity operator panel", () => {
  it("shows retained case identity, resolution and executed tolerance with accessible empty/read states", async () => {
    const { container } = render(<AccountCashCasesPanel scope={scope} context={context} />);
    await screen.findByText("cash-pilot · v4");
    await selectCase();
    expect(screen.getByText("1 day")).toBeInTheDocument();
    expect(screen.getByText("2026-10-13T11:00:00Z · run run-2")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("previews exact source rows and tolerance before capture, then preserves the same run identity on failed retry", async () => {
    const dirty = vi.fn();
    api.runCashComparison.mockRejectedValueOnce(new Error("Source store unavailable"));
    render(<AccountCashCasesPanel scope={scope} context={context} onDirtyChange={dirty} />);
    await screen.findByText("cash-pilot · v4");
    const preview = within(await importFile());
    expect(api.parseCashComparisonImport).toHaveBeenCalledWith('{"typed":"handoff"}', scope);
    expect(api.runCashComparison).not.toHaveBeenCalled();
    expect(preview.getAllByText(`${exactAmount} USD`)).toHaveLength(4);
    expect(preview.getByText("run-2")).toBeInTheDocument();
    expect(dirty).toHaveBeenLastCalledWith(true);
    await userEvent.click(preview.getByRole("button", { name: "Capture cash comparison" }));
    await screen.findByText(/The imported run identity is retained for a retry/);
    expect(screen.getByRole("row", { name: /USD cash case stable-cash-case/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Capture cash comparison" }));
    await waitFor(() => expect(api.runCashComparison).toHaveBeenCalledTimes(2));
    const first = api.runCashComparison.mock.calls[0][1];
    expect(api.runCashComparison.mock.calls[1][1]).toBe(first);
    expect(first.left.rows[0].row.amount).toBe(exactAmount);
    await screen.findByText("Comparison run-2 retained. Cash cases are refreshing.");
    expect(screen.queryByRole("region", { name: "Cash comparison preview" })).not.toBeInTheDocument();
  });

  it("sends expected case version and assignment rationale, then displays retained actor history", async () => {
    render(<AccountCashCasesPanel scope={scope} context={context} />);
    await selectCase();
    const form = within(screen.getByRole("form", { name: "Assign cash case owner" }));
    await userEvent.clear(form.getByLabelText("Case owner"));
    await userEvent.type(form.getByLabelText("Case owner"), "analyst-b");
    await userEvent.type(form.getByLabelText("Assignment rationale"), "Review corrected balance");
    await userEvent.click(form.getByRole("button", { name: "Retain owner assignment" }));
    await waitFor(() => expect(api.assignCashCase).toHaveBeenCalledWith(scope, "stable-cash-case", { expectedVersion: 3, owner: "analyst-b", reason: "Review corrected balance" }, expect.objectContaining({ signal: expect.any(AbortSignal) })));
    await screen.findByText("Owner assignment retained for stable-cash-case.");
    await userEvent.click(screen.getByRole("tab", { name: "Owner history" }));
    expect(screen.getByText("Review corrected balance")).toBeVisible();
    expect(screen.getByText(/by reviewer/)).toBeVisible();
  });

  it("discards an unsaved assignment draft without retaining it", async () => {
    render(<AccountCashCasesPanel scope={scope} context={context} />);
    await selectCase();
    const form = within(screen.getByRole("form", { name: "Assign cash case owner" }));
    await userEvent.clear(form.getByLabelText("Case owner"));
    await userEvent.type(form.getByLabelText("Case owner"), "analyst-b");
    await userEvent.type(form.getByLabelText("Assignment rationale"), "Draft that should not be kept");
    await userEvent.click(form.getByRole("button", { name: "Discard assignment" }));
    expect(api.assignCashCase).not.toHaveBeenCalled();
    const reset = within(screen.getByRole("form", { name: "Assign cash case owner" }));
    expect(reset.getByLabelText("Case owner")).toHaveValue("analyst-a");
    expect(reset.getByLabelText("Assignment rationale")).toHaveValue("");
    expect(reset.queryByRole("button", { name: "Discard assignment" })).not.toBeInTheDocument();
  });

  it("blocks a stale assignment until the operator refreshes retained case versions", async () => {
    api.assignCashCase.mockRejectedValueOnce(new ApiError({ path: "/cash-cases", status: 409, detail: "Version conflict" }));
    render(<AccountCashCasesPanel scope={scope} context={context} />);
    await selectCase();
    await userEvent.type(screen.getByLabelText("Assignment rationale"), "Check updated owner");
    await userEvent.click(screen.getByRole("button", { name: "Retain owner assignment" }));
    await screen.findByText(/This case changed after it was loaded/);
    expect(screen.getByRole("button", { name: "Retain owner assignment" })).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: "Refresh retained cases" }));
    await waitFor(() => expect(api.listCashCases).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByRole("button", { name: "Retain owner assignment" })).toBeEnabled());
  });

  it("blocks capturing a comparison while an owner assignment draft is unsaved", async () => {
    render(<AccountCashCasesPanel scope={scope} context={context} />);
    await selectCase();
    await userEvent.type(screen.getByLabelText("Assignment rationale"), "Pending owner note");
    const preview = within(await importFile());
    expect(preview.getByRole("button", { name: "Capture cash comparison" })).toBeDisabled();
    expect(screen.getByText("Save or discard the owner assignment before selecting another case or capturing a comparison.")).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Discard assignment" }));
    await waitFor(() => expect(preview.getByRole("button", { name: "Capture cash comparison" })).toBeEnabled());
    await userEvent.click(preview.getByRole("button", { name: "Capture cash comparison" }));
    await waitFor(() => expect(api.runCashComparison).toHaveBeenCalledOnce());
  });

  it("reproduces an explicitly selected older run and exposes frozen rows, rule version and hashes", async () => {
    render(<AccountCashCasesPanel scope={scope} context={context} />);
    await selectCase();
    await userEvent.selectOptions(screen.getByLabelText("Retained observation"), "run-1");
    await userEvent.click(screen.getByRole("button", { name: "Reproduce retained run" }));
    await screen.findByText("Retained run run-1 reproduced from its frozen source rows and tolerance version.");
    expect(api.replayCashRun).toHaveBeenLastCalledWith(scope, "run-1", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    const detail = within(screen.getByRole("region", { name: "Retained comparison run-1" }));
    expect(detail.getByText("input-hash-run-1")).toBeInTheDocument();
    expect(detail.getByText("result-hash-run-1")).toBeInTheDocument();
    expect(detail.getByText("Executed tolerance cash-pilot · version 4")).toBeInTheDocument();
    await userEvent.click(detail.getByText("Left original source payloads and row references"));
    expect(detail.getByText(`original custodian balance ${exactAmount}`)).toBeVisible();
  });

  it("retains authoritative reads and replay while write controls are disabled", async () => {
    render(<AccountCashCasesPanel scope={scope} context={{ ...context, canWrite: false }} />);
    await screen.findByText("Read-only cash oversight");
    await selectCase();
    expect(screen.getByLabelText("Case owner")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Retain owner assignment" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reproduce retained run" })).toBeEnabled();
    await userEvent.click(screen.getByText("Import next cash comparison"));
    expect(screen.getByLabelText("Reconciliation handoff (.json)")).toBeDisabled();
    expect(api.runCashComparison).not.toHaveBeenCalled();
  });

  it("aborts old scope reads and ignores late responses after switching accounts", async () => {
    let finish!: (value: AccountCashReconciliationCase[]) => void;
    api.listCashCases.mockReturnValueOnce(new Promise(resolve => { finish = resolve; })).mockResolvedValueOnce([]);
    const { rerender } = render(<AccountCashCasesPanel scope={scope} context={context} />);
    const signal = api.listCashCases.mock.calls[0][1]?.signal;
    const otherScope = { ...scope, accountId: "account-b" };
    rerender(<AccountCashCasesPanel scope={otherScope} context={{ ...context, accountId: "account-b" }} />);
    await screen.findByText(/No retained cash cases for this account and book/);
    expect(signal?.aborted).toBe(true);
    await act(async () => finish([cashCase()]));
    expect(screen.queryByText("stable-cash-case")).not.toBeInTheDocument();
    expect(api.replayCashRun).not.toHaveBeenCalled();
  });

  it("rejects foreign account case responses and keeps existing rows when a later refresh fails", async () => {
    api.listCashCases.mockResolvedValueOnce([cashCase()]).mockResolvedValueOnce([cashCase({ scope: { ...caseScope, accountId: "foreign" } })]).mockRejectedValueOnce(new Error("Retained store unavailable"));
    render(<AccountCashCasesPanel scope={scope} context={context} />);
    await screen.findByText("cash-pilot · v4");
    await userEvent.click(screen.getByRole("button", { name: "Refresh retained cases" }));
    await screen.findByText(/Cash cases belong to a different account or ledger book/);
    expect(screen.getByRole("row", { name: /USD cash case stable-cash-case/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Refresh retained cases" }));
    await screen.findByText("Retained store unavailable");
    expect(screen.getByRole("row", { name: /USD cash case stable-cash-case/ })).toBeInTheDocument();
  });

  it("shows import validation errors without changing retained rows or issuing a mutation", async () => {
    api.parseCashComparisonImport.mockImplementation(() => { throw new Error("Every source row must belong to the selected account."); });
    render(<AccountCashCasesPanel scope={scope} context={context} />);
    await screen.findByText("cash-pilot · v4");
    await userEvent.click(screen.getByText("Import next cash comparison"));
    await userEvent.upload(screen.getByLabelText("Reconciliation handoff (.json)"), new File(["{}"], "wrong-account.json", { type: "application/json" }));
    await screen.findByText("Every source row must belong to the selected account.");
    expect(screen.getByRole("row", { name: /USD cash case stable-cash-case/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Capture cash comparison" })).not.toBeInTheDocument();
    expect(api.runCashComparison).not.toHaveBeenCalled();
  });

  it("reports unavailable durable history without presenting an empty-case assertion", async () => {
    api.listCashCases.mockRejectedValue(new ApiError({ path: "/cash-cases", status: 503, detail: "Durable cash history requires provisioning." }));
    render(<AccountCashCasesPanel scope={scope} context={context} />);
    await screen.findByText("Retained cash cases could not be loaded. Refresh retained cases to retry.");
    expect(screen.queryByText(/No retained cash cases for this account and book/)).not.toBeInTheDocument();
    expect(screen.queryByText("0 retained cash cases")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh retained cases" })).toBeEnabled();
  });
});

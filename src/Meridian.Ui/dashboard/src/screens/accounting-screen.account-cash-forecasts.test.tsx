import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import * as oversightApi from "@/lib/api/account-oversight.api";
import { ApiError } from "@/lib/api-errors";
import { AccountCashForecastsPanel } from "./accounting-screen.account-cash-forecasts";
import type {
  AccountCashForecastRequest, AccountCashForecastSnapshot, AccountCashProvenance,
  OversightContext, OversightScope
} from "@/types/account-oversight";

vi.mock("@/lib/api/account-oversight.api", () => ({
  listCashForecasts: vi.fn(), getCashForecast: vi.fn(), captureCashForecast: vi.fn(),
  parseCashForecastImport: vi.fn((text: string) => JSON.parse(text))
}));
const api = vi.mocked(oversightApi);
const scope: OversightScope = {
  accountId: "2b281f0a-b5d9-42db-a4e7-57e053434880", ledgerBookId: "16a99fb5-adcb-449e-9a8e-f57a45fda650", baseCurrency: "USD"
};
const context: OversightContext = {
  accountId: scope.accountId, bookId: scope.ledgerBookId, baseCurrency: "USD", bookName: "Account primary",
  actor: "controller@example.com", canWrite: true, canReadPrices: true, canManagePrices: true, canProvision: false
};
const provenance: AccountCashProvenance = {
  sourceDomain: "EvidenceVault", sourceEntityId: "source-coupon-v1", sourceContentHash: "a".repeat(64), role: 0,
  evidence: {
    evidenceId: "doc:ev-coupon", evidenceUri: "evidence://evidence-vault/ev-coupon", contentHashSha256: "b".repeat(64),
    sourceSystem: "Custodian", sourceReference: "coupon-receipt-v1", reviewStatus: "Accepted", reviewedBy: "reviewer@example.com",
    reviewedAtUtc: "2026-10-11T10:00:00Z", effectiveDate: "2026-10-10", evidenceVersion: 3,
    retainedAtUtc: "2026-10-11T10:00:00Z", retainedBy: "reviewer@example.com", subjectType: "account", subjectId: scope.accountId
  }
};

function request(overrides: Partial<AccountCashForecastRequest> = {}): AccountCashForecastRequest {
  return {
    runId: "cash-run-1", scope: { tenantId: "tenant", companyId: "company", accountId: scope.accountId, bookId: scope.ledgerBookId, baseCurrency: "USD" },
    periodStart: "2026-10-01", periodEnd: "2026-10-12", recordedAtUtc: "2026-10-13T10:00:00Z",
    openingCash: { accountId: scope.accountId, bookId: scope.ledgerBookId, amount: "9007199254740993.37", currency: "USD", asOfDate: "2026-10-01", isSettled: true, provenance },
    closingCash: null,
    flows: [{
      flowId: "coupon-1", versionId: "coupon-expected-v1", accountId: scope.accountId, bookId: scope.ledgerBookId,
      status: 0, flowType: "Coupon", amount: "125.1275", currency: "EUR", economicDate: "2026-10-08", settlementDate: "2026-10-09", cashReceiptDate: "2026-10-10",
      provenance: { ...provenance, role: 1 }, securityId: "security-1", projectionRunId: "projection-v1",
      projectionEngineVersion: "factor-engine-v1", termsVersionId: "terms-v1", termsHash: "factor-terms-hash-v1"
    }],
    fxConversions: [{ currency: "EUR", baseCurrency: "USD", conversionDate: "2026-10-10", baseUnitsPerCurrencyUnit: "1.08423715", provenance: { ...provenance, role: 2 } }],
    minimumCashThreshold: "10.00", bucketDays: 1, ...overrides
  };
}

function snapshot(inputs = request(), overrides: Partial<AccountCashForecastSnapshot> = {}): AccountCashForecastSnapshot {
  return {
    snapshotId: "snapshot-1", evidenceHash: "c".repeat(64), engineVersion: "account-cash-v1", inputs,
    openingBaseCash: "9007199254740993.37", actualNetBaseFlow: "0.00", reconciledClosingBaseCash: "9007199254740993.37",
    observedClosingBaseCash: null, closingVariance: null, isCertified: false,
    blockers: [{ code: "CLOSING_CASH_MISSING", message: "Closing settled cash observation is required." }],
    forecastLadder: {
      engineVersion: "cash-ladder-v1", asOfDate: "2026-10-01", horizonDays: 12, baseCurrency: "USD", scenarioId: "base", openingCash: "9007199254740993.37", minimumCashThreshold: "10.00",
      buckets: [{ bucketStart: "2026-10-10", bucketEnd: "2026-10-10", label: "October 10", inflows: "135.664386886625", outflows: "0", netFlow: "135.664386886625", cumulativeCash: "9007199254741129.034386886625", breachesMinimumBalance: false, sources: [] }],
      contributions: [{ securityId: "security-1", displayName: "Coupon expected", assetClass: "Bond", projectionRunId: "projection-v1", projectionEngineVersion: "factor-engine-v1", termsVersionId: "terms-v1", termsHash: "factor-terms-hash-v1", sourceDomain: "Factors", sourceEntityId: "coupon-1", flowType: "Coupon", sourceLane: "Expected", dueDate: "2026-10-10", amount: "125.1275", currency: "EUR", baseAmount: "135.664386886625", scenarioAdjustment: null }],
      availableScenarios: [], assumptions: ["Retained factor prediction"], warnings: [], generatedAt: "2026-10-13T10:00:00Z", securitiesEvaluated: 1, securitiesWithFlows: 1, isDecisionReady: false, blockingReasons: ["Closing cash is missing."]
    }, ...overrides
  };
}

function view(nextScope = scope, nextContext = context, onDirtyChange?: (dirty: boolean) => void) {
  return <MemoryRouter><AccountCashForecastsPanel scope={nextScope} context={nextContext} onDirtyChange={onDirtyChange} /></MemoryRouter>;
}

function handoffFile(input: AccountCashForecastRequest) {
  const text = JSON.stringify(input);
  const file = new File([text], "cash-handoff.json", { type: "application/json" });
  Object.defineProperty(file, "text", { value: vi.fn().mockResolvedValue(text) });
  return file;
}

async function importHandoff(user: ReturnType<typeof userEvent.setup>, input = request()) {
  await user.click(screen.getByText("Capture cash forecast from source handoff"));
  await user.upload(screen.getByLabelText("Cash forecast handoff file"), handoffFile(input));
  return screen.findByRole("region", { name: "Cash forecast handoff preview" });
}

beforeEach(() => {
  api.listCashForecasts.mockResolvedValue([]);
});
afterEach(() => vi.clearAllMocks());

describe("AccountCashForecastsPanel", () => {
  it("previews an imported scoped handoff and captures only after explicit review", async () => {
    const user = userEvent.setup();
    const input = request();
    const dirty = vi.fn();
    api.captureCashForecast.mockResolvedValue(snapshot(input));
    render(view(scope, context, dirty));
    await screen.findByText("No cash forecasts retained for this account and book.");
    const preview = await importHandoff(user, input);

    expect(within(preview).getByText("9007199254740993.37 USD · 2026-10-01")).toBeInTheDocument();
    expect(within(preview).getByText("125.1275 EUR")).toBeInTheDocument();
    expect(within(preview).getByText("Preview has no certification result. Only validated server evidence can reconcile cash.")).toBeInTheDocument();
    expect(api.parseCashForecastImport).toHaveBeenCalledWith(JSON.stringify(input), scope);
    expect(api.captureCashForecast).not.toHaveBeenCalled();
    expect(dirty).toHaveBeenLastCalledWith(true);

    await user.click(screen.getByRole("button", { name: "Capture cash forecast" }));
    await waitFor(() => expect(api.captureCashForecast).toHaveBeenCalledWith(scope, input, { signal: expect.any(AbortSignal) }));
    expect(await screen.findByText("Cash forecast captured. Earlier predictions and their source revisions remain retained.")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Cash forecast handoff preview" })).not.toBeInTheDocument();
    expect(dirty).toHaveBeenLastCalledWith(false);
  });

  it("shows server certification blockers, separate dates, dated FX and retained factor provenance", async () => {
    api.listCashForecasts.mockResolvedValue([snapshot()]);
    render(view());
    expect(await screen.findByText("Cash reconciliation blocked")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Cash certification blockers")).getByText(/Closing settled cash observation is required/)).toBeInTheDocument();
    const flows = screen.getByRole("table", { name: "Expected and actual cash flows" });
    expect(within(flows).getByRole("columnheader", { name: "Economic date" })).toBeInTheDocument();
    expect(within(flows).getByText("2026-10-08")).toBeInTheDocument();
    expect(within(flows).getByText("2026-10-09")).toBeInTheDocument();
    expect(within(flows).getByText("2026-10-10")).toBeInTheDocument();
    expect(within(flows).getByText(/coupon-1 · Expected/)).toBeInTheDocument();
    expect(within(flows).getByText("factor-terms-hash-v1", { exact: false })).toBeInTheDocument();
    expect(screen.getByText("1.08423715")).toBeInTheDocument();
    expect(screen.getByText("9007199254741129.034386886625")).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "Inspect evidence and review", hidden: true })[0]).toHaveAttribute("href", `/reporting/evidence?subjectKind=account&subjectId=${scope.accountId}`);
    expect(screen.getAllByText("135.664386886625").length).toBeGreaterThan(0);
    expect(screen.getByText("Select an earlier retained run to inspect its original predictions. Corrections append a linked version and preserve the earlier inputs.")).toBeInTheDocument();
  });

  it("retains earlier predictions when a linked factor correction is inspected", async () => {
    const user = userEvent.setup();
    const original = snapshot();
    const updatedInput = request({ runId: "cash-run-2", previousSnapshotId: "snapshot-1", correctionRationale: "Factor restated", flows: [{ ...request().flows[0], versionId: "coupon-expected-v2", supersedesVersionId: "coupon-expected-v1", amount: "140.25", termsVersionId: "terms-v2" }] });
    const correction = snapshot(updatedInput, { snapshotId: "snapshot-2" });
    api.listCashForecasts.mockResolvedValue([original, correction]);
    api.getCashForecast.mockResolvedValue(original);
    render(view());
    expect(await screen.findByText("Linked correction of snapshot-1: Factor restated")).toBeInTheDocument();
    expect(screen.getByText("140.25 EUR")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Retained cash forecast"), "snapshot-1");
    expect(await screen.findByText("Version coupon-expected-v1")).toBeInTheDocument();
    expect(screen.queryByText("Version coupon-expected-v2")).not.toBeInTheDocument();
    expect(within(screen.getByRole("table", { name: "Expected and actual cash flows" })).getByText("125.1275 EUR")).toBeInTheDocument();
    expect(api.getCashForecast).toHaveBeenCalledWith(scope, "snapshot-1", { signal: expect.any(AbortSignal) });
  });

  it("exposes retained history in read-only sessions without capture controls", async () => {
    api.listCashForecasts.mockResolvedValue([snapshot()]);
    render(view(scope, { ...context, canWrite: false }));
    expect(await screen.findByText("Cash reconciliation blocked")).toBeInTheDocument();
    expect(screen.getByText("Read-only cash forecasts")).toBeInTheDocument();
    expect(screen.queryByLabelText("Cash forecast handoff file")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Capture cash forecast" })).not.toBeInTheDocument();
  });

  it("shows certification and actual cash receipts only when retained by the server", async () => {
    const input = request();
    input.flows.push({ ...input.flows[0], versionId: "coupon-actual-v2", status: 1, amount: "50.00", provenance });
    api.listCashForecasts.mockResolvedValue([snapshot(input, {
      isCertified: true, blockers: [], openingBaseCash: "1000.00", actualNetBaseFlow: "50.00",
      reconciledClosingBaseCash: "1050.00", observedClosingBaseCash: "1050.00", closingVariance: "0.00"
    })]);
    render(view());
    expect(await screen.findByText("Cash reconciliation certified")).toBeInTheDocument();
    const flows = screen.getByRole("table", { name: "Expected and actual cash flows" });
    expect(within(flows).getByText("coupon-1 · Actual receipt")).toBeInTheDocument();
    expect(within(flows).getByText("coupon-1 · Expected")).toBeInTheDocument();
    expect(screen.getByText("0.00 USD")).toBeInTheDocument();
    expect(screen.queryByLabelText("Cash certification blockers")).not.toBeInTheDocument();
  });

  it("provides a scope-filled handoff template with empty required financial evidence", async () => {
    const user = userEvent.setup();
    render(view());
    await screen.findByText("No cash forecasts retained for this account and book.");
    await user.click(screen.getByText("Capture cash forecast from source handoff"));
    const link = screen.getByRole("link", { name: "Download scoped handoff template" });
    const template = JSON.parse(decodeURIComponent(link.getAttribute("href")!.split(",")[1]));
    expect(template.scope).toEqual({ tenantId: "", companyId: "", accountId: scope.accountId, bookId: scope.ledgerBookId, baseCurrency: "USD" });
    expect(template.openingCash.amount).toBe("");
    expect(template.openingCash.isSettled).toBeNull();
    expect(template.openingCash.provenance.evidence.evidenceId).toBe("");
    expect(template.runId).toBe("");
    expect(api.captureCashForecast).not.toHaveBeenCalled();
  });

  it("preserves the imported request identity across an unavailable-service retry", async () => {
    const user = userEvent.setup();
    const input = request();
    api.captureCashForecast.mockRejectedValueOnce(new ApiError({ path: "/cash-forecasts", status: 503, detail: "Durable forecast store requires recovery." })).mockResolvedValueOnce(snapshot(input));
    render(view());
    await screen.findByText("No cash forecasts retained for this account and book.");
    await importHandoff(user, input);
    await user.click(screen.getByRole("button", { name: "Capture cash forecast" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Durable forecast store requires recovery.");
    expect(screen.getByRole("region", { name: "Cash forecast handoff preview" })).toHaveTextContent("Run cash-run-1");
    await user.click(screen.getByRole("button", { name: "Keep handoff and retry" }));
    await user.click(screen.getByRole("button", { name: "Capture cash forecast" }));
    await screen.findByText("Cash forecast captured. Earlier predictions and their source revisions remain retained.");
    expect(api.captureCashForecast.mock.calls.map(call => call[1])).toEqual([input, input]);
  });

  it("blocks a correction whose handoff omits the latest period snapshot", async () => {
    const user = userEvent.setup();
    api.listCashForecasts.mockResolvedValue([snapshot()]);
    render(view());
    await screen.findByText("Cash reconciliation blocked");
    await importHandoff(user, request({ runId: "cash-run-2" }));
    expect(screen.getByText(/Correction must link the latest retained snapshot for this period: snapshot-1/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Capture cash forecast" })).toBeDisabled();
    expect(api.captureCashForecast).not.toHaveBeenCalled();
  });

  it("blocks capture while authoritative history is unavailable even if a file can be previewed", async () => {
    const user = userEvent.setup();
    api.listCashForecasts.mockRejectedValueOnce(new Error("History read unavailable"));
    render(view());
    await screen.findByRole("alert");
    await importHandoff(user);
    expect(screen.getByText("Refresh authoritative cash history before capturing. Correction linkage cannot be verified while history is unavailable.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Capture cash forecast" })).toBeDisabled();
    expect(api.captureCashForecast).not.toHaveBeenCalled();
  });

  it("aborts and ignores late history from a different account", async () => {
    let finish!: (value: AccountCashForecastSnapshot[]) => void;
    api.listCashForecasts.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockResolvedValueOnce([]);
    const rendered = render(view());
    const originalSignal = api.listCashForecasts.mock.calls[0][1]?.signal;
    const otherScope = { ...scope, accountId: "eeb2ec51-0493-402e-9581-ecf6e3f6b893" };
    rendered.rerender(view(otherScope, { ...context, accountId: otherScope.accountId }));
    await screen.findByText("No cash forecasts retained for this account and book.");
    await act(async () => { finish([snapshot()]); });
    expect(originalSignal?.aborted).toBe(true);
    expect(screen.queryByText("Version coupon-expected-v1")).not.toBeInTheDocument();
    expect(screen.getByText(/Account eeb2ec51/)).toBeInTheDocument();
  });

  it("discards pending handoffs on account changes and ignores a late file read", async () => {
    const user = userEvent.setup();
    let finish!: (value: string) => void;
    const file = new File(["pending"], "pending.json", { type: "application/json" });
    Object.defineProperty(file, "text", { value: () => new Promise<string>(resolve => { finish = resolve; }) });
    const rendered = render(view());
    await screen.findByText("No cash forecasts retained for this account and book.");
    await user.click(screen.getByText("Capture cash forecast from source handoff"));
    await user.upload(screen.getByLabelText("Cash forecast handoff file"), file);
    const otherScope = { ...scope, accountId: "eeb2ec51-0493-402e-9581-ecf6e3f6b893" };
    rendered.rerender(view(otherScope, { ...context, accountId: otherScope.accountId }));
    await act(async () => { finish(JSON.stringify(request())); });
    expect(screen.queryByRole("region", { name: "Cash forecast handoff preview" })).not.toBeInTheDocument();
    expect(api.parseCashForecastImport).not.toHaveBeenCalled();
    expect(api.captureCashForecast).not.toHaveBeenCalled();
  });
});

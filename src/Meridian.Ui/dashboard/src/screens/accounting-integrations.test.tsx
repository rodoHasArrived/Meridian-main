import { fireEvent, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import * as integrationApi from "@/lib/api/accounting-integration.api";
import type { LedgerBook } from "@/types";
import type { AutomatedPostingGrant, ThreeWayReconciliation } from "@/types/accounting-integration";
import { AutomaticPostingAuthorityPanel } from "./accounting-screen.automation-grant";
import { ThreeWayAccountingPanel } from "./accounting-screen.three-way";
import { GeneralLedgerExportPanel } from "./reporting-screen.general-ledger-export";
import { corporateActionSourceMode, updateCorporateActionSource } from "./quant-lab-corporate-actions";

vi.mock("@/lib/api/accounting-integration.api", () => ({
  listIntegrationBooks: vi.fn(), listIntegrationPeriods: vi.fn(), getIntegrationMapping: vi.fn(),
  activatePostingGrant: vi.fn(), getPostingGrant: vi.fn(), revokePostingGrant: vi.fn(),
  runThreeWayReconciliation: vi.fn(), getLatestThreeWayReconciliation: vi.fn(), downloadGeneralLedger: vi.fn(), getGeneralLedgerExportAccess: vi.fn(), prepareThreeWayAdjustment: vi.fn()
}));
const api = vi.mocked(integrationApi);
const bookId = "11111111-1111-1111-1111-111111111111";
const periodId = "22222222-2222-2222-2222-222222222222";
const accountId = "33333333-3333-3333-3333-333333333333";
const book: LedgerBook = { ledgerBookId: bookId, fundProfileId: "synthetic-fund", fundStructureNodeId: accountId,
  fundStructureNodeKind: "Account", displayName: "Synthetic USD book", baseCurrency: "USD", createdAt: "2026-11-01T00:00:00Z",
  updatedAt: "2026-11-01T00:00:00Z", accountingBasis: "Primary", accountingPolicyId: "synthetic-policy", accountingPolicyVersion: "1" };
const result: ThreeWayReconciliation = {
  runId: "synthetic-run", operationId: "synthetic-operation", cutoffUtc: "2026-11-14T12:00:00Z", recordedAtUtc: "2026-11-14T12:01:00Z", status: "Blocked", dataProvenanceToken: "simulated",
  scope: { tenantId: "synthetic-tenant", companyId: "synthetic-company", fundProfileId: book.fundProfileId,
    entityId: accountId, financialAccountId: accountId, ledgerBookId: bookId, accountingPeriodId: periodId, currency: "USD" },
  sources: [{ source: "Broker", asOfUtc: "2026-11-14T11:59:00Z", evidenceReference: "evidence://synthetic/broker-snapshot", cash: null,
    positions: [], costBasisConvention: "Unknown", coverageFailures: ["Broker cash is unavailable"], dataProvenanceToken: "simulated" }],
  checks: [{ checkId: "synthetic-check", measure: "Cash", symbol: null, leftSource: "Ledger", rightSource: "Broker",
    leftAmount: "1000.00", rightAmount: null, tolerance: "0.01", status: "Blocked", explanation: "Independent broker cash is missing" }], breakIds: ["synthetic-break"]
};
const grant: AutomatedPostingGrant = { reference: { grantId: "44444444-4444-4444-4444-444444444444", version: 1, tenantId: "synthetic-tenant",
  financialAccountId: accountId, ledgerBookId: bookId, periodId, policyId: book.accountingPolicyId, policyVersion: "1", mappingRevision: "mapping-7", sourceMode: "Synthetic" },
  isActive: true, activatedBy: "synthetic-operator", activatedAtUtc: "2026-11-14T12:00:00Z" };

beforeEach(() => {
  api.listIntegrationBooks.mockResolvedValue([book]);
  api.listIntegrationPeriods.mockResolvedValue([{ periodId, ledgerBookId: bookId, label: "November 2026", status: "Open", version: 3 }]);
  api.getIntegrationMapping.mockResolvedValue({ configurationVersion: "mapping-7" });
  api.getGeneralLedgerExportAccess.mockResolvedValue(true);
});
afterEach(() => { vi.resetAllMocks(); vi.unstubAllGlobals(); });

async function selectScope(account = true) {
  fireEvent.click(screen.getByRole("button", { name: "Load ledger scope" }));
  await screen.findByRole("option", { name: "Synthetic USD book · USD" });
  fireEvent.change(screen.getByLabelText("Ledger book"), { target: { value: bookId } });
  await screen.findByRole("option", { name: "November 2026 · Open" });
  fireEvent.change(screen.getByLabelText("Accounting period"), { target: { value: periodId } });
  if (account) fireEvent.change(screen.getByLabelText("Financial account ID"), { target: { value: accountId } });
}

describe("accounting integration panels", () => {
  it("compares the selected exact scope and displays missing operands, coverage and retained case links", async () => {
    api.runThreeWayReconciliation.mockResolvedValue(result);
    renderWithRouter(<ThreeWayAccountingPanel />);
    expect(api.listIntegrationBooks).not.toHaveBeenCalled();
    await selectScope();
    fireEvent.click(await screen.findByRole("button", { name: "Run three-way comparison" }));
    const retained = await screen.findByLabelText("Retained three-way comparison");
    expect(api.runThreeWayReconciliation).toHaveBeenCalledWith(accountId, bookId, periodId, expect.any(String));
    expect(retained).toHaveTextContent("Ledger: 1000.00");
    expect(retained).toHaveTextContent("Broker: Unavailable");
    expect(retained).toHaveTextContent("Broker cash is unavailable");
    expect(retained).toHaveTextContent("1 minute");
    expect(within(retained).getByRole("link", { name: "Open break synthetic-break" })).toHaveAttribute("href", expect.stringContaining("reconciliationBreak=synthetic-break"));
    expect(retained).toHaveTextContent("A comparison does not post an adjustment");
  });

  it("rejects a latest comparison from a different period", async () => {
    api.getLatestThreeWayReconciliation.mockResolvedValue({ ...result, scope: { ...result.scope, accountingPeriodId: "other-period" } });
    renderWithRouter(<ThreeWayAccountingPanel />);
    await selectScope();
    fireEvent.click(await screen.findByRole("button", { name: "Load latest comparison" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("does not match the selected account, book and period");
    expect(screen.queryByLabelText("Retained three-way comparison")).not.toBeInTheDocument();
  });

  it("prepares only a supported fee draft and retries the same frozen evidence request after an uncertain response", async () => {
    api.runThreeWayReconciliation.mockResolvedValue({ ...result, status: "Breaks", accountingPeriodVersion: 7,
      sources: ["Ledger", "OMS", "Broker"].map(source => ({ ...result.sources[0], source,
        cash: source === "Broker" ? "997.50" : "1000.00", costBasisConvention: "FIFO", coverageFailures: [] })),
      checks: [{ ...result.checks[0], rightAmount: "997.50", status: "Break", explanation: "Independent retained fee explains the cash difference" }], adjustmentProposals: [{
      checkId: "synthetic-check", status: "Ready", description: "Independently retained synthetic broker fee", amount: "2.50",
      currency: "USD", evidenceIds: ["synthetic-fee-id"], evidenceHash: "sha256:synthetic-fee-proof"
    }] });
    api.prepareThreeWayAdjustment.mockRejectedValueOnce(new Error("Connection lost after draft save")).mockResolvedValueOnce({
      journalEntryId: "55555555-5555-5555-5555-555555555555", fundProfileId: book.fundProfileId, ledgerBookId: bookId, periodId,
      status: "Draft", memo: "Synthetic independently evidenced fee"
    });
    renderWithRouter(<ThreeWayAccountingPanel />);
    await selectScope();
    fireEvent.click(await screen.findByRole("button", { name: "Run three-way comparison" }));
    const prepare = await screen.findByRole("button", { name: "Prepare fee adjustment draft" });
    expect(api.prepareThreeWayAdjustment).not.toHaveBeenCalled();
    fireEvent.click(prepare);
    expect(await screen.findByRole("alert")).toHaveTextContent("same preparation request is retained for retry");
    fireEvent.click(screen.getByRole("button", { name: "Retry same fee draft preparation" }));
    const link = await screen.findByRole("link", { name: "Open fee adjustment draft" });
    expect(api.prepareThreeWayAdjustment.mock.calls[0]).toEqual(api.prepareThreeWayAdjustment.mock.calls[1]);
    expect(api.prepareThreeWayAdjustment.mock.calls[0]).toEqual([accountId, {
      ledgerBookId: bookId, accountingPeriodId: periodId, operationId: result.operationId, checkId: "synthetic-check",
      expectedPeriodVersion: 7, evidenceHash: "sha256:synthetic-fee-proof"
    }]);
    expect(link).toHaveAttribute("href", expect.stringContaining("journalEntryId=55555555-5555-5555-5555-555555555555"));
    expect(screen.queryByRole("button", { name: /Approve|Post journal/ })).not.toBeInTheDocument();
  });

  it("explains insufficient fee evidence without offering a preparation command", async () => {
    api.runThreeWayReconciliation.mockResolvedValue({ ...result, accountingPeriodVersion: 7, adjustmentProposals: [{
      checkId: "synthetic-check", status: "InsufficientEvidence", description: "No independent fee identity explains this cash difference",
      amount: null, currency: "USD", evidenceIds: [], evidenceHash: ""
    }] });
    renderWithRouter(<ThreeWayAccountingPanel />);
    await selectScope();
    fireEvent.click(await screen.findByRole("button", { name: "Run three-way comparison" }));
    expect(await screen.findByText("Insufficient evidence")).toBeInTheDocument();
    expect(screen.getByText("No independent fee identity explains this cash difference")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Prepare fee adjustment draft" })).not.toBeInTheDocument();
    expect(api.prepareThreeWayAdjustment).not.toHaveBeenCalled();
  });

  it("activates only after explicit mapping review and revokes the retained exact version", async () => {
    api.activatePostingGrant.mockResolvedValue(grant);
    api.revokePostingGrant.mockResolvedValue({ ...grant, isActive: false, reference: { ...grant.reference, version: 2 }, revokedBy: "synthetic-operator" });
    renderWithRouter(<AutomaticPostingAuthorityPanel />);
    await selectScope();
    const activate = await screen.findByRole("button", { name: "Activate synthetic posting" });
    expect(activate).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Load current mapping" }));
    await screen.findByText("mapping-7");
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(activate);
    await screen.findByLabelText("Retained posting grant");
    expect(api.activatePostingGrant).toHaveBeenCalledWith(expect.objectContaining({ financialAccountId: accountId, ledgerBookId: bookId,
      periodId, policyId: "synthetic-policy", policyVersion: "1", mappingRevision: "mapping-7", sourceMode: "Synthetic", actionOrigin: "HumanOperator" }));
    expect(api.activatePostingGrant.mock.calls[0][0]).not.toHaveProperty("tenantId");
    expect(api.activatePostingGrant.mock.calls[0][0]).not.toHaveProperty("actor");
    const revoke = screen.getByRole("button", { name: "Revoke authority" });
    expect(revoke).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(revoke);
    await screen.findByRole("button", { name: "Prepare new grant" });
    expect(api.revokePostingGrant).toHaveBeenCalledWith(grant.reference.grantId, 1);
    expect(screen.getByLabelText("Retained posting grant")).toHaveTextContent("Revoked · Version 2");
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("keeps the same grant identity after an uncertain activation response", async () => {
    api.activatePostingGrant.mockRejectedValue(new Error("Connection lost after submission"));
    renderWithRouter(<AutomaticPostingAuthorityPanel />);
    await selectScope();
    const idBefore = (screen.getByLabelText("Grant ID") as HTMLInputElement).value;
    fireEvent.click(screen.getByRole("button", { name: "Load current mapping" }));
    await screen.findByText("mapping-7");
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Activate synthetic posting" }));
    await screen.findByRole("alert");
    expect((screen.getByLabelText("Grant ID") as HTMLInputElement).value).toBe(idBefore);
    expect(screen.getByText(/inspect the same grant ID before retrying/)).toBeInTheDocument();
  });

  it("downloads the server ZIP for the selected book and period", async () => {
    api.downloadGeneralLedger.mockResolvedValue(new Blob(["synthetic-zip"], { type: "application/zip" }));
    URL.createObjectURL = vi.fn().mockReturnValue("blob:synthetic-general-ledger");
    URL.revokeObjectURL = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    renderWithRouter(<GeneralLedgerExportPanel />);
    await selectScope(false);
    fireEvent.click(await screen.findByRole("button", { name: "Download general ledger ZIP" }));
    await screen.findByRole("status");
    expect(api.downloadGeneralLedger).toHaveBeenCalledWith(bookId, periodId);
    expect(URL.createObjectURL).toHaveBeenCalledWith(expect.any(Blob));
  });

  it("does not offer a download when the current session lacks ExportData", async () => {
    api.getGeneralLedgerExportAccess.mockResolvedValue(false);
    renderWithRouter(<GeneralLedgerExportPanel />);
    await selectScope(false);
    expect(await screen.findByText(/does not have ExportData permission/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Download general ledger ZIP" })).not.toBeInTheDocument();
    expect(api.downloadGeneralLedger).not.toHaveBeenCalled();
  });

  it("rechecks export authority before downloading and refuses a revoked permission", async () => {
    api.getGeneralLedgerExportAccess.mockResolvedValueOnce(true).mockResolvedValueOnce(false);
    renderWithRouter(<GeneralLedgerExportPanel />);
    await selectScope(false);
    fireEvent.click(await screen.findByRole("button", { name: "Download general ledger ZIP" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("no longer has ExportData permission");
    expect(api.downloadGeneralLedger).not.toHaveBeenCalled();
  });
});

describe("visible backtest mode source edits", () => {
  it("inserts the executed command after using directives and changes it without duplicate overrides", () => {
    const raw = updateCorporateActionSource("using System;\nPrint(1);", "RawPricesWithCanonicalEvents");
    expect(raw).toBe('using System;\nBacktest.WithCorporateActionMode("RawPricesWithCanonicalEvents");\nPrint(1);');
    const legacy = updateCorporateActionSource(raw, "Legacy");
    expect(legacy).toContain('Backtest.WithCorporateActionMode("Legacy");');
    expect(legacy.match(/WithCorporateActionMode/g)).toHaveLength(1);
  });
  it("leaves custom fluent and multiple overrides for explicit source editing", () => {
    const source = 'var run = await Backtest.WithSymbols("SPY").WithCorporateActionMode("Legacy").RunAsync();';
    expect(corporateActionSourceMode(source).manual).toBe(true);
    expect(updateCorporateActionSource(source, "RawPricesWithCanonicalEvents")).toBe(source);
  });
  it("preserves a leading block comment and following using directives", () => {
    const source = "/* Synthetic experiment\n * retained source header\n */\nusing System;\nPrint(1);";
    expect(updateCorporateActionSource(source, "RawPricesWithCanonicalEvents"))
      .toContain('*/\nusing System;\nBacktest.WithCorporateActionMode("RawPricesWithCanonicalEvents");\nPrint(1);');
  });
});

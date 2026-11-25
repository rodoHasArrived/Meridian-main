import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as fiscalClose from "@/lib/api/fiscal-year-close.api";
import * as ledger from "@/lib/ledger-reports-api";
import { FiscalYearClosePanel } from "./accounting-screen.fiscal-year-close";
import { targetBook, targetPeriod } from "./accounting-screen.prepare-next-period.test-fixtures";

vi.mock("@/lib/api/fiscal-year-close.api", () => ({
  getFiscalYearClosePlan: vi.fn(),
  getFiscalYearCloseReceipt: vi.fn(),
  finalizeFiscalYearClose: vi.fn()
}));
vi.mock("@/lib/ledger-reports-api", () => ({
  getLedgerBooks: vi.fn(),
  getLedgerPeriods: vi.fn()
}));

const readyPlan: fiscalClose.FiscalYearClosePlan = {
  ledgerBookId: targetBook.ledgerBookId,
  fiscalYear: targetPeriod.fiscalYear,
  fiscalYearStart: "2026-01-01",
  fiscalYearEnd: "2026-12-31",
  isReady: true,
  canFinalizeFiscalYearClose: true,
  blockers: [],
  periods: [],
  preClosingTrialBalance: [],
  postedClosingTransfers: [],
  proposedClosingTransfers: [],
  postClosingTrialBalance: [],
  nextYearOpeningBalances: [{
    accountName: "Retained earnings",
    accountType: "Equity",
    symbol: null,
    financialAccountId: null,
    dimensions: null,
    debitTotal: "0",
    creditTotal: "1100",
    balance: "1100"
  }],
  reportedNetIncome: "100",
  openingEquity: "1000",
  preClosingEquity: "1000",
  postClosingEquity: "1100",
  planHashSha256: "verified-plan-hash"
};

const scope = { ledgerBookId: readyPlan.ledgerBookId, fiscalYear: readyPlan.fiscalYear };
const service = vi.mocked(fiscalClose);

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(ledger.getLedgerBooks).mockResolvedValue([targetBook]);
  vi.mocked(ledger.getLedgerPeriods).mockResolvedValue([{ ...targetPeriod, ledgerBookId: targetBook.ledgerBookId }]);
  service.getFiscalYearClosePlan.mockResolvedValue(readyPlan);
  service.getFiscalYearCloseReceipt.mockResolvedValue(null);
});

async function openCloseReview() {
  const user = userEvent.setup();
  render(<FiscalYearClosePanel />);
  expect(ledger.getLedgerBooks).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Review fiscal close" }));
  return user;
}

it("surfaces the server blocker without presenting a close command or opening balances", async () => {
  service.getFiscalYearClosePlan.mockResolvedValue({
    ...readyPlan, isReady: false, canFinalizeFiscalYearClose: false, planHashSha256: null,
    blockers: [{ code: "snapshot-missing", message: "May has no retained close snapshot.", periodId: null }]
  });
  await openCloseReview();
  expect(await screen.findByText("May has no retained close snapshot.")).toBeVisible();
  expect(screen.queryByRole("button", { name: "Retain fiscal close" })).not.toBeInTheDocument();
  expect(screen.queryByText("Reported net income")).not.toBeInTheDocument();
  expect(service.getFiscalYearClosePlan).toHaveBeenCalledWith(scope, expect.any(AbortSignal));
});

it("recovers the same command after an uncertain response and presents the retained evidence", async () => {
  service.finalizeFiscalYearClose.mockRejectedValueOnce(new Error("Connection lost"));
  service.finalizeFiscalYearClose.mockImplementationOnce(async (_, request) => ({
    receiptId: request.commandId,
    supersedesReceiptId: null,
    recordedAt: "2026-12-31T15:00:00Z",
    authorizedBy: "Test controller",
    controllerRole: "Controller",
    reason: request.reason,
    plan: readyPlan,
    functionalCurrency: "USD",
    accountingBasis: "Gaap",
    accountingPolicyId: "test-policy",
    accountingPolicyVersion: "1",
    closingJournalEntryIds: []
  }));
  const user = await openCloseReview();
  await user.type(await screen.findByLabelText("Close reason"), "Reviewed controller evidence");
  const button = screen.getByRole("button", { name: "Retain fiscal close" });
  await user.click(button);
  expect(await screen.findByRole("alert")).toBeVisible();
  await user.click(button);
  expect(await screen.findByRole("region", { name: "Retained close receipt" })).toBeVisible();
  expect(service.finalizeFiscalYearClose.mock.calls[1][1]).toEqual(service.finalizeFiscalYearClose.mock.calls[0][1]);
  expect(service.finalizeFiscalYearClose.mock.calls[0][1]).toMatchObject({
    expectedPlanHashSha256: readyPlan.planHashSha256,
    reason: "Reviewed controller evidence",
    supersedesReceiptId: null
  });
  expect(screen.queryByRole("button", { name: "Retain fiscal close" })).not.toBeInTheDocument();
  await user.click(screen.getByText("Next-year opening balances (1)"));
  expect(within(screen.getByRole("table", { name: "Next-year opening balances" })).getByText("Retained earnings")).toBeVisible();
});

it("allows read-only review when the server withholds close authority", async () => {
  service.getFiscalYearClosePlan.mockResolvedValue({ ...readyPlan, canFinalizeFiscalYearClose: false });
  await openCloseReview();
  expect(await screen.findByText("Retaining the close requires controller authorization.")).toBeVisible();
  expect(screen.getByText("1,100 USD")).toBeVisible();
  expect(screen.queryByLabelText("Close reason")).not.toBeInTheDocument();
});

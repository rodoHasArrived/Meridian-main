import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import {
  buildAccountingPostedLedgerViewState,
  buildPostedLedgerPnlViewState,
  resolveDefaultPostedLedgerPeriodId,
  sortLedgerPeriodsDescending,
  toTrialBalanceLine,
  useAccountingPostedLedgerViewModel,
  type AccountingPostedLedgerServices
} from "@/screens/accounting-screen.posted-ledger.view-model";
import type {
  LedgerBook,
  LedgerPeriod,
  LedgerPeriodPnlSummary,
  LedgerPeriodTrialBalanceLine,
  LedgerPostedJournalEntry
} from "@/types";

function makePeriod(overrides: Partial<LedgerPeriod> = {}): LedgerPeriod {
  return {
    periodId: "00000000-0000-0000-0000-000000000001",
    ledgerBookId: "00000000-0000-0000-0000-0000000000aa",
    fiscalYear: 2026,
    periodNo: 7,
    label: "July 2026",
    startDate: "2026-07-01",
    endDate: "2026-07-31",
    status: "HardClosed",
    openedAt: "2026-07-01T00:00:00Z",
    closedAt: "2026-08-02T00:00:00Z",
    version: 3,
    ...overrides
  };
}

function makeLine(overrides: Partial<LedgerPeriodTrialBalanceLine> = {}): LedgerPeriodTrialBalanceLine {
  return {
    accountName: "Cash",
    accountType: "Asset",
    symbol: null,
    financialAccountId: "1000",
    debitTotal: 1500,
    creditTotal: 250,
    balance: 1250,
    entryCount: 12,
    accountingBasis: "Primary",
    ...overrides
  };
}

function makePnl(overrides: Partial<LedgerPeriodPnlSummary> = {}): LedgerPeriodPnlSummary {
  return {
    periodId: "00000000-0000-0000-0000-000000000001",
    ledgerBookId: "00000000-0000-0000-0000-0000000000aa",
    fiscalYear: 2026,
    periodNo: 7,
    label: "July 2026",
    totalRevenue: 5000,
    totalExpenses: 1800,
    netIncome: 3200,
    periodOnPeriodVariance: 150,
    openBreakCount: 0,
    signoffStatus: "SignedOff",
    completedAt: "2026-08-02T00:00:00Z",
    revenueLines: [],
    expenseLines: [],
    ...overrides
  };
}

function makeBook(overrides: Partial<LedgerBook> = {}): LedgerBook {
  return {
    ledgerBookId: "00000000-0000-0000-0000-0000000000aa",
    fundProfileId: "fund-alpha",
    fundStructureNodeId: "00000000-0000-0000-0000-0000000000bb",
    fundStructureNodeKind: "Fund",
    displayName: "Master Fund",
    baseCurrency: "USD",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    accountingBasis: "Primary",
    accountingPolicyId: "legacy-v1",
    accountingPolicyVersion: "legacy-v1",
    ...overrides
  };
}

function makeServices(overrides: Partial<AccountingPostedLedgerServices> = {}): AccountingPostedLedgerServices {
  return {
    getBooks: vi.fn().mockResolvedValue([makeBook()]),
    getPeriods: vi.fn().mockResolvedValue([makePeriod()]),
    getTrialBalance: vi.fn().mockResolvedValue([makeLine()]),
    getPnlSummary: vi.fn().mockResolvedValue(makePnl()),
    getJournalEntries: vi.fn().mockResolvedValue([]),
    ...overrides
  };
}

describe("sortLedgerPeriodsDescending", () => {
  it("orders periods newest first by fiscal year and period number", () => {
    const sorted = sortLedgerPeriodsDescending([
      makePeriod({ periodId: "p-2025-12", fiscalYear: 2025, periodNo: 12 }),
      makePeriod({ periodId: "p-2026-07", fiscalYear: 2026, periodNo: 7 }),
      makePeriod({ periodId: "p-2026-01", fiscalYear: 2026, periodNo: 1 })
    ]);

    expect(sorted.map((period) => period.periodId)).toEqual(["p-2026-07", "p-2026-01", "p-2025-12"]);
  });
});

describe("resolveDefaultPostedLedgerPeriodId", () => {
  it("prefers the latest closed period over a newer open one", () => {
    const selected = resolveDefaultPostedLedgerPeriodId([
      makePeriod({ periodId: "p-open", fiscalYear: 2026, periodNo: 8, status: "Open" }),
      makePeriod({ periodId: "p-closed", fiscalYear: 2026, periodNo: 7, status: "HardClosed" })
    ]);

    expect(selected).toBe("p-closed");
  });

  it("falls back to the latest period when nothing is closed", () => {
    const selected = resolveDefaultPostedLedgerPeriodId([
      makePeriod({ periodId: "p-old", fiscalYear: 2026, periodNo: 6, status: "Open" }),
      makePeriod({ periodId: "p-new", fiscalYear: 2026, periodNo: 7, status: "Open" })
    ]);

    expect(selected).toBe("p-new");
  });

  it("returns null when there are no periods", () => {
    expect(resolveDefaultPostedLedgerPeriodId([])).toBeNull();
  });
});

describe("toTrialBalanceLine", () => {
  it("maps a posted-journal line into the shared trial-balance shape without a security reference", () => {
    const line = toTrialBalanceLine(makeLine({ ruleId: "rule-7", sourceJournalEntryId: "je-1" }));

    expect(line.accountName).toBe("Cash");
    expect(line.balance).toBe(1250);
    expect(line.entryCount).toBe(12);
    expect(line.security).toBeNull();
    expect(line.ruleId).toBe("rule-7");
    expect(line.sourceJournalEntryId).toBe("je-1");
  });
});

describe("buildAccountingPostedLedgerViewState", () => {
  it("labels the trial balance with the posted-journal period scope, not a run", () => {
    const view = buildAccountingPostedLedgerViewState({
      periods: [makePeriod()],
      periodsLoading: false,
      periodsError: null,
      selectedPeriodId: makePeriod().periodId,
      periodNotice: null,
      trialBalanceRows: [toTrialBalanceLine(makeLine())],
      trialBalanceLoading: false,
      trialBalanceError: null,
      pnl: makePnl(),
      pnlLoading: false,
      pnlError: null,
      selectedRowId: null,
      selectedBasis: "Primary",
      accountFilter: "",
      selectedBookLabel: "Master Fund",
      booksErrorText: null,
      baseCurrency: "USD",
      bookOptions: []
    });

    expect(view.trialBalance.description).toContain("the posted journal for period July 2026");
    expect(view.trialBalance.description).not.toContain("ledger run");
    expect(view.trialBalance.hasRows).toBe(true);
    expect(view.trialBalance.detailPanelId).toBe("posted-ledger-account-detail");
    expect(view.periodSelector.options).toHaveLength(1);
    expect(view.periodSelector.options[0]?.isSelected).toBe(true);
    expect(view.pnl.state).toBe("ready");
    expect(view.pnl.signoffLabel).toBe("Signed off");
  });

  it("explains how to create the governed book when no periods exist", () => {
    const view = buildAccountingPostedLedgerViewState({
      periods: [],
      periodsLoading: false,
      periodsError: null,
      selectedPeriodId: null,
      periodNotice: null,
      trialBalanceRows: [],
      trialBalanceLoading: false,
      trialBalanceError: null,
      pnl: null,
      pnlLoading: false,
      pnlError: null,
      selectedRowId: null,
      selectedBasis: "Primary",
      accountFilter: "",
      selectedBookLabel: "Master Fund",
      booksErrorText: null,
      baseCurrency: "USD",
      bookOptions: []
    });

    expect(view.periodSelector.emptyText).toContain("No ledger periods exist yet");
    expect(view.pnl.state).toBe("empty");
  });
});

describe("buildPostedLedgerPnlViewState", () => {
  it("flags open breaks and negative net income", () => {
    const view = buildPostedLedgerPnlViewState({
      pnl: makePnl({ netIncome: -100, openBreakCount: 3, signoffStatus: "Pending" }),
      loading: false,
      error: null,
      periodLabel: "July 2026"
    });

    expect(view.items.find((item) => item.id === "net-income")?.tone).toBe("danger");
    expect(view.items.find((item) => item.id === "open-breaks")?.tone).toBe("warning");
    expect(view.signoffLabel).toBe("Sign-off pending");
    expect(view.signoffTone).toBe("warning");
  });
});

describe("useAccountingPostedLedgerViewModel", () => {
  it("loads periods on the ledger workstream and reads the posted book for the default period", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    await waitFor(() => {
      expect(result.current.view.trialBalance.hasRows).toBe(true);
    });

    expect(services.getPeriods).toHaveBeenCalledTimes(1);
    expect(services.getTrialBalance).toHaveBeenCalledWith(makePeriod().periodId);
    expect(services.getPnlSummary).toHaveBeenCalledWith(makePeriod().periodId);
    expect(result.current.view.pnl.state).toBe("ready");
  });

  it("does not call the ledger API outside the ledger workstream", async () => {
    const services = makeServices();
    renderHook(() => useAccountingPostedLedgerViewModel("reconciliation", services));

    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(services.getPeriods).not.toHaveBeenCalled();
    expect(services.getTrialBalance).not.toHaveBeenCalled();
  });

  it("treats a 404 as an open-period notice rather than a failure", async () => {
    const notFound = new ApiError({ path: "/api/ledger/periods/x/trial-balance", status: 404 });
    const services = makeServices({
      getTrialBalance: vi.fn().mockRejectedValue(notFound),
      getPnlSummary: vi.fn().mockRejectedValue(notFound)
    });
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    await waitFor(() => {
      expect(result.current.view.periodNotice).toContain("no closed-period summary");
    });

    expect(result.current.view.trialBalance.errorText).toBeNull();
    expect(result.current.view.pnl.errorText).toBeNull();
  });

  it("surfaces a real failure as an error", async () => {
    const services = makeServices({
      getTrialBalance: vi.fn().mockRejectedValue(new ApiError({ path: "/api/ledger/periods/x/trial-balance", status: 500 }))
    });
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    await waitFor(() => {
      expect(result.current.view.trialBalance.errorText).not.toBeNull();
    });
    expect(result.current.view.periodNotice).toBeNull();
  });

  it("reloads the posted book when the operator selects another period", async () => {
    const closed = makePeriod();
    const older = makePeriod({
      periodId: "00000000-0000-0000-0000-000000000002",
      periodNo: 6,
      label: "June 2026"
    });
    const services = makeServices({
      getPeriods: vi.fn().mockResolvedValue([closed, older])
    });
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    await waitFor(() => {
      expect(result.current.view.trialBalance.hasRows).toBe(true);
    });

    act(() => {
      result.current.selectPeriod(older.periodId);
    });

    await waitFor(() => {
      expect(services.getTrialBalance).toHaveBeenCalledWith(older.periodId);
    });
    expect(result.current.view.periodSelector.options.find((option) => option.isSelected)?.id).toBe(older.periodId);
  });

  it("clears the outgoing period's figures rather than relabelling them as the new period", async () => {
    const closed = makePeriod();
    const older = makePeriod({
      periodId: "00000000-0000-0000-0000-000000000002",
      periodNo: 6,
      label: "June 2026"
    });
    // The second period's request never settles, standing in for a slow or hung response. The
    // period label and scope switch the moment the selection changes, so any retained rows would
    // be July's balances presented as June's — and stay that way.
    const getTrialBalance = vi.fn()
      .mockResolvedValueOnce([makeLine()])
      .mockImplementationOnce(() => new Promise(() => {}));
    const getJournalEntries = vi.fn()
      .mockResolvedValueOnce([])
      .mockImplementationOnce(() => new Promise(() => {}));
    const services = makeServices({
      getPeriods: vi.fn().mockResolvedValue([closed, older]),
      getTrialBalance,
      getJournalEntries
    });
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    await waitFor(() => {
      expect(result.current.view.trialBalance.hasRows).toBe(true);
    });

    act(() => {
      result.current.selectPeriod(older.periodId);
    });

    await waitFor(() => {
      expect(getTrialBalance).toHaveBeenCalledWith(older.periodId);
    });

    expect(result.current.view.trialBalance.hasRows).toBe(false);
    expect(result.current.view.trialBalance.state).toBe("loading");
    expect(result.current.journalLines).toHaveLength(0);
  });
  it("scopes the period request to a ledger book instead of spanning every book", async () => {
    // Unscoped, the period list spans every book and the default lands on whichever book owns
    // the globally latest closed period — shown under this panel's scope as if it were the only one.
    const services = makeServices({
      getBooks: vi.fn().mockResolvedValue([makeBook({ displayName: "Feeder Fund" })])
    });
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    await waitFor(() => {
      expect(services.getPeriods).toHaveBeenCalled();
    });

    expect(services.getPeriods).toHaveBeenCalledWith({
      ledgerBookId: "00000000-0000-0000-0000-0000000000aa"
    });
    await waitFor(() => {
      expect(result.current.view.selectedBookLabel).toBe("Feeder Fund");
    });
  });

  it("selects a basis the incoming period actually carries", async () => {
    // Carrying GAAP across a period change filtered every row out of a Primary-only period, and
    // the period read as having no trial balance even though it loaded successfully.
    const older = makePeriod({ periodId: "22222222-2222-2222-2222-222222222222", periodNo: 6, label: "June 2026" });
    const closed = makePeriod();
    const getTrialBalance = vi.fn()
      .mockResolvedValueOnce([makeLine({ accountingBasis: "Gaap" })])
      .mockResolvedValueOnce([makeLine({ accountingBasis: "Primary" })]);
    const services = makeServices({
      getPeriods: vi.fn().mockResolvedValue([closed, older]),
      getTrialBalance
    });
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    await waitFor(() => {
      expect(result.current.view.trialBalance.selectedBasis).toBe("Gaap");
    });

    act(() => {
      result.current.selectPeriod(older.periodId);
    });

    await waitFor(() => {
      expect(result.current.view.trialBalance.selectedBasis).toBe("Primary");
    });
    expect(result.current.view.trialBalance.hasRows).toBe(true);
  });

  it("drops the outgoing book's periods and figures when another book is selected", async () => {
    // Clearing only selectedPeriodId left book A's periods in place, so the validation effect
    // immediately re-picked A's default and loaded its figures under B's label and currency.
    const services = makeServices({
      getBooks: vi.fn().mockResolvedValue([
        makeBook(),
        makeBook({ ledgerBookId: "00000000-0000-0000-0000-0000000000cc", displayName: "Feeder Fund" })
      ]),
      getTrialBalance: vi.fn().mockResolvedValue([makeLine()])
    });
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    await waitFor(() => {
      expect(result.current.view.trialBalance.hasRows).toBe(true);
    });

    act(() => {
      result.current.selectBook("00000000-0000-0000-0000-0000000000cc");
    });

    expect(result.current.view.periodSelector.options).toHaveLength(0);
    expect(result.current.view.trialBalance.hasRows).toBe(false);
    expect(result.current.journalLines).toHaveLength(0);
  });

  it("leaves the posted journal unrequested for a consumer that does not render it", async () => {
    // AccountingPostedLedgerSection destructures only `view`. The journal route returns a
    // period's entries in full, so fetching it for that panel downloads a production month's
    // book purely to discard it.
    const services = makeServices();
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    await waitFor(() => {
      expect(result.current.view.trialBalance.hasRows).toBe(true);
    });

    expect(services.getJournalEntries).not.toHaveBeenCalled();
    expect(result.current.journalLines).toHaveLength(0);
    expect(result.current.journalLoading).toBe(false);
  });

  it("requests the posted journal for a consumer that opts in", async () => {
    const entry: LedgerPostedJournalEntry = {
      journalEntryId: "je-1",
      periodId: "00000000-0000-0000-0000-000000000001",
      ledgerBookId: "00000000-0000-0000-0000-0000000000aa",
      timestamp: "2026-07-31T00:00:00Z",
      description: "Management fee accrual",
      totalDebits: 1200,
      totalCredits: 1200,
      isBalanced: true,
      lines: []
    };
    const services = makeServices({
      getJournalEntries: vi.fn().mockResolvedValue([entry])
    });
    const { result } = renderHook(
      () => useAccountingPostedLedgerViewModel("ledger", services, { includeJournal: true })
    );

    await waitFor(() => {
      expect(result.current.journalLines).toHaveLength(1);
    });

    expect(services.getJournalEntries).toHaveBeenCalledWith("00000000-0000-0000-0000-000000000001");
    expect(result.current.journalLines[0]?.journalEntryId).toBe("je-1");
  });
});

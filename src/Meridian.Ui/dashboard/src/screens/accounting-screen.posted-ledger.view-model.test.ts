import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import {
  buildAccountingPostedLedgerViewState,
  buildPostedLedgerPnlViewState,
  collectPostedLedgerRelatedSecurities,
  resolveDefaultPostedLedgerPeriodId,
  resolvePostedEntryDimensions,
  sortLedgerPeriodsDescending,
  toLedgerJournalLine,
  toTrialBalanceLine,
  useAccountingPostedLedgerViewModel,
  type AccountingPostedLedgerServices
} from "@/screens/accounting-screen.posted-ledger.view-model";
import type { AccountingWorkstream } from "@/screens/accounting-screen.task-mode-view-model";
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

function makePostedEntryLine(
  overrides: Partial<LedgerPostedJournalEntry["lines"][number]> = {}
): LedgerPostedJournalEntry["lines"][number] {
  return {
    entryId: "entry-1",
    journalEntryId: "je-1",
    timestamp: "2026-07-31T00:00:00Z",
    accountName: "Cash",
    accountType: "Asset",
    symbol: null,
    financialAccountId: "1000",
    debit: 1200,
    credit: 0,
    description: "Management fee accrual",
    ...overrides
  };
}

function makePostedEntry(overrides: Partial<LedgerPostedJournalEntry> = {}): LedgerPostedJournalEntry {
  return {
    journalEntryId: "je-1",
    periodId: "00000000-0000-0000-0000-000000000001",
    ledgerBookId: "00000000-0000-0000-0000-0000000000aa",
    timestamp: "2026-07-31T00:00:00Z",
    description: "Management fee accrual",
    totalDebits: 1200,
    totalCredits: 1200,
    isBalanced: true,
    lines: [makePostedEntryLine()],
    ...overrides
  };
}

describe("resolvePostedEntryDimensions", () => {
  it("reports the shared scope when every line was posted to it", () => {
    // LedgerJournalEntryDto declares no dimensions of its own. Reading `entry.dimensions` meant
    // the posted book's evidence rows were always unscoped, however precisely the lines were
    // tagged.
    const dimensions = { fundId: "fund-alpha", instrumentId: "sec-1" };
    const entry = makePostedEntry({
      lines: [
        makePostedEntryLine({ entryId: "entry-1", dimensions }),
        makePostedEntryLine({ entryId: "entry-2", debit: 0, credit: 1200, dimensions: { ...dimensions } })
      ]
    });

    expect(resolvePostedEntryDimensions(entry)).toEqual(dimensions);
    expect(toLedgerJournalLine(entry).dimensions).toEqual(dimensions);
  });

  it("reports no scope for an entry whose lines disagree", () => {
    // Naming one line's scope as the entry's would attribute the whole entry to whichever line
    // happened to come first.
    const entry = makePostedEntry({
      lines: [
        makePostedEntryLine({ entryId: "entry-1", dimensions: { fundId: "fund-alpha" } }),
        makePostedEntryLine({ entryId: "entry-2", debit: 0, credit: 1200, dimensions: { fundId: "fund-beta" } })
      ]
    });

    expect(resolvePostedEntryDimensions(entry)).toBeNull();
  });

  it("reports no scope when a line carries none", () => {
    const entry = makePostedEntry({
      lines: [
        makePostedEntryLine({ entryId: "entry-1", dimensions: { fundId: "fund-alpha" } }),
        makePostedEntryLine({ entryId: "entry-2", debit: 0, credit: 1200 })
      ]
    });

    expect(resolvePostedEntryDimensions(entry)).toBeNull();
    expect(resolvePostedEntryDimensions(makePostedEntry({ lines: [] }))).toBeNull();
  });
});

describe("collectPostedLedgerRelatedSecurities", () => {
  it("reads the instrument dimension the posted book identifies a security by", () => {
    // LedgerPeriodTrialBalanceLineDto carries no security reference, so keying strictly on
    // security.securityId left the drill-through permanently empty on a posted period.
    const related = collectPostedLedgerRelatedSecurities([
      toTrialBalanceLine(makeLine({ symbol: "AAPL", dimensions: { instrumentId: "sec-1" } })),
      toTrialBalanceLine(makeLine({ accountName: "Fees", symbol: null, dimensions: { instrumentId: "sec-1" } })),
      toTrialBalanceLine(makeLine({ accountName: "Cash", symbol: null }))
    ]);

    expect(related).toEqual([{ securityId: "sec-1", label: "AAPL" }]);
  });

  it("prefers an explicit security reference over the dimension", () => {
    const related = collectPostedLedgerRelatedSecurities([
      {
        symbol: "MSFT",
        dimensions: { instrumentId: "sec-dimension" },
        security: {
          securityId: "sec-reference",
          displayName: "Microsoft Corp",
          assetClass: "Equity",
          currency: "USD",
          status: "Active",
          primaryIdentifier: "MSFT",
          subType: null
        }
      }
    ]);

    expect(related).toEqual([{ securityId: "sec-reference", label: "Microsoft Corp" }]);
  });

  it("returns nothing for rows that name no instrument at all", () => {
    expect(collectPostedLedgerRelatedSecurities([toTrialBalanceLine(makeLine())])).toEqual([]);
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
  it("scopes revenue and expense to the selected basis instead of summing every projection", () => {
    // The endpoint aggregates across every basis the period holds, so a GAAP trial balance used to
    // sit beside a P&L that had added Primary and GAAP together.
    const pnl = makePnl({
      revenueLines: [
        makeLine({ accountName: "Fees", accountType: "Revenue", balance: 1000, accountingBasis: "Primary" }),
        makeLine({ accountName: "Fees", accountType: "Revenue", balance: 900, accountingBasis: "Gaap" })
      ],
      expenseLines: [
        makeLine({ accountName: "Admin", accountType: "Expense", balance: 400, accountingBasis: "Primary" }),
        makeLine({ accountName: "Admin", accountType: "Expense", balance: 300, accountingBasis: "Gaap" })
      ]
    });

    const gaap = buildPostedLedgerPnlViewState({
      pnl,
      loading: false,
      error: null,
      periodLabel: "July 2026",
      selectedBasis: "Gaap"
    });

    expect(gaap.items.find((item) => item.id === "revenue")?.value).toContain("900");
    expect(gaap.items.find((item) => item.id === "expenses")?.value).toContain("300");
    // 900 - 300, not the cross-basis 1,900 - 700.
    expect(gaap.items.find((item) => item.id === "net-income")?.value).toContain("600");
  });

  it("labels posted amounts in the book's base currency", () => {
    const view = buildPostedLedgerPnlViewState({
      pnl: makePnl({ revenueLines: [makeLine({ accountType: "Revenue", balance: 1000 })] }),
      loading: false,
      error: null,
      periodLabel: "July 2026",
      baseCurrency: "EUR"
    });

    expect(view.items.find((item) => item.id === "revenue")?.value).not.toContain("$");
  });

  it("labels the period-on-period variance as cross-basis on a mixed period", () => {
    // The endpoint derives the variance across every basis the period holds and it cannot be
    // split. Scoping revenue and expense without saying so left a basis-scoped net income sitting
    // beside a cross-basis variance as though they were one set of figures.
    const pnl = makePnl({ periodOnPeriodVariance: 150 });

    const mixed = buildPostedLedgerPnlViewState({
      pnl,
      loading: false,
      error: null,
      periodLabel: "July 2026",
      availableBasisCount: 2
    });
    expect(mixed.items.find((item) => item.id === "variance")?.value).toContain("all bases");

    const single = buildPostedLedgerPnlViewState({
      pnl,
      loading: false,
      error: null,
      periodLabel: "July 2026",
      availableBasisCount: 1
    });
    expect(single.items.find((item) => item.id === "variance")?.value).not.toContain("all bases");
  });

  it("discloses cross-basis totals when the summary carries no line detail to scope by", () => {
    const withoutDetail = buildPostedLedgerPnlViewState({
      pnl: makePnl({ revenueLines: [], expenseLines: [] }),
      loading: false,
      error: null,
      periodLabel: "July 2026",
      selectedBasis: "Gaap",
      availableBasisCount: 2
    });
    expect(withoutDetail.items.find((item) => item.id === "basis-scope")?.value)
      .toContain("across all 2 bases");

    // With line detail the totals are genuinely the selected basis's own, so there is nothing to
    // disclose.
    const withDetail = buildPostedLedgerPnlViewState({
      pnl: makePnl({
        revenueLines: [makeLine({ accountType: "Revenue", balance: 900, accountingBasis: "Gaap" })]
      }),
      loading: false,
      error: null,
      periodLabel: "July 2026",
      selectedBasis: "Gaap",
      availableBasisCount: 2
    });
    expect(withDetail.items.find((item) => item.id === "basis-scope")).toBeUndefined();
  });

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

  it("keeps the ledger on screen when the book already selected is chosen again", async () => {
    // Re-selecting the current book is not a scope change, but the clearing ran unconditionally
    // while setSelectedBookId bailed out on the unchanged id — so the period effect never re-ran
    // and the panel stayed blank until a different book was picked.
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
    const selectedBook = result.current.view.bookOptions.find((book) => book.isSelected);
    expect(selectedBook).toBeDefined();
    const periodCount = result.current.view.periodSelector.options.length;
    expect(periodCount).toBeGreaterThan(0);

    act(() => {
      result.current.selectBook(selectedBook!.id);
    });

    expect(result.current.view.bookOptions.find((book) => book.isSelected)?.id).toBe(selectedBook!.id);
    expect(result.current.view.periodSelector.options).toHaveLength(periodCount);
    expect(result.current.view.trialBalance.hasRows).toBe(true);
  });

  it("drops the previous book's figures when book discovery fails", async () => {
    // The book label and base currency come off the selected book, and the period effect does not
    // run without one -- so figures left behind after a failed refresh rendered a book's balances
    // with nothing on screen saying whose they were, indefinitely.
    const getBooks = vi.fn()
      .mockResolvedValueOnce([makeBook()])
      .mockRejectedValue(new Error("ledger service unavailable"));
    const services = makeServices({ getBooks, getTrialBalance: vi.fn().mockResolvedValue([makeLine()]) });
    const { result, rerender } = renderHook(
      ({ workstream }: { workstream: AccountingWorkstream }) =>
        useAccountingPostedLedgerViewModel(workstream, services),
      { initialProps: { workstream: "ledger" as AccountingWorkstream } }
    );

    await waitFor(() => {
      expect(result.current.view.trialBalance.hasRows).toBe(true);
    });

    // Re-run book discovery by leaving and returning to the ledger workstream.
    rerender({ workstream: "reconciliation" });
    rerender({ workstream: "ledger" });

    await waitFor(() => {
      expect(result.current.view.periodSelector.errorText).toContain("unavailable");
    });
    expect(result.current.view.trialBalance.hasRows).toBe(false);
    expect(result.current.view.periodSelector.options).toHaveLength(0);
    expect(result.current.view.selectedBookLabel).toBeNull();
    // And it must not read as "create a ledger book" during an outage.
    expect(result.current.view.periodSelector.emptyText).toBeNull();
  });

  it("reads as loading while book discovery is in flight, not as an empty book", async () => {
    let releaseBooks: (books: LedgerBook[]) => void = () => undefined;
    const services = makeServices({
      getBooks: vi.fn().mockImplementation(() => new Promise<LedgerBook[]>((resolve) => {
        releaseBooks = resolve;
      }))
    });
    const { result } = renderHook(() => useAccountingPostedLedgerViewModel("ledger", services));

    // Book discovery gates every request below it, so an untracked one told the operator to create
    // a ledger book while the request that would have found one was still running.
    expect(result.current.view.periodSelector.loading).toBe(true);
    expect(result.current.view.periodSelector.emptyText).toBeNull();

    await act(async () => {
      releaseBooks([makeBook()]);
      await Promise.resolve();
    });

    await waitFor(() => {
      expect(result.current.view.periodSelector.loading).toBe(false);
    });
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

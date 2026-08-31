import { screen } from "@testing-library/react";
import * as api from "@/lib/api";
import * as ledgerReportsApi from "@/lib/ledger-reports-api";
import { JournalEntryDetailScreen } from "@/screens/journal-entry-detail-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { LedgerJournalLine, ManualJournalEntryDraft, ManualJournalEntryWorkbench } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getManualJournalEntryWorkbench: vi.fn(),
    getRunLedgerJournal: vi.fn()
  };
});

vi.mock("@/lib/ledger-reports-api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/ledger-reports-api")>("@/lib/ledger-reports-api");
  return {
    ...actual,
    getLedgerPeriodJournalEntries: vi.fn(),
    getLedgerBooks: vi.fn()
  };
});

const manualDraft: ManualJournalEntryDraft = {
  journalEntryId: "manual-je-1",
  status: "Approved",
  fundProfileId: "fund-alpha",
  ledgerBookId: "book-alpha",
  accountingBasis: "Primary",
  accountingDate: "2026-06-30",
  currency: "USD",
  memo: "Manual close adjustment",
  preparedBy: "browser-user",
  createdAtUtc: "2026-06-30T00:00:00Z",
  updatedAtUtc: "2026-06-30T00:00:00Z",
  version: 1,
  lines: [
    { lineId: "line-1", side: "Debit", amount: 500, currency: "USD", accountPath: "Cash", description: "Sweep in" },
    { lineId: "line-2", side: "Credit", amount: 500, currency: "USD", accountPath: "Suspense", description: "Sweep out" }
  ],
  evidenceLinks: [],
  validationIssues: [],
  evidenceAttachments: [
    {
      attachmentId: "att-1",
      displayName: "Bank statement.pdf",
      evidenceKind: "Statement",
      uri: "https://evidence.example/att-1",
      sourceSystem: "vault",
      addedAtUtc: "2026-06-30T01:00:00Z",
      addedBy: "controller"
    }
  ],
  totalDebits: 500,
  totalCredits: 500,
  imbalance: 0,
  entryType: "General",
  lifecycleTransitions: [
    {
      transitionId: "t1",
      fromStatus: "Draft",
      toStatus: "Approved",
      action: "Approve",
      actor: "controller",
      recordedAtUtc: "2026-06-30T02:00:00Z",
      evidenceLinks: []
    }
  ]
};

const workbench: ManualJournalEntryWorkbench = {
  fundProfileId: "fund-alpha",
  ledgerBookId: "book-alpha",
  loadedAtUtc: "2026-06-30T00:00:00Z",
  ledgerBooks: [],
  chartOfAccounts: [],
  drafts: [manualDraft],
  auditTrail: []
};

const journalLine: LedgerJournalLine = {
  journalEntryId: "je-posted-1",
  timestamp: "2026-06-30T00:00:00Z",
  description: "Cash sweep",
  totalDebits: 500,
  totalCredits: 500,
  lineCount: 2
};

async function renderScreen(initialEntry: string) {
  const result = renderWithRouter(<JournalEntryDetailScreen />, { initialEntries: [initialEntry] });
  await waitForAsyncEffects();
  return result;
}

describe("JournalEntryDetailScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("prompts for a journal entry when no id is provided", async () => {
    await renderScreen("/accounting/journal-entries/detail");

    expect(screen.getByText(/Open this page from a trial balance, ledger, or reconciliation row/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Journal Entries" })).toHaveAttribute("href", "/accounting/journal-entries");
    expect(screen.getByRole("link", { name: "Open Ledger Explorer" })).toHaveAttribute("href", "/accounting/ledger");
  });

  it("fails closed when the governed journal source is unavailable", async () => {
    vi.mocked(api.getManualJournalEntryWorkbench).mockRejectedValueOnce(new Error("offline"));

    await renderScreen("/accounting/journal-entries/detail?journalEntryId=manual-je-1");

    expect(await screen.findByRole("alert")).toHaveTextContent("Source unavailable");
    expect(screen.queryByText("Manual close adjustment")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
  });

  it("renders full detail for a manual draft entry", async () => {
    vi.mocked(api.getManualJournalEntryWorkbench).mockResolvedValueOnce(workbench);

    await renderScreen("/accounting/journal-entries/detail?journalEntryId=manual-je-1&runId=run-42");

    expect(await screen.findByText("Manual close adjustment")).toBeInTheDocument();
    expect(screen.getByText("Approved")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Lines for journal entry manual-je-1" })).toHaveTextContent("Cash");
    expect(screen.getByRole("region", { name: "Lines for journal entry manual-je-1" })).toHaveTextContent("Suspense");
    expect(screen.getByText("Draft -> Approved (Approve)")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Bank statement.pdf" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Approval" })).toBeInTheDocument();
    expect(screen.getAllByText("browser-user").length).toBeGreaterThan(0);
    expect(screen.getByRole("heading", { name: "Source lineage" })).toBeInTheDocument();
    expect(screen.getByText("Manual journal workbench")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Actions" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reverse" })).toBeDisabled();
    expect(screen.getByRole("link", { name: "Clone" })).toHaveAttribute("href", "/accounting/journal-entries");
    expect(screen.getByRole("link", { name: "Attach evidence" })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=journal-entry&subjectId=manual-je-1"
    );
    expect(screen.getByRole("button", { name: "Request review" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Export" })).toBeDisabled();
    // Run evidence lives under Strategy now; /accounting/ledger reads the posted book and
    // ignores runId, so these links used to drop operators into the wrong book entirely.
    expect(screen.getByRole("link", { name: "Back to Run Ledger" })).toHaveAttribute(
      "href",
      "/strategy/run-ledger?runId=run-42"
    );
  });

  it("falls back to a summary-only view for a posted entry not in the manual workbench", async () => {
    vi.mocked(api.getManualJournalEntryWorkbench).mockResolvedValueOnce(workbench);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce([journalLine]);

    await renderScreen("/accounting/journal-entries/detail?journalEntryId=je-posted-1&runId=run-42");

    expect(await screen.findByText("Summary-only entry")).toBeInTheDocument();
    expect(screen.getByText(/only the run-ledger summary is available/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Posting summary" })).toBeInTheDocument();
    expect(screen.getAllByText("$500.00").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Open in Run Ledger" })).toHaveAttribute(
      "href",
      "/strategy/run-ledger?runId=run-42"
    );
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("still resolves a run entry when the manual workbench refuses the caller", async () => {
    // A ViewStrategies-only operator is refused by the manual workbench but authorized on the run
    // journal. The workbench's rejection must not decide the screen, or the run's own evidence is
    // reported as an unavailable workbench record to the very operators it is meant for.
    vi.mocked(api.getManualJournalEntryWorkbench).mockRejectedValueOnce(new Error("forbidden"));
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce([journalLine]);

    await renderScreen("/accounting/journal-entries/detail?journalEntryId=je-posted-1&runId=run-42");

    expect(await screen.findByText("Summary-only entry")).toBeInTheDocument();
    expect(api.getRunLedgerJournal).toHaveBeenCalledWith("run-42");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("renders a not-found state when the entry cannot be located anywhere", async () => {
    vi.mocked(api.getManualJournalEntryWorkbench).mockResolvedValueOnce(workbench);
    vi.mocked(api.getRunLedgerJournal).mockResolvedValueOnce([]);

    await renderScreen("/accounting/journal-entries/detail?journalEntryId=unknown-je&runId=run-42");

    expect(await screen.findByRole("heading", { name: "Journal entry not found" })).toBeInTheDocument();
    expect(screen.getByText(/No matching journal entry was found/)).toBeInTheDocument();
  });

  it("renders a not-found state without a runId and does not call the run ledger fallback", async () => {
    vi.mocked(api.getManualJournalEntryWorkbench).mockResolvedValueOnce(workbench);

    await renderScreen("/accounting/journal-entries/detail?journalEntryId=unknown-je");

    expect(await screen.findByRole("heading", { name: "Journal entry not found" })).toBeInTheDocument();
    expect(api.getRunLedgerJournal).not.toHaveBeenCalled();
  });
  it("resolves a posted entry through the period journal when the link carries a periodId", async () => {
    // Trial Balance links posted entries by period. Before this the screen only knew the manual
    // workbench and strategy runs, so the fund's own postings always read as "not found".
    vi.mocked(api.getManualJournalEntryWorkbench).mockResolvedValueOnce(workbench);
    vi.mocked(ledgerReportsApi.getLedgerPeriodJournalEntries).mockResolvedValueOnce([
      {
        journalEntryId: "je-posted-1",
        timestamp: "2026-06-30T00:00:00Z",
        description: "Posted close entry",
        totalDebits: 500,
        totalCredits: 500,
        isBalanced: true,
        lines: [
          { entryId: "e-1", journalEntryId: "je-posted-1", timestamp: "2026-06-30T00:00:00Z", accountName: "Cash", accountType: "Asset", debit: 500, credit: 0, description: "Sweep in" },
          { entryId: "e-2", journalEntryId: "je-posted-1", timestamp: "2026-06-30T00:00:00Z", accountName: "Suspense", accountType: "Asset", debit: 0, credit: 500, description: "Sweep out" }
        ],
        dimensions: null
      }
    ] as never);

    await renderScreen("/accounting/journal-entries/detail?journalEntryId=je-posted-1&periodId=period-7");

    // The period response already carried full posting lines; the drill-through must render them
    // rather than reporting "summary only" over detail it was handed.
    expect(await screen.findByRole("table")).toBeInTheDocument();
    expect(screen.getByText("Sweep in")).toBeInTheDocument();
    expect(screen.queryByText("Summary-only entry")).not.toBeInTheDocument();
    expect(ledgerReportsApi.getLedgerPeriodJournalEntries).toHaveBeenCalledWith("period-7");
    expect(screen.queryByRole("heading", { name: "Journal entry not found" })).not.toBeInTheDocument();
    // The posted journal and a strategy run are different books; a period scope must never
    // fall back to the run ledger.
    expect(api.getRunLedgerJournal).not.toHaveBeenCalled();
  });
  it("renders a posted entry as governed evidence in its book currency", async () => {
    // A posted entry is "full" in the same sense a draft is, but it has no approval workflow,
    // no lifecycle and no attached evidence, and its amounts are in the book's own currency.
    // Deciding those from completeness rendered manual-workflow panels over a governed posting
    // and labelled a EUR book's lines as dollars.
    vi.mocked(api.getManualJournalEntryWorkbench).mockResolvedValueOnce(workbench);
    vi.mocked(ledgerReportsApi.getLedgerBooks).mockResolvedValueOnce([
      { ledgerBookId: "book-eur", displayName: "Feeder Fund", baseCurrency: "EUR" }
    ] as never);
    vi.mocked(ledgerReportsApi.getLedgerPeriodJournalEntries).mockResolvedValueOnce([
      {
        journalEntryId: "je-posted-1",
        periodId: "period-7",
        ledgerBookId: "book-eur",
        timestamp: "2026-06-30T00:00:00Z",
        description: "Posted close entry",
        totalDebits: 500,
        totalCredits: 500,
        isBalanced: true,
        lines: [
          { entryId: "e-1", journalEntryId: "je-posted-1", timestamp: "2026-06-30T00:00:00Z", accountName: "Cash", accountType: "Asset", debit: 500, credit: 0, description: "Sweep in" }
        ],
        dimensions: null
      }
    ] as never);

    await renderScreen("/accounting/journal-entries/detail?journalEntryId=je-posted-1&periodId=period-7");
    await waitForAsyncEffects();

    expect(await screen.findByText("Governed posted journal")).toBeInTheDocument();
    expect(screen.queryByText("Manual journal workbench")).not.toBeInTheDocument();
    // No manual workflow behind a posted entry, so none of its panels — and in particular no
    // "Approval pending" that can never resolve.
    expect(screen.queryByRole("heading", { name: "Approval" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Lifecycle" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Evidence" })).not.toBeInTheDocument();
    expect(screen.queryByText("Approval pending")).not.toBeInTheDocument();
    // Book currency, not dollars.
    expect(screen.getByRole("table")).toHaveTextContent("€");
    expect(screen.getByRole("table")).not.toHaveTextContent("$");
  });
});

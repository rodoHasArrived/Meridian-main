import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { CapitalAccountWorkbenchPanel } from "@/screens/accounting-screen.capital-account-workbench-panel";
import { useCapitalAccountWorkbenchViewModel } from "@/screens/accounting-screen.capital-accounts.view-model";
import type { CapitalAccountWorkbenchServices } from "@/screens/accounting-screen.view-model";
import { renderWithRouter } from "@/test/render";
import type { CapitalAccountWorkbench } from "@/types";

type InvestorAccount = CapitalAccountWorkbench["investorAccounts"][number];
type SubledgerEntry = InvestorAccount["subledgerEntries"][number];

function subledgerEntry(
  subledgerEntryId: string,
  fundEventType: string,
  effectiveDate: string,
  netCapitalActivity: number,
  runningNetActivity: number
): SubledgerEntry {
  return {
    subledgerEntryId,
    capitalAccountId: "capital-account:fund-alpha:lp-1",
    investorId: "investor:lp-1",
    currency: "USD",
    fundEventId: `fund-event:${subledgerEntryId}`,
    fundEventType,
    entryType: "CapitalCall",
    approvalState: "Posted",
    journalEntryId: `je-${subledgerEntryId}`,
    effectiveDate,
    grossAmount: Math.abs(netCapitalActivity),
    netCapitalActivity,
    runningNetActivity,
    memo: "",
    evidenceLinks: [],
    validationIssues: [],
    updatedAtUtc: "2026-06-30T00:00:00Z",
    isPosted: true
  };
}

function investorAccount(investorId: string, overrides: Partial<InvestorAccount> = {}): InvestorAccount {
  return {
    accountKey: `capital-account:fund-alpha:${investorId}|investor:${investorId}|USD`,
    capitalAccountId: `capital-account:fund-alpha:${investorId}`,
    investorId: `investor:${investorId}`,
    currency: "USD",
    activityRoute: `/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3A${investorId}`,
    readiness: "Ready",
    readinessLabel: "Ready",
    readinessReason: "Retained evidence and statement support are available.",
    nextAction: "Open statement",
    nextActionRoute: null,
    openingNetActivity: 0,
    endingNetActivity: 125,
    netCapitalActivity: 125,
    contributions: 125,
    distributions: 0,
    subscriptions: 0,
    redemptions: 0,
    managementFees: 0,
    fundEventCount: 0,
    postedFundEventCount: 0,
    approvalQueueCount: 0,
    publishedReportOutputCount: 0,
    evidenceLinkCount: 0,
    validationIssueCount: 0,
    evidenceCategorySummary: "No evidence categories projected.",
    evidenceLinks: [],
    evidenceCategories: [],
    fundEventRecords: [],
    subledgerEntries: [],
    ledgerImpacts: [],
    reportOutputs: [],
    validationIssues: [],
    paymentIntentEvidence: null,
    ...overrides
  };
}

function workbench(investorAccounts: InvestorAccount[]): CapitalAccountWorkbench {
  return {
    fundProfileId: "fund-alpha",
    ledgerBookId: "book-alpha",
    projectedAtUtc: "2026-06-30T17:00:00Z",
    capitalAccountId: null,
    investorId: null,
    currency: "USD",
    workbenchRoute: "/api/ledger/private-capital/capital-account-workbench?fundProfileId=fund-alpha",
    statusLabel: "Ready",
    statusReason: "Capital account projection loaded.",
    investorAccountCount: investorAccounts.length,
    fundEventCount: 0,
    statementCount: 0,
    restatementLineageCount: 0,
    auditDrillThroughCount: 0,
    netCapitalActivity: 125,
    investorAccounts,
    allocationRules: [],
    statementLineage: [],
    auditDrillThroughs: [],
    validationIssues: [],
    liveCapabilities: [],
    plannedCapabilities: []
  };
}

function Harness({ services }: { services: CapitalAccountWorkbenchServices }) {
  const view = useCapitalAccountWorkbenchViewModel(true, "", services);
  return <CapitalAccountWorkbenchPanel view={view} />;
}

async function renderPanel(investorAccounts: InvestorAccount[]) {
  const services: CapitalAccountWorkbenchServices = {
    getWorkbench: vi.fn().mockResolvedValue(workbench(investorAccounts))
  };
  const result = renderWithRouter(<Harness services={services} />);
  await waitFor(() => expect(screen.getByText("Investor capital accounts")).toBeInTheDocument());
  return result;
}

function proofFor(accountTitle: string): HTMLDetailsElement {
  const summary = screen.getByText(`for ${accountTitle}`, { exact: false }).closest("summary");
  const details = summary?.closest("details");
  if (!details) {
    throw new Error(`No rollforward proof disclosure for ${accountTitle}`);
  }

  return details;
}

describe("CapitalAccountWorkbenchPanel rollforward proof", () => {
  it("proves each investor account from its subledger entries and keeps the existing roll-forward text", async () => {
    await renderPanel([
      investorAccount("lp-1", {
        openingNetActivity: 100,
        endingNetActivity: 180,
        netCapitalActivity: 80,
        subledgerEntries: [
          subledgerEntry("entry-1", "CapitalCall", "2026-06-10", 125, 225),
          subledgerEntry("entry-2", "Distribution", "2026-06-20", -45, 180)
        ]
      })
    ]);

    // Existing labels are untouched.
    expect(screen.getByText("+$100.00 USD opening → +$180.00 USD ending")).toBeInTheDocument();
    expect(screen.getByText(/Contributions \$125 USD \/ Distributions \$0 USD/)).toBeInTheDocument();

    const proof = proofFor("LP 1 capital account");
    // A tied proof stays closed; its verdict is still readable in the summary.
    expect(proof).not.toHaveAttribute("open");
    expect(within(proof).getByText("Ties")).toHaveAttribute("data-severity", "ready");

    await userEvent.click(within(proof).getByText("Rollforward proof"));

    expect(proof).toHaveAttribute("open");
    expect(within(proof).getByText("Opening net activity")).toBeInTheDocument();
    expect(within(proof).getByText("(derived)")).toBeInTheDocument();
    expect(within(proof).getByText("$100.00")).toBeInTheDocument();
    expect(within(proof).getByText("CapitalCall")).toBeInTheDocument();
    expect(within(proof).getByText("2026-06-10")).toBeInTheDocument();
    expect(within(proof).getByText("$125.00")).toBeInTheDocument();
    expect(within(proof).getByText("Distribution")).toBeInTheDocument();
    expect(within(proof).getByText("$45.00")).toBeInTheDocument();
    expect(within(proof).getByText("Ending net activity")).toBeInTheDocument();
    expect(within(proof).getByText("$180.00")).toBeInTheDocument();
    expect(within(proof).getByText("Opening + movements = closing · 2 movements")).toBeInTheDocument();
    expect(within(proof).getByText(/Opening is derived by the capital-account projection/)).toBeInTheDocument();
  });

  it("opens the proof and prints the out-by amount when the ending does not tie", async () => {
    await renderPanel([
      investorAccount("lp-2", {
        openingNetActivity: 100,
        endingNetActivity: 200,
        subledgerEntries: [
          subledgerEntry("entry-1", "CapitalCall", "2026-06-10", 125, 225),
          subledgerEntry("entry-2", "Distribution", "2026-06-20", -45, 180)
        ]
      })
    ]);

    const proof = proofFor("LP 2 capital account");
    expect(proof).toHaveAttribute("open");
    expect(within(proof).getByText("Out by $20.00")).toHaveAttribute("data-severity", "blocked");
    expect(within(proof).getByText("Out by $20.00 — movements do not explain the closing balance")).toBeInTheDocument();
    expect(within(proof).queryByText(/Opening \+ movements = closing/)).not.toBeInTheDocument();
  });

  it("does not present an account with no subledger entries as a passed proof", async () => {
    await renderPanel([investorAccount("lp-3")]);

    const proof = proofFor("LP 3 capital account");
    // Not green, not "Ties": opening + one net movement = ending cannot fail for this account.
    expect(within(proof).queryByText("Ties")).not.toBeInTheDocument();
    expect(within(proof).getByText("Not itemised")).toHaveAttribute("data-severity", "review");
    expect(within(proof).queryByText(/Opening \+ movements = closing/)).not.toBeInTheDocument();
    // The reason is readable without expanding anything.
    expect(proof).toHaveAttribute("open");
    const reason = within(proof).getByText(/Not proved — no subledger entries are loaded for this account/);
    expect(reason).toBeVisible();
    expect(within(proof).getByText(/Opening is set by the capital-account projection with no subledger entries behind it/)).toBeVisible();
    // The single net movement row the trace always shows for this case is still there.
    expect(within(proof).getByText("Net capital activity")).toBeVisible();
    expect(within(proof).getAllByText("$125.00")).toHaveLength(2);
  });

  it("still reports an out-by for an account with no subledger entries", async () => {
    await renderPanel([investorAccount("lp-4", { openingNetActivity: 0, netCapitalActivity: 125, endingNetActivity: 200 })]);

    const proof = proofFor("LP 4 capital account");
    expect(proof).toHaveAttribute("open");
    expect(within(proof).getByText("Out by $75.00")).toHaveAttribute("data-severity", "blocked");
    expect(within(proof).queryByText("Not itemised")).not.toBeInTheDocument();
  });

  it("prints a missing ending in words beside a zero opening instead of two identical dashes", async () => {
    await renderPanel([investorAccount("lp-5", { openingNetActivity: 0, endingNetActivity: Number.NaN })]);

    const proof = proofFor("LP 5 capital account");
    expect(proof).toHaveAttribute("open");
    expect(within(proof).getByText("Not provable")).toHaveAttribute("data-severity", "blocked");
    const opening = within(proof).getByText("Opening net activity").closest("div") as HTMLElement;
    const ending = within(proof).getByText("Ending net activity").closest("div") as HTMLElement;
    expect(within(opening).getByText("—")).toHaveAttribute("aria-hidden", "true");
    expect(within(opening).getByText("zero")).toBeInTheDocument();
    expect(within(ending).getByText("Not available")).toBeVisible();
    expect(within(ending).queryByText("—")).not.toBeInTheDocument();
  });

  it("adds no table beyond the two the workbench already renders", async () => {
    await renderPanel([investorAccount("lp-1"), investorAccount("lp-2")]);

    // Investor accounts only: the fund-event table is absent without fund-event records.
    expect(screen.getAllByRole("table")).toHaveLength(1);
    // One proof disclosure per investor row, inside the workbench table's "Rollforward proof" column.
    expect(screen.getByRole("table").querySelectorAll("details")).toHaveLength(2);
  });

  it("has no accessibility violations with tied, not itemised and out-of-tie proofs", async () => {
    const { container } = await renderPanel([
      investorAccount("lp-1"),
      investorAccount("lp-3", {
        openingNetActivity: 100,
        endingNetActivity: 225,
        subledgerEntries: [subledgerEntry("entry-1", "CapitalCall", "2026-06-10", 125, 225)]
      }),
      investorAccount("lp-2", {
        openingNetActivity: 100,
        endingNetActivity: 200,
        subledgerEntries: [subledgerEntry("entry-1", "CapitalCall", "2026-06-10", 125, 225)]
      })
    ]);

    expect((await axe(container)).violations).toEqual([]);
  });
});

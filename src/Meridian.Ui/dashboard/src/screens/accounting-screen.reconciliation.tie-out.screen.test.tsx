import { act, screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { useNavigate, type NavigateFunction } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { ACCOUNTING_SYSTEM_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { getScenario, type ApiScenario } from "@/scenarios";
import { accountingPayload } from "@/scenarios/accounting-payload";
import { AccountingScreen } from "@/screens/accounting-screen";
import { renderWithRouter } from "@/test/render";
import { installScenario } from "@/test/scenarios";
import type { AccountingSystemReconciliationSummary } from "@/types";

// Renders the real Accounting screen through the real API client, so these tests fail if the
// tie-out mount under "Reconciliation exceptions and evidence" is removed or miswired. The
// sibling accounting-screen.reconciliation.tie-out.test.tsx pins the composition's rules in a
// light harness; this file pins that the screen actually mounts it.
//
// Responses come from the shared scenario runtime: the external GL reconciliation is stated
// here, and every other read the screen makes is served by the development fixtures.

type Row = AccountingSystemReconciliationSummary["rows"][number];

// The external GL reconciliation is read on the External GL workstream; the exceptions band that
// compares it lives on the Reconciliation workstream. Both are the one mounted Accounting screen
// (`/accounting/*`), so the operator's path is: open External GL, then open Reconciliation.
const externalGlRoute = "/accounting/reconciliation/external-gl";
const exceptionsRoute = "/accounting/reconciliation#accounting-exceptions";

let navigateTo: NavigateFunction = () => undefined;
function CaptureNavigate() {
  navigateTo = useNavigate();
  return null;
}
const comparisonName = "Cash reconciliation broker statement versus ledger comparison";

function row(rowId: string, accountName: string, status: Row["status"], external: number, meridian: number): Row {
  return {
    rowId,
    accountCode: `Assets:${accountName}`,
    accountName,
    currency: "USD",
    status,
    externalDebit: Math.max(external, 0),
    externalCredit: Math.max(-external, 0),
    meridianDebit: Math.max(meridian, 0),
    meridianCredit: Math.max(-meridian, 0),
    variance: external - meridian,
    detail: `${accountName} detail.`,
    evidenceRef: `quickbooks-fixture:trial-balance:${rowId}`
  };
}

function systemReconciliation(rows: Row[], breakCount: number): AccountingSystemReconciliationSummary {
  return {
    reconciliationId: "gl-recon-tie-out",
    importId: "gl-import-tie-out",
    providerId: "quickbooks-fixture",
    fundProfileId: "default-fund",
    periodStart: "2026-01-01",
    periodEnd: "2026-01-31",
    generatedAtUtc: "2026-02-01T00:05:00Z",
    matchedCount: rows.filter((item) => item.status === "Matched").length,
    breakCount,
    totalExternalDebits: rows.reduce((total, item) => total + item.externalDebit, 0),
    totalExternalCredits: rows.reduce((total, item) => total + item.externalCredit, 0),
    totalMeridianDebits: rows.reduce((total, item) => total + item.meridianDebit, 0),
    totalMeridianCredits: rows.reduce((total, item) => total + item.meridianCredit, 0),
    postingEnabled: false,
    postingDisabledReason: "Posting/export remains disabled.",
    evidenceReferences: ["quickbooks-fixture:trial-balance"],
    rows
  };
}

/** Render the real screen, let External GL read `reconciliation`, then open the exceptions band. */
async function renderAccountingScreen(reconciliation: AccountingSystemReconciliationSummary | null) {
  const base = getScenario("accounting.normal");
  const scenario: ApiScenario = {
    ...base,
    id: "accounting.tie-out-mount",
    responses: [
      {
        method: "GET",
        path: ACCOUNTING_SYSTEM_API_ENDPOINTS.reconciliationLatest,
        response: { status: 200, body: reconciliation }
      },
      ...base.responses
    ]
  };
  // Not strict: reads this file does not state fall through to the development fixtures.
  installScenario(scenario, { strict: false });

  // Record what the screen asks for, so a test can wait for the external GL load to finish.
  const scenarioFetch = globalThis.fetch;
  const requestedPaths: string[] = [];
  vi.stubGlobal("fetch", ((input, init) => {
    const url = typeof input === "string" || input instanceof URL ? String(input) : input.url;
    requestedPaths.push(new URL(url, "http://meridian.local").pathname);
    return scenarioFetch(input, init);
  }) as typeof fetch);

  const result = renderWithRouter(
    <><CaptureNavigate /><AccountingScreen data={accountingPayload} /></>,
    { initialEntries: [externalGlRoute] }
  );
  await waitFor(
    () => expect(requestedPaths).toContain(ACCOUNTING_SYSTEM_API_ENDPOINTS.reconciliationLatest),
    { timeout: 10_000 }
  );
  await settle();
  await settle();
  await act(async () => {
    await navigateTo(exceptionsRoute);
  });
  return { ...result, requestedPaths };
}

async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

describe("AccountingScreen reconciliation tie-out mount", () => {
  it("mounts the tie-out proof under the comparison summary for a loaded external GL reconciliation", async () => {
    await renderAccountingScreen(
      systemReconciliation([row("cash", "Cash", "MissingMeridian", 100, 0)], 1)
    );

    const proof = await screen.findByRole("region", { name: "Tie-out proof" }, { timeout: 10_000 });
    const comparison = screen.getByRole("region", { name: comparisonName });

    expect(proof).toBeVisible();
    // The tie-out is the proof of the summary directly above it, never a second verdict.
    expect(comparison).toHaveTextContent("Out by $100");
    expect(comparison.compareDocumentPosition(proof) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(comparison.parentElement).toBe(proof.parentElement);
    expect(proof).toHaveAttribute("data-tie-out-status", "exception");
    expect(within(proof).getByText("Custodian statement")).toBeInTheDocument();
    expect(within(proof).getByText("Internal ledger")).toBeInTheDocument();
    expect(within(proof).getByText("+$100.00")).toBeInTheDocument();
    expect(proof).not.toHaveTextContent("Tied out");
    // Read-only: nothing here is backed by an endpoint, so nothing here offers an action.
    expect(proof).not.toHaveTextContent("quickbooks-fixture:trial-balance:cash");
    expect(proof).not.toHaveTextContent(/Accept|Resolve|Escalate|Reopen|Add reconciling item|Reviewed by/);
    expect((await axe(proof)).violations).toEqual([]);
  }, 30_000);

  it("heads the tie-out at the same level as its sibling cards in the exceptions band", async () => {
    await renderAccountingScreen(
      systemReconciliation([row("cash", "Cash", "Matched", 500, 500)], 0)
    );

    const proof = await screen.findByRole("region", { name: "Tie-out proof" }, { timeout: 10_000 });
    const band = screen.getByRole("region", { name: "Reconciliation exceptions and evidence" });
    const comparison = within(band).getByRole("region", { name: comparisonName });

    // A balanced summary and a tied proof: one verdict, stated twice from the same figures.
    expect(within(comparison).getByText("Balanced")).toBeInTheDocument();
    expect(proof).toHaveAttribute("data-tie-out-status", "tied");
    expect(within(proof).getByText("Tied out")).toBeInTheDocument();

    // An h4 here would read as a subsection of the card above ("Queue readiness").
    expect(within(proof).getByRole("heading", { level: 3, name: "Tie-out proof" })).toBeInTheDocument();
    expect(within(band).getByRole("heading", { level: 3, name: "Queue readiness" })).toBeInTheDocument();
    expect(within(band).queryAllByRole("heading", { level: 4, name: "Tie-out proof" })).toHaveLength(0);
  }, 30_000);

  it("renders no tie-out proof when no external GL reconciliation exists", async () => {
    await renderAccountingScreen(null);
    await settle();

    expect(await screen.findByRole("region", { name: comparisonName })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Tie-out proof" })).not.toBeInTheDocument();
  }, 30_000);
});

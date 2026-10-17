import { describe, expect, it } from "vitest";
import { buildNumberPassportItems } from "@/components/meridian/number-passport";
import type { FinancialRecordExplorerDto, FinancialRecordExplorerSelectedRecordDto } from "@/types";

describe("NumberPassport", () => {
  it("uses only stable action and relationship identifiers for evidence links", () => {
    const items = buildNumberPassportItems(createExplorer(), createRecord());
    const byLabel = Object.fromEntries(items.map((item) => [item.label, item]));

    expect(byLabel.Source?.value).toBe("Ledger account");
    expect(byLabel.Freshness?.value).toBe("Review required: no structured freshness marker");
    expect(byLabel.Reconciliation?.value).toBe("Review required: unverified record navigation /accounting/reconciliation?caseId=recon-1");
    expect(byLabel.Approvals?.value).toBe("Review required: unverified record navigation /accounting/approvals?approvalId=approval-1");
    expect(byLabel["Report Usage"]?.value).toBe("Review required: unverified record navigation /reporting/report-packs?lineKey=cash");
    expect(byLabel.Blockers?.value).toBe("Review required: proof completeness is unverified");
    expect(byLabel["Evidence Packet"]?.value).toBe("Review required: unverified record navigation /reporting/evidence?subjectId=cash");
    expect(byLabel["Audit Trail"]?.value).toBe("Review required: unverified record navigation /accounting/audit?recordId=cash");
  });

  it("uses explicit empty-state fallbacks for missing proof evidence", () => {
    const record = {
      ...createRecord(),
      tone: "Danger",
      description: "Approval evidence is missing.",
      fields: [],
      proofActions: [],
      usedIn: [],
      impacts: [],
      fullRecordHref: ""
    } satisfies FinancialRecordExplorerSelectedRecordDto;
    const items = buildNumberPassportItems(createExplorer(), record);

    expect(findItem(items, "Freshness").value).toBe("Review required: no structured freshness marker");
    expect(findItem(items, "Reconciliation").value).toBe("Review required: no reconciliation evidence");
    expect(findItem(items, "Approvals").value).toBe("Review required: no approval evidence");
    expect(findItem(items, "Report Usage").value).toBe("Review required: no report usage evidence");
    expect(findItem(items, "Blockers").value).toBe("Approval evidence is missing.");
    expect(findItem(items, "Evidence Packet").value).toBe("Review required: no evidence packet");
    expect(findItem(items, "Audit Trail").value).toBe("Review required: no audit trail evidence");
  });
  it("does not infer proof from matching labels, symbols, routes or a generic source action", () => {
    const record = createRecord();
    record.usedIn = record.usedIn.map((item) => ({ ...item, relationshipId: "unrelated-fund-report" }));
    record.impacts = record.impacts.map((item) => ({ ...item, relationshipId: `foreign-${item.relationshipId}` }));
    record.proofActions = record.proofActions.map((item) => ({ ...item, actionId: `foreign-${item.actionId}` }));
    const items = buildNumberPassportItems(createExplorer(), record);
    for (const label of ["Reconciliation", "Approvals", "Report Usage", "Evidence Packet", "Audit Trail"]) {
      expect(findItem(items, label).value).toMatch(/^Review required:/);
    }
  });

  it("treats duplicate evidence identifiers as ambiguous", () => {
    const record = createRecord();
    record.proofActions.push({ ...record.proofActions[1]!, href: "/foreign-fund/evidence" });
    expect(findItem(buildNumberPassportItems(createExplorer(), record), "Evidence Packet").value)
      .toBe("Review required: no evidence packet");
  });

});

function findItem(items: ReturnType<typeof buildNumberPassportItems>, label: string) {
  const item = items.find((candidate) => candidate.label === label);
  expect(item).toBeDefined();
  return item!;
}

function createExplorer(): FinancialRecordExplorerDto {
  return {
    explorerId: "ledger",
    title: "Ledger Explorer",
    description: "Explore retained ledger records.",
    sourceState: "Source-backed ledger projection from run run-1.",
    isBlocked: false,
    blockedReason: "",
    scopeItems: [],
    savedViews: [],
    summaryItems: [],
    filters: [],
    columns: [],
    rows: [],
    selectedRecord: null,
    proofActions: [],
    recordGraph: { nodes: [], edges: [] }
  };
}

function createRecord(): FinancialRecordExplorerSelectedRecordDto {
  return {
    recordId: "ledger:run-1:cash",
    recordType: "Ledger account",
    title: "Cash",
    subtitle: "Assets - run-1",
    description: "Source-backed cash balance with a statement evidence gap.",
    tone: "Warning",
    fields: [
      {
        label: "Last updated",
        value: "2026-06-30 14:00 UTC",
        detail: "Refreshed from retained ledger projection.",
        tone: "Info"
      },
      {
        label: "Blocker",
        value: "Custodian statement proof missing",
        detail: "Controller owns the statement proof upload.",
        tone: "Warning"
      }
    ],
    proofActions: [
      {
        actionId: "open-source",
        label: "Open source record",
        description: "Open retained source.",
        href: "/accounting/source?recordId=cash",
        isEnabled: true,
        disabledReason: "",
        tone: "Info"
      },
      {
        actionId: "evidence-packet",
        label: "Evidence packet",
        description: "Open retained evidence packet.",
        href: "/reporting/evidence?subjectId=cash",
        isEnabled: true,
        disabledReason: "",
        tone: "Info"
      },
      {
        actionId: "audit-trail",
        label: "Audit trail",
        description: "Open retained audit trail.",
        href: "/accounting/audit?recordId=cash",
        isEnabled: true,
        disabledReason: "",
        tone: "Info"
      }
    ],
    usedIn: [
      {
        relationshipId: "report-line",
        label: "Report Usage",
        description: "Cash appears in the board report pack.",
        href: "/reporting/report-packs?lineKey=cash",
        tone: "Info"
      }
    ],
    impacts: [
      {
        relationshipId: "approval-gate",
        label: "Approval gate",
        description: "Controller approval is required.",
        href: "/accounting/approvals?approvalId=approval-1",
        tone: "Warning"
      },
      {
        relationshipId: "reconciliation-case",
        label: "Reconciliation case",
        description: "Statement proof is required before the case can close.",
        href: "/accounting/reconciliation?caseId=recon-1",
        tone: "Warning"
      }
    ],
    fullRecordHref: "/accounting/ledger?recordId=cash"
  };
}

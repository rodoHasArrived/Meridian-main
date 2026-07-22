import { describe, expect, it } from "vitest";
import { buildNumberPassportItems } from "@/components/meridian/number-passport";
import type { FinancialRecordExplorerDto, FinancialRecordExplorerSelectedRecordDto } from "@/types";

describe("NumberPassport", () => {
  it("prefers explicit evidence, blocker, audit, approval, reconciliation, and report proof", () => {
    const items = buildNumberPassportItems(createExplorer(), createRecord());
    const byLabel = Object.fromEntries(items.map((item) => [item.label, item]));

    expect(byLabel.Source?.value).toBe("Ledger account");
    expect(byLabel.Freshness?.value).toBe("2026-06-30 14:00 UTC");
    expect(byLabel.Reconciliation?.value).toBe("/accounting/reconciliation?caseId=recon-1");
    expect(byLabel.Approvals?.value).toBe("/accounting/approvals?approvalId=approval-1");
    expect(byLabel["Report Usage"]?.value).toBe("/reporting/report-packs?lineKey=cash");
    expect(byLabel.Blockers?.value).toBe("Custodian statement proof missing");
    expect(byLabel["Evidence Packet"]?.value).toBe("/reporting/evidence?subjectId=cash");
    expect(byLabel["Audit Trail"]?.value).toBe("/accounting/audit?recordId=cash");
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

    expect(findItem(items, "Freshness").value).toBe("Source-backed ledger projection from run run-1.");
    expect(findItem(items, "Reconciliation").value).toBe("No reconciliation link on selected record");
    expect(findItem(items, "Approvals").value).toBe("No approval link on selected record");
    expect(findItem(items, "Report Usage").value).toBe("No report usage link on selected record");
    expect(findItem(items, "Blockers").value).toBe("Approval evidence is missing.");
    expect(findItem(items, "Evidence Packet").value).toBe("No evidence packet link");
    expect(findItem(items, "Audit Trail").value).toBe("Source-backed ledger projection from run run-1.");
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

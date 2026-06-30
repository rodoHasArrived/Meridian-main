import { describe, expect, it } from "vitest";
import { buildNumberPassportItems } from "@/components/meridian/number-passport";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

describe("buildNumberPassportItems", () => {
  it("builds the standard proof fields from selected record evidence", () => {
    const items = buildNumberPassportItems(createExplorer(), createRecord());

    expect(items.map((item) => item.label)).toEqual([
      "Source",
      "Freshness",
      "Reconciliation",
      "Approvals",
      "Report Usage",
      "Blockers",
      "Evidence Packet",
      "Audit Trail"
    ]);
    expect(findItem(items, "Freshness").value).toBe("2026-06-30T00:00:00Z");
    expect(findItem(items, "Reconciliation").value).toBe("/reconciliation/case-42");
    expect(findItem(items, "Approvals").value).toBe("/approvals/package-7");
    expect(findItem(items, "Report Usage").value).toBe("/reports/board-pack/line-9");
    expect(findItem(items, "Evidence Packet").value).toBe("/evidence/passport-42");
    expect(findItem(items, "Audit Trail").value).toBe("/audit/number-42");
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

    expect(findItem(items, "Freshness").value).toBe("Source-backed close package from 2026-06-30.");
    expect(findItem(items, "Reconciliation").value).toBe("No reconciliation link on selected record");
    expect(findItem(items, "Approvals").value).toBe("No approval link on selected record");
    expect(findItem(items, "Report Usage").value).toBe("No report usage link on selected record");
    expect(findItem(items, "Blockers").value).toBe("Approval evidence is missing.");
    expect(findItem(items, "Evidence Packet").value).toBe("No evidence packet link");
    expect(findItem(items, "Audit Trail").value).toBe("Source-backed close package from 2026-06-30.");
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
    description: "Close evidence.",
    sourceState: "Source-backed close package from 2026-06-30.",
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
    recordId: "ledger:number-42",
    recordType: "Ledger balance",
    title: "Cash balance",
    subtitle: "Operating cash",
    description: "Validated cash balance.",
    tone: "Success",
    fields: [
      { label: "As of", value: "2026-06-30T00:00:00Z", detail: "Close timestamp.", tone: "Info" }
    ],
    proofActions: [
      {
        actionId: "passport",
        label: "Open evidence passport",
        description: "Primary evidence packet.",
        href: "/evidence/passport-42",
        isEnabled: true,
        disabledReason: "",
        tone: "Info"
      }
    ],
    usedIn: [
      {
        relationshipId: "report",
        label: "Report line",
        description: "Board pack report usage.",
        href: "/reports/board-pack/line-9",
        tone: "Info"
      },
      {
        relationshipId: "approval",
        label: "Approval package",
        description: "Controller approval evidence.",
        href: "/approvals/package-7",
        tone: "Success"
      }
    ],
    impacts: [
      {
        relationshipId: "reconciliation",
        label: "Reconciliation case",
        description: "Matched cash proof.",
        href: "/reconciliation/case-42",
        tone: "Success"
      },
      {
        relationshipId: "audit",
        label: "Audit trail",
        description: "Audit events for this number.",
        href: "/audit/number-42",
        tone: "Info"
      }
    ],
    fullRecordHref: "/records/number-42"
  };
}

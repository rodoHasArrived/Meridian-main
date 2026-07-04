import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { StatementImportPanel } from "@/screens/statement-import-panel";
import type { StatementImportPanelServices } from "@/screens/statement-import-panel.view-model";
import { renderWithRouter } from "@/test/render";
import type {
  StatementConnectorDescriptor,
  StatementImportCommitResult,
  StatementImportPreview,
  StatementMappingProfile
} from "@/types";

const connectors: StatementConnectorDescriptor[] = [
  {
    connectorId: "generic-csv",
    displayName: "Generic CSV",
    fileExtensions: [".csv"],
    supportsFileImport: true,
    supportsRemoteFetch: false,
    requiresMappingProfile: false,
    defaultProfileId: null
  }
];

const profiles: StatementMappingProfile[] = [
  {
    schemaVersion: 1,
    profileId: "builtin-generic",
    displayName: "Built-in generic",
    format: "csv",
    csv: { delimiter: ",", quote: "\"", hasHeader: true },
    culture: null,
    dateFormats: null,
    fields: [],
    activityCodes: [],
    lastAcceptedFingerprint: null,
    isBuiltIn: true,
    notes: null
  }
];

const preview: StatementImportPreview = {
  connectorId: "generic-csv",
  connectorDisplayName: "Generic CSV",
  profileId: "builtin-generic",
  fileName: "statement.csv",
  fileSizeBytes: 64,
  detectedColumns: ["Symbol"],
  columnMappings: [
    { sourceColumn: "Symbol", canonicalField: "symbol", confidence: "Exact", score: 1, rationale: "Exact header match." },
    { sourceColumn: "Mystery", canonicalField: null, confidence: "Unmapped", score: 0, rationale: "No candidate field." }
  ],
  recordCount: 1,
  kindSummaries: [
    {
      kind: "Transaction",
      recordCount: 1,
      sampleRecords: [
        {
          kind: "Transaction",
          account: "U-100",
          symbol: "AAPL",
          quantity: 10,
          price: 187.25,
          cashAmount: -1872.5,
          activityType: "Buy",
          tradeDate: "2026-06-01",
          settlementDate: null,
          currency: "USD",
          feesCommission: null,
          externalTransactionId: "TXN-1"
        }
      ]
    }
  ],
  issues: [
    { code: "FORMAT_DRIFT", severity: "Warning", rowNumber: null, field: null, message: "Header layout drifted." }
  ],
  profileSuggestions: [{ profileId: "builtin-generic", displayName: "Built-in generic", score: 0.9 }],
  status: "ReadyToImport",
  nextAction: "Commit the import to create a reconciliation run."
};

const commitResult: StatementImportCommitResult = {
  runId: "stmt-run-77",
  duplicate: false,
  recordCount: 1,
  kindSummaries: preview.kindSummaries,
  breakCount: 1,
  caseCount: 1,
  retainedSourcePath: "reconciliation/statement-connector-imports/sc-1/statement.csv",
  retainedCanonicalPath: "reconciliation/statement-connector-imports/sc-1/canonical.csv",
  status: "Imported",
  nextAction: "Review the reconciliation queue.",
  evidenceVaultIdentity: {
    vaultId: "ev-1234567890abcdef12345678",
    subjectKind: "statement-run",
    subjectId: "stmt-run-77",
    manifestPath: "workstation/evidence/_vault/ev-1234567890abcdef12345678/manifest.json",
    manifestRoute: "/workstation/evidence/_vault/ev-1234567890abcdef12345678/manifest.json",
    retainedAt: "2026-07-02T00:00:00Z",
    contentHashSha256: "abc123",
    schemaVersion: 1,
    storageKind: "file-bundle",
    artifacts: [],
    supportRequests: [],
    requestLists: [],
    documents: [],
    manifestSnapshot: null
  },
  evidenceWorkbenchRoute: "/accounting/evidence?subjectKind=statement-run&subjectId=stmt-run-77&documentClassification=Statement",
  reconciliationRoute: "/accounting/reconciliation/match?runId=stmt-run-77",
  breakIds: ["break-cash-1"],
  caseIds: ["case:break-cash-1"],
  reconciliationCaseRoutes: ["/accounting/reconciliation/match?runId=stmt-run-77&caseId=case%3Abreak-cash-1&breakId=break-cash-1"],
  nextActions: ["Open retained statement evidence in Evidence Vault.", "Review reconciliation cases and linked statement evidence."]
};

function makeServices(): StatementImportPanelServices {
  return {
    getConnectors: vi.fn(async () => connectors),
    listMappingProfiles: vi.fn(async () => profiles),
    upsertMappingProfile: vi.fn(async (profile: StatementMappingProfile) => profile),
    previewImport: vi.fn(async () => preview),
    commitImport: vi.fn(async () => commitResult)
  };
}

describe("StatementImportPanel", () => {
  it("renders the source controls, previews an uploaded file, and stays accessible", async () => {
    const user = userEvent.setup();
    const services = makeServices();
    const { container } = renderWithRouter(<StatementImportPanel services={services} />, {
      initialEntries: ["/accounting/statement-import"]
    });

    expect(screen.getByRole("heading", { name: "Import statement" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText("Connector")).toBeEnabled());
    expect(screen.getByRole("option", { name: "Auto-detect connector" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Built-in generic (built-in)" })).toBeInTheDocument();

    const fileInput = screen.getByLabelText<HTMLInputElement>("Statement file", { selector: "input" });
    await user.upload(fileInput, new File(["Symbol\nAAPL"], "statement.csv", { type: "text/csv" }));

    await waitFor(() => expect(screen.getByRole("heading", { name: "Preview: statement.csv" })).toBeInTheDocument());
    expect(screen.getByRole("table", { name: "Statement column mappings" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Transaction/ })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("table", { name: "Sample Transaction records" })).toBeInTheDocument();
    expect(screen.getByLabelText("Statement import issues")).toHaveTextContent("FORMAT_DRIFT");
    expect(screen.getByLabelText("Suggested mapping profiles")).toHaveTextContent("Built-in generic");

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("links committed statement imports to Evidence Vault and reconciliation review", async () => {
    const user = userEvent.setup();
    const services = makeServices();
    renderWithRouter(<StatementImportPanel services={services} />, {
      initialEntries: ["/accounting/statement-import"]
    });

    await waitFor(() => expect(screen.getByLabelText("Connector")).toBeEnabled());
    const fileInput = screen.getByLabelText<HTMLInputElement>("Statement file", { selector: "input" });
    await user.upload(fileInput, new File(["Symbol\nAAPL"], "statement.csv", { type: "text/csv" }));
    await waitFor(() => expect(screen.getByRole("heading", { name: "Preview: statement.csv" })).toBeInTheDocument());

    await user.type(screen.getByLabelText("Source institution"), "Interactive Brokers");
    await user.type(screen.getByLabelText("Fund account"), "FUND-A");
    await user.type(screen.getByLabelText("External account"), "U-100");
    await user.type(screen.getByLabelText("Period start"), "2026-06-01");
    await user.type(screen.getByLabelText("Period end"), "2026-06-30");
    await user.click(screen.getByRole("button", { name: "Commit statement import" }));

    await waitFor(() => expect(screen.getByText("Statement import committed")).toBeInTheDocument());
    expect(screen.getByText("ev-1234567890abcdef12345678")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Open Evidence Vault/ })).toHaveAttribute(
      "href",
      "/accounting/evidence?subjectKind=statement-run&subjectId=stmt-run-77&documentClassification=Statement"
    );
    expect(screen.getByRole("link", { name: /Open reconciliation queue/ })).toHaveAttribute(
      "href",
      "/accounting/reconciliation/match?runId=stmt-run-77"
    );
    expect(screen.getByLabelText("Statement import reconciliation cases")).toHaveTextContent("case:break-cash-1");
    expect(screen.getByRole("link", { name: /case:break-cash-1/ })).toHaveAttribute(
      "href",
      "/accounting/reconciliation/match?runId=stmt-run-77&caseId=case%3Abreak-cash-1&breakId=break-cash-1"
    );
  });
});

import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { StatementImportPanel } from "@/screens/statement-import-panel";
import type { StatementImportPanelServices } from "@/screens/statement-import-panel.view-model";
import type { StatementFetchPanelServices } from "@/screens/statement-fetch-panel.view-model";
import { renderWithRouter } from "@/test/render";
import type {
  StatementConnectorDescriptor,
  StatementImportCommitResult,
  StatementImportPreview,
  StatementFetchSchedule,
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
  reconciliationCaseLinks: [
    {
      caseId: "case:break-cash-1",
      breakId: "break-cash-1",
      route: "/accounting/reconciliation/match?runId=stmt-run-77&caseId=case%3Abreak-cash-1&breakId=break-cash-1",
      label: "Cash break case",
      status: "Open",
      priority: "High",
      reason: "Cash statement break from imported file.",
      suggestedNextAction: "Assign the case and attach cash support."
    }
  ],
  nextActions: ["Open retained statement evidence in Evidence Vault.", "Review reconciliation cases and linked statement evidence."]
};

const remoteConnector: StatementConnectorDescriptor = {
  connectorId: "alpaca-activity",
  displayName: "Alpaca account activity",
  fileExtensions: [".json"],
  supportsFileImport: true,
  supportsRemoteFetch: true,
  requiresMappingProfile: false,
  defaultProfileId: "builtin-generic"
};

const fetchSchedule: StatementFetchSchedule = {
  scheduleId: "alpaca-daily",
  connectorId: "alpaca-activity",
  externalAccountId: "PA3ALPACA01",
  fundAccountId: "FUND-A",
  sourceInstitution: "Alpaca",
  mappingProfileId: "builtin-generic",
  toleranceProfileId: "statement-default",
  cadenceHours: 24,
  enabled: true,
  lastRunAtUtc: null,
  lastRunStatus: null,
  nextDueAtUtc: null,
  sourceKind: "broker",
  periodStart: "2026-07-01",
  periodEnd: "2026-07-31",
  accountingScope: {
    fundProfileId: "fund-a",
    ledgerBookId: "11111111-1111-1111-1111-111111111111",
    accountingPeriodId: "22222222-2222-2222-2222-222222222222",
    asOfDate: "2026-07-31"
  }
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

function makeFetchServices(): StatementFetchPanelServices {
  return {
    deleteSchedule: vi.fn(async () => undefined),
    fetchPreview: vi.fn(async () => preview),
    listSchedules: vi.fn(async () => [fetchSchedule]),
    runSchedule: vi.fn(async () => commitResult),
    upsertSchedule: vi.fn(async () => fetchSchedule)
  };
}

describe("StatementImportPanel", () => {
  it("does not dispatch an unscoped preview and retries after the account scope is complete", async () => {
    const user = userEvent.setup();
    const services = makeServices();
    renderWithRouter(<StatementImportPanel services={services} />, {
      initialEntries: ["/accounting/statement-import"]
    });

    await waitFor(() => expect(screen.getByLabelText("Connector")).toBeEnabled());
    const fileInput = screen.getByLabelText<HTMLInputElement>("Statement file", { selector: "input" });
    await user.upload(fileInput, new File(["Symbol\nAAPL"], "statement.csv", { type: "text/csv" }));

    expect(services.previewImport).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Source institution"), "Interactive Brokers");
    await user.type(screen.getByLabelText("Fund account"), "FUND-A");
    await user.type(screen.getByLabelText("External account"), "U-100");
    await user.type(screen.getByLabelText("Period start"), "2026-06-01");
    expect(services.previewImport).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Period end"), "2026-06-30");

    await waitFor(() => expect(services.previewImport).toHaveBeenCalledTimes(1));
    expect(services.previewImport).toHaveBeenCalledWith(expect.objectContaining({
      externalAccountId: "U-100",
      sourceKind: "broker",
      sourceInstitution: "Interactive Brokers",
      fundAccountId: "FUND-A",
      periodStart: "2026-06-01",
      periodEnd: "2026-06-30"
    }));
  });

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

    await user.type(screen.getByLabelText("Source institution"), "Interactive Brokers");
    await user.type(screen.getByLabelText("Fund account"), "FUND-A");
    await user.type(screen.getByLabelText("External account"), "U-100");
    await user.type(screen.getByLabelText("Period start"), "2026-06-01");
    await user.type(screen.getByLabelText("Period end"), "2026-06-30");
    const fileInput = screen.getByLabelText<HTMLInputElement>("Statement file", { selector: "input" });
    await user.upload(fileInput, new File(["Symbol\nAAPL"], "statement.csv", { type: "text/csv" }));

    await waitFor(() => expect(screen.getByRole("heading", { name: "Preview: statement.csv" })).toBeInTheDocument());
    expect(screen.getByRole("table", { name: "Statement column mappings" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Transaction/ })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("table", { name: "Sample Transaction records" })).toBeInTheDocument();
    expect(screen.getByLabelText("Statement import issues")).toHaveTextContent("FORMAT_DRIFT");
    expect(screen.getByLabelText("Suggested mapping profiles")).toHaveTextContent("Built-in generic");
    expect(services.previewImport).toHaveBeenCalledWith(expect.objectContaining({
      externalAccountId: "U-100",
      sourceKind: "broker",
      sourceInstitution: "Interactive Brokers",
      fundAccountId: "FUND-A",
      periodStart: "2026-06-01",
      periodEnd: "2026-06-30"
    }));

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
    await user.type(screen.getByLabelText("Source institution"), "Interactive Brokers");
    await user.type(screen.getByLabelText("Fund account"), "FUND-A");
    await user.type(screen.getByLabelText("External account"), "U-100");
    await user.type(screen.getByLabelText("Period start"), "2026-06-01");
    await user.type(screen.getByLabelText("Period end"), "2026-06-30");
    const fileInput = screen.getByLabelText<HTMLInputElement>("Statement file", { selector: "input" });
    await user.upload(fileInput, new File(["Symbol\nAAPL"], "statement.csv", { type: "text/csv" }));
    await waitFor(() => expect(screen.getByRole("heading", { name: "Preview: statement.csv" })).toBeInTheDocument());

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
    expect(screen.getByLabelText("Statement import reconciliation cases")).toHaveTextContent("Cash break case");
    expect(screen.getByLabelText("Statement import reconciliation cases")).toHaveTextContent("High");
    expect(screen.getByLabelText("Statement import reconciliation cases")).toHaveTextContent("Cash statement break from imported file.");
    expect(screen.getByLabelText("Statement import reconciliation cases")).toHaveTextContent("Assign the case and attach cash support.");
    expect(screen.getByRole("link", { name: /case:break-cash-1/ })).toHaveAttribute(
      "href",
      "/accounting/reconciliation/match?runId=stmt-run-77&caseId=case%3Abreak-cash-1&breakId=break-cash-1"
    );
  });

  it("opens the scheduled-fetch operator path from the Import statement route", async () => {
    const user = userEvent.setup();
    const services = makeServices();
    services.getConnectors = vi.fn(async () => [...connectors, remoteConnector]);
    renderWithRouter(
      <StatementImportPanel services={services} fetchServices={makeFetchServices()} />,
      { initialEntries: ["/accounting/statement-import"] }
    );

    await waitFor(() => expect(screen.getByRole("tab", { name: "Scheduled fetch" })).toBeEnabled());
    await user.click(screen.getByRole("tab", { name: "Scheduled fetch" }));

    await waitFor(() => expect(screen.getByRole("heading", { name: "Remote statement preview and schedule" })).toBeInTheDocument());
    expect(screen.getByRole("table", { name: "Statement fetch schedules" })).toHaveTextContent("alpaca-daily");
  });
});

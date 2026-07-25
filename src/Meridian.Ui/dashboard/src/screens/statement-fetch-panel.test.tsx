import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { StatementFetchPanel } from "@/screens/statement-fetch-panel";
import {
  validateStatementFetchDraft,
  type StatementFetchDraft,
  type StatementFetchPanelServices
} from "@/screens/statement-fetch-panel.view-model";
import { renderWithRouter } from "@/test/render";
import type {
  StatementConnectorDescriptor,
  StatementFetchSchedule,
  StatementImportCommitResult,
  StatementImportPreview,
  StatementMappingProfile
} from "@/types";

const connectors: StatementConnectorDescriptor[] = [
  {
    connectorId: "alpaca-activity",
    displayName: "Alpaca account activity",
    fileExtensions: [".json"],
    supportsFileImport: true,
    supportsRemoteFetch: true,
    requiresMappingProfile: false,
    defaultProfileId: "alpaca-activity-v1"
  },
  {
    connectorId: "csv-mapped",
    displayName: "Mapped CSV",
    fileExtensions: [".csv"],
    supportsFileImport: true,
    supportsRemoteFetch: false,
    requiresMappingProfile: true,
    defaultProfileId: "canonical-csv-v1"
  }
];

const profiles: StatementMappingProfile[] = [
  {
    schemaVersion: 1,
    profileId: "alpaca-activity-v1",
    displayName: "Alpaca activity",
    format: "json",
    csv: null,
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
  connectorId: "alpaca-activity",
  connectorDisplayName: "Alpaca account activity",
  profileId: "alpaca-activity-v1",
  fileName: "alpaca-PA3ALPACA01-20260718.json",
  fileSizeBytes: 512,
  detectedColumns: ["activity_type", "symbol", "qty"],
  columnMappings: [
    { sourceColumn: "activity_type", canonicalField: "ActivityType", confidence: "Alias", score: 0.95, rationale: "Known Alpaca alias." },
    { sourceColumn: "symbol", canonicalField: "SecurityIdentifier", confidence: "Exact", score: 1, rationale: "Exact field." },
    { sourceColumn: "qty", canonicalField: "Quantity", confidence: "Alias", score: 0.9, rationale: "Known Alpaca alias." }
  ],
  recordCount: 2,
  kindSummaries: [
    {
      kind: "Transaction",
      recordCount: 2,
      sampleRecords: [
        {
          kind: "Transaction",
          account: "PA3ALPACA01",
          symbol: "AAPL",
          quantity: 2,
          price: 190,
          cashAmount: -380,
          activityType: "Buy",
          tradeDate: "2026-07-17",
          settlementDate: null,
          currency: "USD",
          feesCommission: 0,
          externalTransactionId: "alpaca-activity-1"
        }
      ]
    }
  ],
  issues: [],
  profileSuggestions: [],
  status: "ReadyToImport",
  nextAction: "Save or run the schedule to import these records."
};

const schedule: StatementFetchSchedule = {
  scheduleId: "alpaca-daily",
  connectorId: "alpaca-activity",
  externalAccountId: "PA3ALPACA01",
  fundAccountId: "FUND-ALPHA-BROKERAGE",
  sourceInstitution: "Alpaca",
  mappingProfileId: "alpaca-activity-v1",
  toleranceProfileId: "statement-default",
  cadenceHours: 24,
  enabled: true,
  lastRunAtUtc: "2026-07-17T06:00:00Z",
  lastRunStatus: "Imported run stmt-run-88: 2 record(s), 1 case(s).",
  nextDueAtUtc: "2026-07-18T06:00:00Z",
  sourceKind: "broker"
};

const runResult: StatementImportCommitResult = {
  runId: "stmt-run-99",
  duplicate: false,
  recordCount: 2,
  kindSummaries: preview.kindSummaries,
  breakCount: 1,
  caseCount: 1,
  retainedSourcePath: "reconciliation/statement-connector-imports/stmt-run-99/alpaca.json",
  retainedCanonicalPath: "reconciliation/statement-connector-imports/stmt-run-99/canonical.csv",
  status: "Imported",
  nextAction: "Review the reconciliation queue.",
  evidenceWorkbenchRoute: "/accounting/evidence?subjectId=stmt-run-99",
  reconciliationRoute: "/accounting/reconciliation/match?runId=stmt-run-99"
};

function makeServices(): StatementFetchPanelServices {
  return {
    deleteSchedule: vi.fn(async () => undefined),
    fetchPreview: vi.fn(async () => preview),
    listSchedules: vi.fn(async () => [schedule]),
    runSchedule: vi.fn(async () => runResult),
    upsertSchedule: vi.fn(async (request) => ({
      ...schedule,
      ...request,
      scheduleId: request.scheduleId ?? "generated-schedule"
    }))
  };
}

const validDraft: StatementFetchDraft = {
  cadenceHours: "24",
  connectorId: "alpaca-activity",
  datasets: "all",
  enabled: true,
  externalAccountId: "PA3ALPACA01",
  fundAccountId: "FUND-ALPHA-BROKERAGE",
  mappingProfileId: "alpaca-activity-v1",
  scheduleId: "alpaca-daily",
  sinceDate: "2026-07-17",
  sourceInstitution: "Alpaca",
  sourceKind: "broker",
  toleranceProfileId: "statement-default"
};

describe("validateStatementFetchDraft", () => {
  it("requires a remote connector and reconciliation scope before scheduling", () => {
    expect(validateStatementFetchDraft(validDraft, connectors, "schedule")).toEqual({});
    expect(validateStatementFetchDraft({
      ...validDraft,
      cadenceHours: "0",
      connectorId: "csv-mapped",
      externalAccountId: "",
      fundAccountId: "",
      sourceInstitution: ""
    }, connectors, "schedule")).toMatchObject({
      cadenceHours: expect.any(String),
      connectorId: expect.any(String),
      externalAccountId: expect.any(String),
      fundAccountId: expect.any(String),
      sourceInstitution: expect.any(String)
    });
  });
});

describe("StatementFetchPanel", () => {
  it("previews provider activity with canonical mapping confidence and stays accessible", async () => {
    const user = userEvent.setup();
    const services = makeServices();
    const { container } = renderWithRouter(
      <StatementFetchPanel connectors={connectors} profiles={profiles} services={services} />,
      { initialEntries: ["/accounting/statement-import"] }
    );

    await waitFor(() => expect(screen.getByRole("table", { name: "Statement fetch schedules" })).toBeInTheDocument());
    await user.type(screen.getByLabelText("External account"), "PA3ALPACA01");
    await user.click(screen.getByRole("button", { name: "Preview remote statement" }));

    await waitFor(() => expect(screen.getByRole("heading", { name: `Preview: ${preview.fileName}` })).toBeInTheDocument());
    expect(screen.getByRole("table", { name: "Statement column mappings" })).toHaveTextContent("Alias");
    expect(services.fetchPreview).toHaveBeenCalledWith(expect.objectContaining({
      connectorId: "alpaca-activity",
      externalAccountId: "PA3ALPACA01",
      datasets: "all"
    }));

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("edits, saves, runs, and deletes persisted schedules", async () => {
    const user = userEvent.setup();
    const services = makeServices();
    renderWithRouter(
      <StatementFetchPanel connectors={connectors} profiles={profiles} services={services} />,
      { initialEntries: ["/accounting/statement-import"] }
    );

    await waitFor(() => expect(screen.getByRole("table", { name: "Statement fetch schedules" })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit schedule alpaca-daily" }));
    expect(screen.getByLabelText("Fund account")).toHaveValue("FUND-ALPHA-BROKERAGE");
    await user.selectOptions(screen.getByLabelText("Statement source"), "custodian");
    await user.click(screen.getByRole("button", { name: "Save fetch schedule" }));
    await waitFor(() => expect(screen.getByText("Statement fetch schedule saved")).toBeInTheDocument());
    expect(services.upsertSchedule).toHaveBeenCalledWith(expect.objectContaining({
      scheduleId: "alpaca-daily",
      connectorId: "alpaca-activity",
      cadenceHours: 24,
      enabled: true,
      sourceKind: "custodian"
    }));

    await user.click(screen.getByRole("button", { name: "Run schedule alpaca-daily" }));
    await waitFor(() => expect(screen.getByText("Scheduled statement imported")).toBeInTheDocument());
    expect(screen.getByRole("link", { name: /Open reconciliation queue/ })).toHaveAttribute(
      "href",
      "/accounting/reconciliation/match?runId=stmt-run-99"
    );

    await user.click(screen.getByRole("button", { name: "Delete schedule alpaca-daily" }));
    await waitFor(() => expect(screen.queryByRole("table", { name: "Statement fetch schedules" })).not.toBeInTheDocument());
    expect(services.deleteSchedule).toHaveBeenCalledWith("alpaca-daily");
  });
});

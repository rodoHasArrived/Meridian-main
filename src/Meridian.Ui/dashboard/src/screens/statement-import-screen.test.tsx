import { screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "@/lib/api";
import { StatementImportScreen } from "@/screens/statement-import-screen";
import { renderWithRouter } from "@/test/render";
import type { StatementConnectorDescriptor, StatementMappingProfile } from "@/types";

// The route wrapper passes no services, so it must reach the panel's default @/lib/api calls.
// StatementImportPanel's own suite injects services and never exercises that wiring.
vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getStatementConnectors: vi.fn(),
    listStatementMappingProfiles: vi.fn(),
    previewStatementImport: vi.fn(),
    commitStatementImport: vi.fn(),
    upsertStatementMappingProfile: vi.fn()
  };
});

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

function renderScreen() {
  return renderWithRouter(<StatementImportScreen />, {
    initialEntries: ["/accounting/statement-import"]
  });
}

describe("StatementImportScreen", () => {
  beforeEach(() => {
    vi.mocked(api.getStatementConnectors).mockReset().mockResolvedValue(connectors);
    vi.mocked(api.listStatementMappingProfiles).mockReset().mockResolvedValue(profiles);
    vi.mocked(api.previewStatementImport).mockReset();
    vi.mocked(api.commitStatementImport).mockReset();
    vi.mocked(api.upsertStatementMappingProfile).mockReset();
  });

  it("loads the connector library through the shared statement API", async () => {
    renderScreen();

    expect(screen.getByRole("heading", { level: 2, name: "Import statement" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText("Connector")).toBeEnabled());
    expect(screen.getByRole("option", { name: "Generic CSV" })).toBeInTheDocument();
    expect(api.getStatementConnectors).toHaveBeenCalledTimes(1);
    expect(api.listStatementMappingProfiles).toHaveBeenCalledTimes(1);
    // Nothing is previewed or committed until the operator supplies a file and scope.
    expect(api.previewStatementImport).not.toHaveBeenCalled();
    expect(api.commitStatementImport).not.toHaveBeenCalled();
  });

  it("surfaces a connector library failure instead of an empty form", async () => {
    vi.mocked(api.getStatementConnectors).mockRejectedValue(new Error("connector catalog unavailable"));

    renderScreen();

    expect(await screen.findByText("Statement connector library failed to load")).toBeInTheDocument();
  });

  it("has no detectable accessibility violations once loaded", async () => {
    const { container } = renderScreen();

    await waitFor(() => expect(screen.getByLabelText("Connector")).toBeEnabled());

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});

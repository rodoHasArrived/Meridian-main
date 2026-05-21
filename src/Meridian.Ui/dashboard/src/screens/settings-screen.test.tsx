import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { SettingsScreen } from "@/screens/settings-screen";
import { renderWithRouter } from "@/test/render";
import type {
  BrokerageConnectionStatus,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  SessionInfo,
  SystemOverviewResponse
} from "@/types";

const apiMocks = vi.hoisted(() => ({
  connectAlpacaConnection: vi.fn(),
  revokeAlpacaConnection: vi.fn()
}));

vi.mock("@/lib/api", async (importActual) => ({
  ...(await importActual<typeof import("@/lib/api")>()),
  connectAlpacaConnection: apiMocks.connectAlpacaConnection,
  revokeAlpacaConnection: apiMocks.revokeAlpacaConnection
}));

const session: SessionInfo = {
  displayName: "Andrew Rowden",
  role: "Fund Manager",
  environment: "paper",
  activeWorkspace: "settings",
  commandCount: 42
};

const overview: SystemOverviewResponse = {
  systemStatus: "Degraded",
  providersOnline: 2,
  providersTotal: 3,
  activeRuns: 1,
  openPositions: 5,
  activeBackfills: 0,
  symbolsMonitored: 120,
  storageHealth: "Warning",
  lastHeartbeatUtc: "2026-05-01T00:00:00Z",
  metrics: [],
  recentEvents: [
    {
      id: "evt-1",
      type: "warning",
      message: "Brokerage sync delayed.",
      source: "Provider health",
      timestamp: "2026-05-01T00:00:00Z"
    }
  ]
};

const alpacaConnection: BrokerageConnectionStatus = {
  providerId: "alpaca",
  displayName: "Alpaca paper",
  state: "Connected",
  isConfigured: true,
  isConnected: true,
  authorizationUrl: null,
  connectedAt: "2026-05-07T11:50:00Z",
  expiresAt: null,
  lastError: null,
  warnings: [],
  scopes: ["trading:account", "brokerage-sync:read"],
  environment: "paper",
  externalAccountId: "PA123",
  verifiedAt: "2026-05-07T11:50:00Z",
  maskedKeyId: "********1234"
};

const providerConnections: ProviderConnectionRow[] = [
  {
    providerId: "alpaca",
    displayName: "Alpaca",
    capability: "DataAndBrokerage",
    credentialState: "Verified",
    credentialSource: "LocalEncryptedStore",
    verificationState: "Verified",
    health: "Healthy",
    fallbackActive: false,
    lastVerifiedAt: "2026-05-07T11:50:00Z",
    lastSuccessfulAt: "2026-05-07T11:50:00Z",
    lastFailureAt: null,
    lastError: null,
    maskedKeyPreview: "********1234",
    environment: "paper",
    externalAccountId: "PA123",
    affectedWorkflows: ["Trading readiness", "Portfolio brokerage sync"],
    recommendedAction: "No credential repair action required.",
    actionHref: "/settings#alpaca-provider-setup"
  },
  {
    providerId: "polygon",
    displayName: "Polygon.io",
    capability: "Data",
    credentialState: "Missing",
    credentialSource: "None",
    verificationState: "NotVerified",
    health: "Warning",
    fallbackActive: true,
    lastVerifiedAt: null,
    lastSuccessfulAt: null,
    lastFailureAt: "2026-05-07T11:45:00Z",
    lastError: "Provider credential missing.",
    maskedKeyPreview: null,
    environment: null,
    externalAccountId: null,
    affectedWorkflows: ["Historical backfill"],
    recommendedAction: "Add the Polygon API key before routing data repair through Polygon.",
    actionHref: "/settings#provider-polygon-connection"
  }
];

const providerRoutingConnections: ProviderRoutingConnection[] = [
  {
    connectionId: "provider-reference",
    providerFamilyId: "polygon",
    displayName: "Reference data route",
    connectionType: "DataVendor",
    connectionMode: "ReadOnly",
    enabled: true,
    credentialReference: "vault:polygon/default",
    institutionId: null,
    externalAccountId: null,
    scope: null,
    tags: ["reference"],
    description: null,
    productionReady: true
  }
];

const providerRoutingBindings: ProviderRoutingBinding[] = [
  {
    bindingId: "provider-reference-ReferenceData",
    capability: "ReferenceData",
    connectionId: "provider-reference",
    target: null,
    priority: 100,
    enabled: true,
    failoverConnectionIds: [],
    safetyModeOverride: null,
    notes: null
  }
];

const providerRoutingTrustSnapshots: ProviderRoutingTrustSnapshot[] = [
  {
    connectionId: "provider-reference",
    providerFamilyId: "polygon",
    score: 97,
    isHealthy: true,
    healthStatus: "Healthy",
    isProductionReady: true,
    isCertificationFresh: true,
    signals: []
  }
];

const portfolio: PortfolioWorkspaceResponse = {
  metrics: [],
  positions: [],
  risk: {
    state: "Healthy",
    summary: "No portfolio risk flags.",
    netExposure: "$0",
    grossExposure: "$0",
    var95: "$0",
    maxDrawdown: "0%",
    activeGuardrails: [],
    buyingPowerUsed: "0%"
  },
  brokerage: {
    provider: "Alpaca",
    account: "PA-DEMO",
    environment: "paper",
    connection: "Connected",
    orderIngress: "healthy",
    fillFeed: "healthy",
    lastHeartbeat: "2026-05-07T12:00:00Z",
    notes: "Paper brokerage fixture is healthy."
  },
  runs: [],
  cashFlow: null
};

describe("SettingsScreen", () => {
  beforeEach(() => {
    apiMocks.connectAlpacaConnection.mockReset();
    apiMocks.revokeAlpacaConnection.mockReset();
  });

  it("renders recent events as accessible status evidence rows", () => {
    renderWithRouter(<SettingsScreen session={session} overview={overview} />);

    expect(screen.getByRole("region", { name: "Settings workbench context" })).toHaveTextContent(
      "Operator control posture"
    );
    const eventTable = screen.getByRole("table", { name: "1 recent system event" });
    const eventRow = within(eventTable).getByRole("row", {
      name: /Select event evt-1\. OBS event from Provider health at May 1, 00:00 UTC\. Brokerage sync delayed\./i
    });
    const eventDetail = screen.getByRole("complementary", { name: "Selected recent event detail" });

    expect(eventRow).toHaveAttribute("aria-selected", "true");
    expect(eventRow).toHaveAttribute("aria-controls", "settings-recent-event-detail");
    expect(eventRow).toHaveAttribute("aria-expanded", "true");
    expect(within(eventRow).getByText("OBS")).toBeInTheDocument();
    expect(within(eventRow).getByText("Brokerage sync delayed.")).toBeInTheDocument();
    expect(within(eventRow).getByText("Provider health")).toBeInTheDocument();
    expect(within(eventDetail).getByText("Brokerage sync delayed.")).toBeInTheDocument();
    expect(within(eventDetail).getByText("Provider health / evt-1")).toBeInTheDocument();
  });

  it("renders profile authentication posture with authority handoffs", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    const profileRegion = screen.getByRole("region", { name: "Profile and authentication posture" });
    expect(profileRegion).toHaveTextContent("Profile and access posture");
    expect(profileRegion).toHaveTextContent("Access ready");
    expect(profileRegion).toHaveTextContent("Andrew Rowden");
    expect(profileRegion).toHaveTextContent("Fund Manager");
    expect(profileRegion).toHaveTextContent("42 commands issued");
    expect(profileRegion).toHaveTextContent("Brokerage verified");
    expect(within(profileRegion).getByRole("list", {
      name: "Profile authentication and authorization readiness steps"
    })).toBeInTheDocument();
    expect(within(profileRegion).getByRole("link", {
      name: "Open Trading readiness from verified profile authentication posture"
    })).toHaveAttribute("href", "/trading/readiness");
    expect(within(profileRegion).getByRole("link", {
      name: "Open Settings diagnostic endpoints from profile authentication posture"
    })).toHaveAttribute("href", "/settings#diagnostic-endpoints");
    expect(document.querySelector("#diagnostic-endpoints")).toBeInTheDocument();
  });

  it("renders provider connection center with continuity repair links", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
        providerConnections={providerConnections}
        providerRoutingConnections={providerRoutingConnections}
        providerRoutingBindings={providerRoutingBindings}
        providerRoutingTrustSnapshots={providerRoutingTrustSnapshots}
        onProviderRoutingRefresh={vi.fn()}
      />
    );

    const center = screen.getByText("Provider Connection Center").closest("div");
    expect(screen.getByText("Brokerage capable")).toBeInTheDocument();
    expect(screen.getByText("Data providers")).toBeInTheDocument();
    expect(screen.getByText("Alpaca")).toBeInTheDocument();
    expect(screen.getByText("Polygon.io")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh Provider Connection Center routing data" })).toBeInTheDocument();
    expect(screen.getByText("Reference data")).toBeInTheDocument();
    expect(screen.getByText("97% · Healthy")).toBeInTheDocument();
    expect(screen.getByText("Production ready")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Alpaca provider connection row" })).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
    expect(screen.getByRole("link", { name: "Open Polygon.io provider connection row" })).toHaveAttribute(
      "href",
      "/settings#provider-polygon-connection"
    );
    expect(center).not.toHaveTextContent("endpoint-secret");
    expect(center).not.toHaveTextContent("vault:polygon/default");
  });

  it("updates recent-event detail with keyboard row selection", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={{
          ...overview,
          recentEvents: [
            ...overview.recentEvents,
            {
              id: "evt-2",
              type: "error",
              message: "Storage heartbeat missed.",
              source: "Storage",
              timestamp: "2026-05-01T00:04:00Z"
            }
          ]
        }}
      />
    );

    const storageRow = screen.getByRole("row", {
      name: /Select event evt-2\. CRIT event from Storage at May 1, 00:04 UTC\. Storage heartbeat missed\./i
    });

    storageRow.focus();
    await user.keyboard("{Enter}");

    const eventDetail = screen.getByRole("complementary", { name: "Selected recent event detail" });
    expect(storageRow).toHaveAttribute("aria-selected", "true");
    expect(within(eventDetail).getByRole("region", { name: "CRIT event detail for evt-2" })).toHaveTextContent(
      "Storage heartbeat missed."
    );
    expect(within(eventDetail).getByText("Critical")).toBeInTheDocument();
  });

  it("keeps the recent-events panel visible when there are no events", () => {
    renderWithRouter(<SettingsScreen session={session} overview={{ ...overview, recentEvents: [] }} />);

    expect(screen.getAllByText("No recent events")).toHaveLength(2);
    expect(screen.getByText("No system events reported for the active session. Diagnostic endpoints remain available below.")).toBeInTheDocument();
  });

  it("renders an alert state when overview data is unavailable", () => {
    renderWithRouter(<SettingsScreen session={session} overview={null} />);

    expect(screen.getAllByText("Event stream unavailable")).toHaveLength(2);
    expect(screen.getByRole("alert")).toHaveTextContent("Reconnect to the Meridian API");
  });

  it("labels diagnostic endpoint links", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        research={{ metrics: [], runs: [] }}
        trading={{} as never}
        portfolio={portfolio}
        dataOperations={{ metrics: [], providers: [], backfills: [], exports: [] }}
        governance={{} as never}
        reporting={{} as never}
      />
    );

    expect(screen.getByRole("link", { name: "Open System overview diagnostic endpoint" })).toHaveAttribute(
      "href",
      "/api/status"
    );
    expect(screen.getByRole("link", { name: "Open Data workspace diagnostic endpoint" })).toHaveAttribute(
      "href",
      "/api/workstation/data"
    );
    expect(screen.getByRole("link", { name: "Open Strategy workspace diagnostic endpoint" })).toHaveAttribute(
      "href",
      "/api/workstation/strategy"
    );
    expect(screen.getByRole("list", { name: "Diagnostic endpoint availability" })).toBeInTheDocument();
    expect(screen.getAllByText("All reachable").length).toBeGreaterThan(0);
  });

  it("renders the Alpaca paper connection panel", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    expect(screen.getByText("Alpaca paper API keys").closest("#alpaca-provider-setup")).toBeInTheDocument();
    expect(screen.getByRole("radiogroup", { name: "Alpaca trading environment" })).toBeInTheDocument();
    const paperEndpoint = screen.getByRole("radio", { name: "Use Alpaca paper endpoint for workstation validation" });
    const liveEndpoint = screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" });
    expect(paperEndpoint).toBeChecked();
    expect(liveEndpoint).not.toBeChecked();
    expect(paperEndpoint).toHaveAccessibleDescription(/Paper endpoint for workstation validation.*Paper endpoint selected/s);
    expect(liveEndpoint).toHaveAccessibleDescription(/Live endpoint for production brokerage verification.*Paper endpoint selected/s);
    expect(screen.getByText("Enter Alpaca credentials")).toBeInTheDocument();
    expect(screen.getAllByText("Key ID").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Needed").length).toBeGreaterThan(0);
    expect(screen.getByText("********1234")).toBeInTheDocument();
    expect(screen.getByText("PA123")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Alpaca provider setup checklist" })).toBeInTheDocument();
    expect(screen.getByText("Move from demo data to a verified paper connection before relying on readiness evidence.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Trading readiness after Alpaca account verification" })).toHaveAttribute(
      "href",
      "/trading/readiness"
    );
    expect(screen.getByRole("button", { name: /connect and test/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /clear/i })).toBeEnabled();
    expect(screen.getByLabelText(/Key ID/)).toHaveAccessibleDescription(/Stored values remain masked after refresh\..*Enter Alpaca credentials/s);
    expect(screen.getByLabelText(/Secret key/)).toHaveAccessibleDescription(/Secret key is never displayed after submit\..*Enter Alpaca credentials/s);
    expect(screen.getByRole("button", { name: /connect and test/i })).toHaveAttribute(
      "title",
      "Enter an Alpaca key ID before testing the connection."
    );
  });

  it("blocks live Alpaca credential testing until the live endpoint is acknowledged", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.click(screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" }));
    await user.type(screen.getByPlaceholderText("ALPACA_KEY_ID"), "AK-LIVE");
    await user.type(screen.getByPlaceholderText("ALPACA_SECRET_KEY"), "secret");

    const submit = screen.getByRole("button", { name: /connect and test/i });
    expect(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" })).not.toBeChecked();
    expect(screen.getByText("Live endpoint review required")).toBeInTheDocument();
    expect(submit).toBeDisabled();
    expect(submit).toHaveAttribute(
      "title",
      "Acknowledge the live Alpaca endpoint before testing live credentials."
    );

    await user.click(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" }));

    expect(submit).toBeEnabled();
    expect(screen.getByText("Credentials ready for test")).toBeInTheDocument();
  });

  it("explains disabled Alpaca form controls while a credential request is running", async () => {
    const user = userEvent.setup();
    const busyReason = "Alpaca credential request is already running.";
    let resolveConnect: (status: BrokerageConnectionStatus) => void = () => undefined;
    apiMocks.connectAlpacaConnection.mockImplementationOnce(() => new Promise<BrokerageConnectionStatus>((resolve) => {
      resolveConnect = resolve;
    }));
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.click(screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" }));
    await user.type(screen.getByPlaceholderText("ALPACA_KEY_ID"), "AK-LIVE");
    await user.type(screen.getByPlaceholderText("ALPACA_SECRET_KEY"), "secret");
    await user.click(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" }));
    await user.click(screen.getByRole("button", { name: /connect and test/i }));

    expect(screen.getByLabelText(/Key ID/)).toBeDisabled();
    expect(screen.getByLabelText(/Key ID/)).toHaveAccessibleDescription(new RegExp(`${busyReason}.*Testing Alpaca credentials`, "s"));
    expect(screen.getByLabelText(/Secret key/)).toBeDisabled();
    expect(screen.getByLabelText(/Secret key/)).toHaveAccessibleDescription(new RegExp(`${busyReason}.*Testing Alpaca credentials`, "s"));
    expect(screen.getByRole("radio", { name: "Use Alpaca paper endpoint for workstation validation" })).toHaveAttribute(
      "aria-describedby",
      expect.stringContaining("alpaca-environment-disabled-reason")
    );
    expect(screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" })).toBeDisabled();
    expect(screen.getAllByText(busyReason).length).toBeGreaterThanOrEqual(4);
    expect(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" })).toHaveAccessibleDescription(
      new RegExp(`${busyReason}.*Testing Alpaca credentials`, "s")
    );

    resolveConnect({ ...alpacaConnection, environment: "live" });
    expect(await screen.findAllByText("Alpaca account verified.")).toHaveLength(2);
  });

  it("requires confirmation before clearing stored Alpaca credentials", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.click(screen.getByRole("button", { name: /clear/i }));

    expect(apiMocks.revokeAlpacaConnection).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: /confirm clear/i })).toBeEnabled();
    expect(screen.getByText("Confirm Alpaca credential clear")).toBeInTheDocument();
    expect(screen.getByText(/remove the stored Alpaca key reference/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /confirm clear/i }));

    expect(apiMocks.revokeAlpacaConnection).toHaveBeenCalledTimes(1);
  });

  it("renders structured Alpaca clear failure details", async () => {
    const user = userEvent.setup();
    apiMocks.revokeAlpacaConnection.mockRejectedValueOnce(
      new ApiError({
        path: "/api/brokerage-connections/alpaca",
        status: 409,
        detail: "Credential revocation is blocked.",
        validationIssues: [
          {
            field: "providerState",
            label: "providerState",
            messages: ["Provider still has an active verification job."]
          }
        ]
      })
    );

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.click(screen.getByRole("button", { name: /clear/i }));
    await user.click(screen.getByRole("button", { name: /confirm clear/i }));

    const setupPanel = document.querySelector("#alpaca-provider-setup");
    expect(setupPanel).not.toBeNull();
    expect(await within(setupPanel as HTMLElement).findByText("Endpoint returned 409 for /api/brokerage-connections/alpaca.")).toBeInTheDocument();
    expect(within(setupPanel as HTMLElement).getAllByText("Credential revocation is blocked.").length).toBeGreaterThan(0);
    expect(within(setupPanel as HTMLElement).getByText("providerState: Provider still has an active verification job.")).toBeInTheDocument();
  });

  it("renders structured Alpaca validation details when credential verification fails", async () => {
    const user = userEvent.setup();
    apiMocks.connectAlpacaConnection.mockRejectedValueOnce(
      new ApiError({
        path: "/api/brokerage-connections/alpaca/connect",
        status: 422,
        detail: "One or more validation errors occurred.",
        validationIssues: [
          {
            field: "secretKey",
            label: "secretKey",
            messages: ["Secret key must include the paper account scope."]
          }
        ]
      })
    );

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.type(screen.getByPlaceholderText("ALPACA_KEY_ID"), "AK-PAPER");
    await user.type(screen.getByPlaceholderText("ALPACA_SECRET_KEY"), "secret");
    await user.click(screen.getByRole("button", { name: /connect and test/i }));

    const setupPanel = document.querySelector("#alpaca-provider-setup");
    expect(setupPanel).not.toBeNull();
    expect(await within(setupPanel as HTMLElement).findByText("Endpoint returned 422 for /api/brokerage-connections/alpaca/connect.")).toBeInTheDocument();
    expect(within(setupPanel as HTMLElement).getAllByText("One or more validation errors occurred.").length).toBeGreaterThan(0);
    expect(within(setupPanel as HTMLElement).getByText("secretKey: Secret key must include the paper account scope.")).toBeInTheDocument();
  });

  it("renders backend capability groups with mapped API links", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        research={{ metrics: [], runs: [] }}
        trading={{} as never}
        portfolio={portfolio}
        dataOperations={{ metrics: [], providers: [], backfills: [], exports: [] }}
        governance={{} as never}
        reporting={{} as never}
      />
    );

    expect(screen.getByRole("list", { name: "Backend capability coverage by workstation route" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "GET /api/workstation/workflows for Settings Workflow library" })).toHaveAttribute(
      "href",
      "/api/workstation/workflows"
    );
    expect(screen.getByRole("link", { name: "GET /api/workstation/runs/history for Strategy Run history" })).toHaveAttribute(
      "href",
      "/api/workstation/runs/history"
    );
    expect(screen.queryByRole("link", { name: "POST /api/workstation/reconciliation/runs for Accounting Run reconciliation" })).toBeNull();
    expect(screen.getByRole("group", {
      name: "Reference-only POST /api/workstation/reconciliation/runs for Accounting Run reconciliation"
    })).toHaveTextContent(
      "Reference"
    );
  });

  it("renders diagnostic endpoint failures as accessible endpoint cards", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        research={null}
        trading={null}
        dataOperations={null}
        governance={null}
        error="Workstation request failed."
        workspaceErrors={{ trading: "Trading API returned 503." }}
      />
    );

    const tradingLink = screen.getByRole("link", { name: "Open Trading workspace diagnostic endpoint" });

    expect(tradingLink).toHaveAttribute("href", "/api/workstation/trading");
    expect(within(tradingLink).getByText("Failed")).toBeInTheDocument();
    expect(within(tradingLink).getByText("Trading API returned 503.")).toBeInTheDocument();
  });
});

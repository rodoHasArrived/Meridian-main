import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import * as api from "@/lib/api";
import * as plaidLink from "@/lib/plaid-link";
import { DataScreen } from "@/screens/data-screen";
import {
  DATA_BACKFILL_DETAIL_PANEL_ID,
  DATA_EXPORT_DETAIL_PANEL_ID,
  DATA_PROVIDER_DETAIL_PANEL_ID
} from "@/screens/data-screen.view-model";
import { renderWithRouter } from "@/test/render";
import type {
  BackfillProgressResponse,
  BackfillPreviewResult,
  BackfillTriggerResult,
  DataWorkspaceResponse,
  ProviderConnectionRow,
  ProviderCredentialVerificationResult,
  ProviderReadinessSummary,
  ProviderSetupResult
} from "@/types";

const data: DataWorkspaceResponse = {
  metrics: [
    { id: "m1", label: "Providers Healthy", value: "4", delta: "0", tone: "success" },
    { id: "m2", label: "Backfills Running", value: "2", delta: "+1", tone: "default" },
    { id: "m3", label: "Exports Ready", value: "3", delta: "+1", tone: "success" },
    { id: "m4", label: "Needs Review", value: "1", delta: "+1", tone: "warning" }
  ],
  providers: [
    {
      providerId: "polygon",
      displayName: "Polygon.io",
      provider: "Polygon",
      status: "Healthy",
      capability: "Streaming equities",
      latency: "18ms p50",
      note: "Realtime subscriptions are stable.",
      trustScore: "98%",
      signalSource: "Provider heartbeat",
      reasonCode: "TRUST_OK",
      recommendedAction: "Keep provider active.",
      gateImpact: "No gate impact"
    }
  ],
  backfills: [
    {
      jobId: "BF-1042",
      scope: "US equities / 30d",
      provider: "Databento",
      status: "Running",
      progress: "62%",
      updatedAt: "2m ago"
    },
    {
      jobId: "BF-1044",
      scope: "Options chains / 7d",
      provider: "Databento",
      status: "Review",
      progress: "95%",
      updatedAt: "5m ago"
    }
  ],
  exports: [
    {
      exportId: "EX-2201",
      profile: "python-pandas",
      target: "strategy pack",
      status: "Ready",
      rows: "124k",
      updatedAt: "4m ago"
    }
  ]
};

const polygonConnection: ProviderConnectionRow = {
  providerId: "polygon",
  displayName: "Polygon.io",
  capability: "Data",
  credentialState: "Verified",
  credentialSource: "LocalEncryptedStore",
  verificationState: "Verified",
  health: "Healthy",
  fallbackActive: false,
  lastVerifiedAt: "2026-05-20T18:20:00Z",
  lastSuccessfulAt: "2026-05-20T18:25:00Z",
  lastFailureAt: null,
  lastError: null,
  maskedKeyPreview: "pk_live_****7F3A",
  environment: "paper",
  externalAccountId: null,
  affectedWorkflows: ["Strategy", "Backfill"],
  recommendedAction: "Keep provider active.",
  actionHref: "/settings#provider-polygon"
};

const successfulVerification: ProviderCredentialVerificationResult = {
  providerId: "polygon",
  success: true,
  verificationState: "Verified",
  health: "Healthy",
  lastVerifiedAt: "2026-05-20T18:30:00Z",
  lastError: null,
  externalAccountId: "acct-provider-01",
  warnings: []
};

const providerReadiness: ProviderReadinessSummary = {
  asOf: "2026-06-02T16:50:00Z",
  status: "Blocked",
  totalProviders: 2,
  readyProviders: 1,
  reviewProviders: 0,
  degradedProviders: 0,
  blockedProviders: 1,
  summary: "1 provider blocks dependent workflows.",
  recommendedAction: "Repair Plaid credentials before routing accounting evidence workflows.",
  providers: [
    {
      providerId: "plaid",
      displayName: "Plaid",
      capability: "Data",
      status: "Blocked",
      credentialState: "Missing",
      credentialSource: "None",
      verificationState: "NotVerified",
      connectionHealth: "Warning",
      isEnabled: true,
      isConnected: false,
      fallbackActive: false,
      degradationScore: null,
      lastVerifiedAt: null,
      lastSuccessfulAt: null,
      lastFailureAt: null,
      lastError: "Sandbox client credentials have not been configured.",
      maskedKeyPreview: null,
      environment: "sandbox",
      externalAccountId: null,
      affectedWorkflows: ["Accounting evidence", "Brokerage sync"],
      recommendedAction: "Connect Plaid sandbox credentials before bank-account evidence can be retained.",
      actionHref: "/data/providers",
      evidence: [
        {
          kind: "Credential",
          label: "Credential",
          status: "Blocked",
          detail: "Required Plaid client fields are missing."
        }
      ],
      recoveryActions: [
        {
          actionId: "provider.plaid.open-setup",
          label: "Open setup",
          target: "/data/providers",
          requiresMutation: false
        }
      ],
      credentialFields: [
        {
          name: "ClientId",
          label: "Client ID",
          required: true,
          inputKind: "Password",
          placeholder: "PLAID_CLIENT_ID",
          helpText: "Used server-side to create Plaid link tokens and retain bank evidence."
        },
        {
          name: "Secret",
          label: "Secret",
          required: true,
          inputKind: "Password",
          placeholder: "PLAID_SECRET",
          helpText: "Used server-side to create Plaid link tokens and retain bank evidence."
        }
      ],
      environmentOptions: [
        { value: "sandbox", label: "Sandbox", isDefault: true },
        { value: "development", label: "Development", isDefault: false },
        { value: "production", label: "Production", isDefault: false }
      ]
    },
    {
      providerId: "polygon",
      displayName: "Polygon.io",
      capability: "Data",
      status: "Ready",
      credentialState: "Verified",
      credentialSource: "LocalEncryptedStore",
      verificationState: "Verified",
      connectionHealth: "Healthy",
      isEnabled: true,
      isConnected: true,
      fallbackActive: false,
      degradationScore: 0.08,
      lastVerifiedAt: "2026-06-02T16:40:00Z",
      lastSuccessfulAt: "2026-06-02T16:45:00Z",
      lastFailureAt: null,
      lastError: null,
      maskedKeyPreview: "pk_live_****7F3A",
      environment: "paper",
      externalAccountId: null,
      affectedWorkflows: ["Import", "Validate", "Backfill"],
      recommendedAction: "No provider readiness action required.",
      actionHref: "/settings#provider-polygon",
      evidence: [],
      recoveryActions: [
        {
          actionId: "provider.polygon.verify",
          label: "Verify credentials",
          target: "/api/providers/polygon/verify",
          requiresMutation: true
        }
      ],
      credentialFields: [
        {
          name: "ApiKey",
          label: "API key",
          required: true,
          inputKind: "Password",
          placeholder: "POLYGON_API_KEY",
          helpText: "Stored in Meridian's encrypted local provider store and masked after save."
        }
      ],
      environmentOptions: []
    }
  ]
};

describe("DataScreen", () => {
  it("renders an accessible route-aware loading panel when bootstrap data is unavailable", () => {
    renderWithRouter(<DataScreen data={null} />, { initialEntries: ["/data/backfills"] });

    const loading = screen.getByRole("status", { name: "Data backfill loading state" });
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(screen.getByText("Loading backfill queue")).toBeInTheDocument();
    expect(screen.getByText("Workspace data pending")).toBeInTheDocument();
    expect(screen.getByText("Backfills")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open alpaca paper provider setup/i }))
      .toHaveAttribute("href", "/settings#alpaca-provider-setup");
    expect(screen.getByRole("link", { name: /open live quotes while data workspace loads/i })).toHaveAttribute("href", "/data/quotes");
  });

  it("renders provider, backfill, and export summaries", () => {
    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    expect(screen.getByText("Data command deck")).toBeInTheDocument();
    expect(screen.getByText("Upload data template")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Data upload template" })).toHaveValue("trade-data");
    expect(screen.getByRole("link", { name: "Download Trade data CSV template" }))
      .toHaveAttribute("download", "meridian-trade-data-template.csv");
    expect(screen.getByText("Provider posture")).toBeInTheDocument();
    expect(screen.getByText("Backfill repair")).toBeInTheDocument();
    expect(screen.getByText("Upload intake")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Data workspace route focus" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Security Master in Accounting" }))
      .toHaveAttribute("href", "/accounting/security-master");
    expect(screen.getAllByText("Provider health").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Backfill queue").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Recent exports").length).toBeGreaterThan(0);
    expect(screen.getByRole("table", { name: "Provider health" })).toBeInTheDocument();
    const providerRow = screen.getByRole("row", { name: "Inspect provider Polygon.io" });
    expect(providerRow).toHaveAttribute("aria-selected", "true");
    expect(providerRow).toHaveAttribute("aria-expanded", "true");
    expect(providerRow).toHaveAttribute("aria-controls", DATA_PROVIDER_DETAIL_PANEL_ID);
    expect(providerRow).toHaveClass("bg-success/5");
    const providerDetail = screen.getByRole("region", { name: /provider detail for Polygon\.io/i });
    expect(providerDetail).toBeInTheDocument();
    expect(within(providerDetail).getByText("Trust score")).toBeInTheDocument();
    expect(within(providerDetail).getByText("98%")).toBeInTheDocument();
    expect(within(providerDetail).getByText("Keep provider active.")).toBeInTheDocument();
    expect(within(providerDetail).getByText("Reason: TRUST_OK")).toBeInTheDocument();
    expect(within(providerDetail).getByText("Gate: No gate impact")).toBeInTheDocument();
    const runningBackfill = screen.getByRole("row", { name: "Inspect backfill BF-1042" });
    expect(runningBackfill).toHaveAttribute("aria-controls", DATA_BACKFILL_DETAIL_PANEL_ID);
    expect(runningBackfill).toHaveClass("bg-paper/5");
    const backfillDetail = screen.getByRole("region", { name: /backfill detail for BF-1042/i });
    expect(backfillDetail).toHaveAttribute("id", DATA_BACKFILL_DETAIL_PANEL_ID);
    expect(within(backfillDetail).getByText("US equities / 30d")).toBeInTheDocument();
    expect(within(backfillDetail).getByText(/Replay is currently advancing/i)).toBeInTheDocument();
    const exportTable = screen.getByRole("table", { name: "Recent exports" });
    expect(exportTable).toBeInTheDocument();
    const exportRow = screen.getByRole("row", { name: "Inspect export EX-2201" });
    expect(exportRow).toHaveAttribute("aria-selected", "true");
    expect(exportRow).toHaveAttribute("aria-expanded", "true");
    expect(exportRow).toHaveAttribute("aria-controls", DATA_EXPORT_DETAIL_PANEL_ID);
    expect(exportRow).toHaveClass("bg-success/5");
    const exportDetail = screen.getByRole("region", { name: /export detail for EX-2201/i });
    expect(exportDetail).toHaveAttribute("id", DATA_EXPORT_DETAIL_PANEL_ID);
    expect(within(exportDetail).getByText("python-pandas")).toBeInTheDocument();
    expect(within(exportDetail).getByText("strategy pack")).toBeInTheDocument();
    expect(within(exportDetail).queryByText("research pack")).not.toBeInTheDocument();
    expect(within(exportDetail).getByText("Next action")).toBeInTheDocument();
    expect(within(exportDetail).getByText("Attach export to the report pack or hand off the package.")).toBeInTheDocument();
  });

  it("previews a data upload file through the shared upload endpoint", async () => {
    const user = userEvent.setup();
    const previewDataUpload = vi.spyOn(api, "previewDataUpload").mockResolvedValueOnce({
      uploadId: "UP-1",
      templateId: "trade-data",
      templateLabel: "Trade data",
      fileName: "trades.csv",
      fileSizeBytes: 96,
      contentType: "text/csv",
      uploadedBy: "ops-user",
      uploadedAtUtc: "2026-06-08T12:00:00Z",
      retainedPath: "workstation/data-uploads/UP-1/trades.csv",
      parsedRowCount: 1,
      previewRowCount: 1,
      headers: ["trade_id", "trade_date", "account_code", "symbol"],
      previewRows: [{ trade_id: "TRD-1", trade_date: "2026-06-01", account_code: "FUND-A", symbol: "AAPL" }],
      issues: [],
      status: "ReadyForReview",
      nextAction: "Review the preview rows, then route this retained source file into validation and reconciliation."
    });
    const file = new File([
      "trade_id,trade_date,account_code,symbol\nTRD-1,2026-06-01,FUND-A,AAPL\n"
    ], "trades.csv", { type: "text/csv" });

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    await user.upload(screen.getByLabelText("Upload Trade data CSV file for preview"), file);

    await waitFor(() => expect(previewDataUpload).toHaveBeenCalledWith(
      { templateId: "trade-data", file },
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    ));
    expect(await screen.findByText(/1 rows parsed from trades\.csv/i)).toBeInTheDocument();
    expect(screen.getByText("workstation/data-uploads/UP-1/trades.csv")).toBeInTheDocument();
    expect(screen.getByText("SFTP file drop")).toBeInTheDocument();
    expect(screen.getByText(/pinned host-key fingerprint/i)).toBeInTheDocument();
    expect(screen.getByText("4 of 7 required mapping fields matched in trades.csv.")).toBeInTheDocument();
    expect(screen.getAllByText("AAPL").length).toBeGreaterThanOrEqual(1);

    previewDataUpload.mockRestore();
  });

  it("renders shared provider readiness and the next action for each provider", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <DataScreen data={data} providerConnections={[polygonConnection]} providerReadiness={providerReadiness} />,
      { initialEntries: ["/data/providers"] }
    );

    expect(screen.getByText(/1 provider blocks dependent workflows/i)).toBeInTheDocument();
    expect(screen.getByText(/Next action: Repair Plaid credentials/i)).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Provider health" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Next Action" })).toBeInTheDocument();
    const plaidRow = screen.getByRole("row", { name: /Inspect provider Plaid/i });
    expect(plaidRow).toHaveClass("bg-danger/5");
    expect(within(plaidRow).getByText("Blocked")).toBeInTheDocument();
    expect(within(plaidRow).getByText("Open setup")).toBeInTheDocument();
    await user.click(screen.getByRole("tab", { name: "Credentials" }));
    const plaidDetail = screen.getByRole("region", { name: /provider detail for Plaid/i });
    expect(plaidDetail).toHaveTextContent("Client ID");
    expect(plaidDetail).toHaveTextContent("Required field");
    expect(plaidDetail).toHaveTextContent("Secret");
    expect(plaidDetail).toHaveTextContent("Sandbox, Development, Production");
    await user.click(screen.getByRole("tab", { name: "Diagnostics" }));
    expect(screen.getByRole("region", { name: /provider detail for Plaid/i }))
      .toHaveTextContent("Required Plaid client fields are missing.");
  });

  it("renders explicit empty guidance when provider, backfill, and export arrays are empty", () => {
    const emptyData: DataWorkspaceResponse = {
      metrics: [],
      providers: [],
      backfills: [],
      exports: []
    };

    renderWithRouter(<DataScreen data={emptyData} />, { initialEntries: ["/data/backfills"] });

    expect(screen.getByText("No providers configured")).toBeInTheDocument();
    expect(screen.getByText("No backfills queued")).toBeInTheDocument();
    expect(screen.getByText("No exports available")).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "Backfill detail empty state" })).toHaveTextContent("No backfill activity yet");
    expect(screen.getAllByRole("status").length).toBeGreaterThanOrEqual(3);
  });

  it("supports keyboard provider selection and updates the provider detail panel", async () => {
    const user = userEvent.setup();
    const providerData: DataWorkspaceResponse = {
      ...data,
      providers: [
        ...data.providers,
        {
          providerId: "databento",
          displayName: "Databento",
          provider: "Databento",
          status: "Degraded",
          capability: "Historical futures",
          latency: "2.4s p95",
          note: "Backfill pressure is elevated.",
          trustScore: "72%",
          signalSource: "Replay audit",
          reasonCode: "LATENCY_ELEVATED",
          recommendedAction: "Route fresh requests to Polygon until replay clears.",
          gateImpact: "Blocks promotion gates"
        }
      ]
    };

    renderWithRouter(<DataScreen data={providerData} />, { initialEntries: ["/data"] });

    const polygonProvider = screen.getByRole("row", { name: "Inspect provider Polygon.io" });
    const databentoProvider = screen.getByRole("row", { name: "Inspect provider Databento" });

    expect(polygonProvider).toHaveAttribute("aria-selected", "true");
    expect(databentoProvider).toHaveAttribute("aria-expanded", "false");
    expect(databentoProvider).toHaveClass("bg-danger/5");

    databentoProvider.focus();
    await user.keyboard("{Enter}");

    expect(databentoProvider).toHaveAttribute("aria-selected", "true");
    expect(databentoProvider).toHaveAttribute("aria-expanded", "true");
    expect(polygonProvider).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("region", { name: /provider detail for Databento/i })).toHaveTextContent("Route fresh requests");
    expect(screen.getByRole("region", { name: /provider detail for Databento/i })).toHaveTextContent("Reason: LATENCY_ELEVATED");
  });

  it("runs provider verification from the selected provider diagnostics tab", async () => {
    const user = userEvent.setup();
    const verifyProviderConnection = vi.spyOn(api, "verifyProviderConnection").mockResolvedValueOnce(successfulVerification);
    const refreshProviderRouting = vi.fn();

    renderWithRouter(
      <DataScreen
        data={data}
        providerConnections={[polygonConnection]}
        onProviderRoutingRefresh={refreshProviderRouting}
      />,
      { initialEntries: ["/data"] }
    );

    expect(screen.getByRole("combobox", { name: "Configured Provider" })).toHaveValue("provider-row-polygon");

    await user.click(screen.getByRole("tab", { name: "Diagnostics" }));
    await user.click(screen.getByRole("button", { name: /run provider credential verification/i }));

    await waitFor(() => expect(verifyProviderConnection).toHaveBeenCalledWith("polygon"));
    await waitFor(() => expect(refreshProviderRouting).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("Verification passed.")).toBeInTheDocument();
    expect(screen.getByText("External account: acct-provider-01")).toBeInTheDocument();

    verifyProviderConnection.mockRestore();
  });

  it("renders a diagnostics empty state when provider evidence has not loaded", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("tab", { name: "Diagnostics" }));

    expect(screen.getByRole("status", { name: "Polygon.io diagnostics empty state" }))
      .toHaveTextContent("Diagnostics not loaded yet");
    expect(screen.getByRole("button", { name: "Provider verification unavailable for Polygon.io" })).toBeDisabled();
  });

  it("clears provider credentials after setup and suppresses browser autocomplete", async () => {
    const user = userEvent.setup();

    vi.spyOn(api, "setupProvider").mockResolvedValueOnce({
      success: true,
      providerId: "provider-alpaca",
      providerName: "Alpaca paper",
      message: "Provider configured.",
      error: null,
      connectionId: "provider-alpaca",
      bindingIds: ["provider-alpaca-RealtimeMarketData", "provider-alpaca-HistoricalBars"],
      credentialState: "Configured",
      credentialSource: "ExternalVaultReference",
      credentialReference: "vault:alpaca/paper",
      environment: "paper",
      warnings: ["Credential verification still needs to run."]
    });
    const refreshProviderRouting = vi.fn();

    renderWithRouter(
      <DataScreen data={data} onProviderSetupConfigured={refreshProviderRouting} />,
      { initialEntries: ["/data"] }
    );

    await user.click(screen.getByRole("button", { name: /configure a new data provider/i }));

    expect(screen.getByRole("region", { name: "Yahoo Finance setup summary" })).toHaveTextContent("No key needed");
    expect(screen.getByRole("region", { name: "Yahoo Finance setup summary" })).toHaveTextContent("Preview a historical backfill");
    expect(screen.getByRole("region", { name: "Data integration workflow" }))
      .toHaveTextContent("Connect Source");
    expect(screen.getByRole("region", { name: "Data integration workflow" }))
      .toHaveTextContent("Publish Data");

    await user.selectOptions(screen.getByLabelText("Select provider type"), "alpaca");

    const apiKey = screen.getByLabelText("Provider API key");
    const apiSecret = screen.getByLabelText("Provider API secret");
    expect(apiKey).toHaveAttribute("autocomplete", "new-password");
    expect(apiSecret).toHaveAttribute("autocomplete", "new-password");

    await user.type(apiKey, "key-123");
    await user.type(apiSecret, "secret-456");
    await user.click(screen.getByRole("button", { name: /configure and register provider/i }));

    await waitFor(() => expect(api.setupProvider).toHaveBeenCalledWith(expect.objectContaining({
      kind: "alpaca",
      apiKey: "key-123",
      apiSecret: "secret-456"
    })));

    expect(await screen.findByText("Alpaca paper configured")).toBeInTheDocument();
    const posture = screen.getByRole("region", { name: "Provider setup routing and credential status" });
    expect(posture).toHaveTextContent("provider-alpaca");
    expect(posture).toHaveTextContent("provider-alpaca-RealtimeMarketData");
    expect(posture).toHaveTextContent("Configured");
    expect(posture).toHaveTextContent("External vault reference");
    expect(posture).toHaveTextContent("PAPER");
    expect(screen.getByRole("status", { name: "Provider setup warnings" }))
      .toHaveTextContent("Credential verification still needs to run.");
    expect(screen.getByRole("region", { name: "Data integration workflow" }))
      .toHaveTextContent("Connected");
    expect(screen.getByRole("region", { name: "Data integration workflow" }))
      .toHaveTextContent("Acquire Data");
    expect(screen.getByRole("region", { name: "Data integration workflow" }))
      .toHaveTextContent("Next");
    expect(screen.queryByText("key-123")).not.toBeInTheDocument();
    expect(screen.queryByText("secret-456")).not.toBeInTheDocument();
    expect(screen.queryByText("vault:alpaca/paper")).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Provider setup next validation" }))
      .toHaveTextContent("Next validation");
    expect(screen.getByRole("link", { name: "Validate live quotes after configuring Alpaca" }))
      .toHaveAttribute("href", "/data/quotes?symbol=AAPL");
    expect(screen.getByRole("link", { name: "Preview a historical backfill after configuring Alpaca" }))
      .toHaveAttribute("href", "/data/backfills");
    expect(screen.getByRole("link", { name: "Check Trading readiness after configuring Alpaca" }))
      .toHaveAttribute("href", "/trading/readiness");
    await waitFor(() => expect(refreshProviderRouting).toHaveBeenCalledTimes(1));

    await user.click(screen.getByRole("button", { name: "Configure another" }));

    expect(screen.getByLabelText("Provider API key")).toHaveValue("");
    expect(screen.getByLabelText("Provider API secret")).toHaveValue("");
  });

  it("submits Plaid setup with client credentials and account-evidence capabilities", async () => {
    const user = userEvent.setup();
    const setupProvider = vi.spyOn(api, "setupProvider").mockResolvedValueOnce({
      success: true,
      providerId: "plaid",
      providerName: "Plaid",
      message: "Plaid credentials were configured.",
      error: null,
      connectionId: null,
      bindingIds: [],
      credentialState: "Configured",
      credentialSource: "LocalEncryptedStore",
      credentialReference: "vault:plaid/sandbox",
      environment: "sandbox",
      warnings: []
    });

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data/providers"] });

    await user.click(screen.getByRole("button", { name: /configure a new data provider/i }));
    await user.selectOptions(screen.getByLabelText("Select provider type"), "plaid");

    const clientId = screen.getByLabelText("Plaid client ID");
    const secret = screen.getByLabelText("Plaid secret");
    expect(screen.getByRole("region", { name: "Plaid setup summary" }))
      .toHaveTextContent("Bank account evidence, Identity verification, Investment holdings");

    await user.type(clientId, "client-sandbox-123");
    await user.type(secret, "secret-sandbox-456");
    await user.click(screen.getByRole("button", { name: /configure and register provider/i }));

    await waitFor(() => expect(setupProvider).toHaveBeenCalledWith(expect.objectContaining({
      kind: "plaid",
      displayName: "Plaid",
      apiKey: "client-sandbox-123",
      apiSecret: "secret-sandbox-456",
      capabilities: ["banking", "identity", "investments"],
      environment: "sandbox"
    })));

    expect(await screen.findByText("Plaid configured")).toBeInTheDocument();
    const posture = screen.getByRole("region", { name: "Provider setup routing and credential status" });
    expect(posture).toHaveTextContent("No routing binding returned");
    expect(posture).toHaveTextContent("Local encrypted store");
    expect(screen.queryByText("client-sandbox-123")).not.toBeInTheDocument();
    expect(screen.queryByText("secret-sandbox-456")).not.toBeInTheDocument();
    expect(screen.queryByText("vault:plaid/sandbox")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Link account evidence after configuring Plaid" }))
      .toHaveAttribute("href", "/data/providers");

    setupProvider.mockRestore();
  });

  it("searches Plaid-supported institutions from the bank connection setup flow", async () => {
    const user = userEvent.setup();
    const searchPlaidInstitutions = vi.spyOn(api, "searchPlaidInstitutions").mockResolvedValueOnce({
      query: "Chase",
      requestId: "req-plaid-institutions",
      institutions: [
        {
          institutionId: "ins_3",
          name: "Chase",
          products: ["Transactions", "Auth", "Identity"],
          countryCodes: ["US"],
          url: "https://www.chase.com",
          primaryColor: "#117ACA",
          logo: null
        }
      ]
    });
    const createPlaidLinkToken = vi.spyOn(api, "createPlaidLinkToken").mockResolvedValueOnce({
      linkToken: "link-sandbox-1234567890abcdef",
      expiration: "2026-06-01T18:00:00Z",
      requestId: "req-link-token",
      products: ["Transactions", "Auth", "Identity", "Investments"],
      institutionId: "ins_3",
      institutionName: "Chase",
      environment: "Sandbox"
    });
    const openPlaidLink = vi.spyOn(plaidLink, "openPlaidLink").mockResolvedValueOnce({
      publicToken: "public-sandbox-token",
      metadata: {
        institution: {
          institution_id: "ins_3",
          name: "Chase"
        },
        accounts: [
          {
            id: "plaid-account-1",
            name: "Plaid Checking",
            official_name: "Plaid Gold Checking",
            mask: "0000",
            type: "depository",
            subtype: "checking"
          }
        ],
        link_session_id: "link-session-1"
      }
    });
    const exchangePlaidPublicToken = vi.spyOn(api, "exchangePlaidPublicToken").mockResolvedValueOnce({
      item: {
        itemId: "item-1",
        institutionId: "ins_3",
        institutionName: "Chase",
        status: "Linked",
        linkedAt: "2026-06-01T18:00:00Z"
      },
      accounts: [
        {
          plaidAccountId: "plaid-account-1",
          name: "Plaid Checking",
          mask: "0000",
          type: "depository",
          subtype: "checking"
        }
      ],
      requestId: "req-exchange"
    });

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data/providers"] });

    await user.click(screen.getByRole("button", { name: /configure a new data provider/i }));
    await user.selectOptions(screen.getByLabelText("Select provider type"), "plaid");
    await user.type(screen.getByLabelText("Plaid client ID"), "client-sandbox-123");
    await user.type(screen.getByLabelText("Plaid secret"), "secret-sandbox-456");

    const bankSearch = screen.getByRole("combobox", { name: "Search supported financial institutions" });
    await user.type(bankSearch, "Chase");
    await user.click(screen.getByRole("button", { name: "Search supported financial institutions for Chase" }));

    await waitFor(() => expect(searchPlaidInstitutions).toHaveBeenCalledWith("Chase"));
    expect(await screen.findByRole("option", { name: /Chase/ })).toHaveTextContent("ins_3 | US | Transactions, Auth, Identity");

    await user.click(screen.getByRole("option", { name: /Chase/ }));

    expect(screen.getByText("Chase is selected for the next bank connection step.")).toBeInTheDocument();
    expect(bankSearch).toHaveValue("Chase");
    expect(screen.getByRole("region", { name: "Plaid sandbox bank connection" }))
      .toHaveTextContent("user_good");
    expect(screen.getByRole("region", { name: "Plaid sandbox bank connection" }))
      .toHaveTextContent("pass_good");

    await user.click(screen.getByRole("button", { name: "Open secure bank connection for Chase" }));

    await waitFor(() => expect(createPlaidLinkToken).toHaveBeenCalledWith(expect.objectContaining({
      userId: "operator",
      institutionId: "ins_3",
      institutionName: "Chase",
      products: ["Transactions", "Auth", "Identity", "Investments"],
      countryCodes: ["US"]
    })));
    await waitFor(() => expect(openPlaidLink).toHaveBeenCalledWith("link-sandbox-1234567890abcdef"));
    await waitFor(() => expect(exchangePlaidPublicToken).toHaveBeenCalledWith(expect.objectContaining({
      publicToken: "public-sandbox-token",
      institutionId: "ins_3",
      institutionName: "Chase",
      requestedBy: "operator",
      accounts: [
        expect.objectContaining({
          plaidAccountId: "plaid-account-1",
          name: "Plaid Checking",
          officialName: "Plaid Gold Checking",
          mask: "0000",
          type: "depository",
          subtype: "checking"
        })
      ]
    })));
    expect(await screen.findByText(/link-sandbox/)).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Plaid sandbox bank connection" }))
      .toHaveTextContent("Chase account evidence was linked and stored by Meridian.");
    expect(screen.getByRole("status", { name: "Plaid linked account evidence" }))
      .toHaveTextContent("item-1");
    expect(screen.getByRole("status", { name: "Plaid linked account evidence" }))
      .toHaveTextContent("Plaid Checking");
    expect(screen.getByRole("status", { name: "Plaid linked account evidence" }))
      .toHaveTextContent("mask 0000 | depository | checking");

    searchPlaidInstitutions.mockRestore();
    createPlaidLinkToken.mockRestore();
    openPlaidLink.mockRestore();
    exchangePlaidPublicToken.mockRestore();
  });

  it("explains why provider setup cannot be cancelled while save is pending", async () => {
    const user = userEvent.setup();
    let resolveSetup!: (value: ProviderSetupResult) => void;

    vi.spyOn(api, "setupProvider").mockReturnValueOnce(new Promise<ProviderSetupResult>((resolve) => {
      resolveSetup = resolve;
    }));

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /configure a new data provider/i }));
    await user.selectOptions(screen.getByLabelText("Select provider type"), "alpaca");
    await user.type(screen.getByLabelText("Provider API key"), "key-123");
    await user.type(screen.getByLabelText("Provider API secret"), "secret-456");
    await user.click(screen.getByRole("button", { name: /configure and register provider/i }));

    const cancelButton = await screen.findByRole("button", { name: /cancel provider setup unavailable/i });
    expect(cancelButton).toBeDisabled();
    expect(cancelButton).toHaveAttribute("title", "Provider setup is in progress; wait before closing.");

    await act(async () => {
      resolveSetup({
        success: true,
        providerId: "provider-alpaca",
        providerName: "Alpaca paper",
        message: "Provider configured.",
        error: null
      });
    });
  });

  it("adapts the hero copy for deep-link routes", () => {
    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data/backfills"] });

    expect(screen.getByText("Backfill queue focus")).toBeInTheDocument();
    expect(screen.getByText("Backfill Detail")).toBeInTheDocument();
    expect(screen.getAllByText(/Replay is currently advancing/).length).toBeGreaterThan(0);
  });

  it("keeps the old static Security Master workbench out of the Data route", () => {
    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    expect(screen.queryByRole("textbox", { name: /search securities/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: /show overview/i })).not.toBeInTheDocument();
    expect(screen.queryByText("Security Master command deck")).not.toBeInTheDocument();
  });

  it("switches the detail panel when a backfill row is selected", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data/backfills"] });

    expect(screen.getByRole("table", { name: "Backfill queue" })).toBeInTheDocument();

    const reviewBackfill = screen.getByRole("row", { name: "Inspect backfill BF-1044" });
    const runningBackfill = screen.getByRole("row", { name: "Inspect backfill BF-1042" });

    expect(runningBackfill).toHaveAttribute("aria-selected", "true");
    expect(runningBackfill).toHaveAttribute("aria-expanded", "true");
    expect(runningBackfill).toHaveAttribute("aria-controls", DATA_BACKFILL_DETAIL_PANEL_ID);
    expect(reviewBackfill).toHaveAttribute("aria-controls", DATA_BACKFILL_DETAIL_PANEL_ID);
    expect(reviewBackfill).toHaveAttribute("aria-expanded", "false");
    expect(reviewBackfill).toHaveClass("bg-warning/5");

    await user.click(reviewBackfill);

    expect(reviewBackfill).toHaveAttribute("aria-selected", "true");
    expect(reviewBackfill).toHaveAttribute("aria-expanded", "true");
    expect(runningBackfill).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("region", { name: /backfill detail for BF-1044/i })).toBeInTheDocument();
    expect(screen.getAllByText("Options chains / 7d").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/waiting on operator review/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText("5m ago").length).toBeGreaterThan(0);
  });

  it("supports keyboard selection in the backfill queue table", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data/backfills"] });

    const reviewBackfill = screen.getByRole("row", { name: "Inspect backfill BF-1044" });
    reviewBackfill.focus();

    await user.keyboard("{Enter}");

    expect(reviewBackfill).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("region", { name: /backfill detail for BF-1044/i })).toBeInTheDocument();
  });

  it("switches the export detail panel from keyboard row selection", async () => {
    const exportData: DataWorkspaceResponse = {
      ...data,
      exports: [
        ...data.exports,
        {
          exportId: "EX-2202",
          profile: "report-pack",
          target: "board packet",
          status: "Attention",
          rows: "42k",
          updatedAt: "9m ago"
        }
      ]
    };

    renderWithRouter(<DataScreen data={exportData} />, { initialEntries: ["/data"] });

    const readyExport = screen.getByRole("row", { name: "Inspect export EX-2201" });
    const attentionExport = screen.getByRole("row", { name: "Inspect export EX-2202" });

    expect(readyExport).toHaveAttribute("aria-selected", "true");
    expect(attentionExport).toHaveAttribute("aria-expanded", "false");
    expect(attentionExport).toHaveAttribute("aria-controls", DATA_EXPORT_DETAIL_PANEL_ID);
    expect(attentionExport).toHaveClass("bg-warning/5");

    attentionExport.focus();
    expect(attentionExport).toHaveFocus();
    fireEvent.keyDown(attentionExport, { key: "Enter", code: "Enter" });

    await waitFor(() => expect(attentionExport).toHaveAttribute("aria-selected", "true"));
    expect(attentionExport).toHaveAttribute("aria-expanded", "true");
    await waitFor(() => expect(readyExport).toHaveAttribute("aria-expanded", "false"));
    const detail = screen.getByRole("region", { name: /export detail for EX-2202/i });
    expect(detail).toHaveTextContent("report-pack");
    expect(detail).toHaveTextContent("Review export profile and target before report-pack use.");
  });

  it("opens the trigger backfill dialog when the Trigger backfill button is clicked", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Trigger backfill" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("textbox", { name: "Backfill symbols" })).toHaveFocus());
    expect(screen.getByRole("group", { name: "Backfill request form" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Backfill provider" })).toHaveValue("polygon");
    expect(screen.getByRole("button", { name: /Polygon Configured/i })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText(/Polygon is configured for Streaming equities/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close backfill dialog" })).toBeInTheDocument();
    expect(screen.getByText("Enter at least one symbol before previewing a backfill.")).toBeInTheDocument();
  });

  it("keeps preview disabled until the backfill symbols are valid", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));

    const disabledPreview = screen.getByRole("button", { name: /preview backfill unavailable/i });
    expect(disabledPreview).toBeDisabled();
    expect(disabledPreview).toHaveAttribute("title", "Enter at least one symbol before previewing a backfill.");

    fireEvent.change(screen.getByRole("textbox", { name: "Backfill symbols" }), {
      target: { value: "AAPL" }
    });

    expect(screen.getByRole("button", { name: "Preview backfill request" })).toBeEnabled();
    expect(screen.getByText("Backfill request is ready to preview.")).toBeInTheDocument();
  });

  it("calls previewBackfill and shows preview result", async () => {
    const user = userEvent.setup();

    const mockPreview: BackfillPreviewResult = {
      provider: "polygon",
      providerDisplayName: "Polygon",
      symbols: [{ symbol: "AAPL", estimatedBars: 2100, hasMarketHoursData: true, notes: [] }],
      from: "2024-01-01",
      to: "2024-01-31",
      totalDays: 31,
      estimatedTradingDays: 21,
      estimatedDurationSeconds: 5,
      notes: []
    };

    vi.spyOn(api, "previewBackfill").mockResolvedValueOnce(mockPreview);

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    await user.type(screen.getByRole("textbox", { name: "Backfill symbols" }), "AAPL");
    await user.click(screen.getByRole("button", { name: "Preview backfill request" }));

    await waitFor(() => {
      const previewStatus = screen.getByRole("status", { name: /preview ready — polygon/i });
      expect(previewStatus).toHaveTextContent("Preview only");
      expect(previewStatus).toHaveTextContent("Bars");
      expect(previewStatus).toHaveTextContent("2,100");
      expect(previewStatus).toHaveTextContent("2024-01-01 to 2024-01-31");
    });
    expect(screen.getByText("Preview is ready. Review the summary before running.")).toBeInTheDocument();
  });

  it("locks backfill request fields while preview is pending", async () => {
    const user = userEvent.setup();

    const mockPreview: BackfillPreviewResult = {
      provider: "polygon",
      providerDisplayName: "Polygon",
      symbols: [{ symbol: "AAPL", estimatedBars: 2100, hasMarketHoursData: true, notes: [] }],
      from: "2024-01-01",
      to: "2024-01-31",
      totalDays: 31,
      estimatedTradingDays: 21,
      estimatedDurationSeconds: 5,
      notes: []
    };
    let resolvePreview!: (value: BackfillPreviewResult) => void;

    vi.spyOn(api, "previewBackfill").mockReturnValueOnce(new Promise<BackfillPreviewResult>((resolve) => {
      resolvePreview = resolve;
    }));

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    fireEvent.change(screen.getByRole("textbox", { name: "Backfill symbols" }), {
      target: { value: "AAPL" }
    });
    await user.click(screen.getByRole("button", { name: "Preview backfill request" }));

    await waitFor(() => expect(screen.getByRole("textbox", { name: "Backfill symbols" })).toBeDisabled());
    expect(screen.getByRole("combobox", { name: "Backfill provider" })).toBeDisabled();
    expect(screen.getByLabelText("Backfill start date")).toBeDisabled();
    expect(screen.getByLabelText("Backfill end date")).toBeDisabled();
    expect(screen.getByRole("textbox", { name: "Backfill symbols" }))
      .toHaveAccessibleDescription("Separate symbols with spaces or commas. At least one symbol is required. Backfill request status Previewing the backfill request. Backfill request is running; wait for the current request to finish before editing.");
    expect(screen.getAllByText("Backfill request is running; wait for the current request to finish before editing.").length).toBeGreaterThan(0);

    await act(async () => {
      resolvePreview(mockPreview);
    });

    await waitFor(() => expect(screen.getByRole("textbox", { name: "Backfill symbols" })).toBeEnabled());
    expect(screen.getByRole("status", { name: /preview ready — polygon/i })).toHaveTextContent("Preview only");
  });

  it("calls triggerBackfill after preview and shows success result", async () => {
    const user = userEvent.setup();

    const mockPreview: BackfillPreviewResult = {
      provider: "polygon",
      providerDisplayName: "Polygon",
      symbols: [{ symbol: "MSFT", estimatedBars: 500, hasMarketHoursData: true, notes: [] }],
      from: "2024-01-01",
      to: "2024-01-31",
      totalDays: 31,
      estimatedTradingDays: 21,
      estimatedDurationSeconds: 5,
      notes: []
    };

    const mockResult: BackfillTriggerResult = {
      success: true,
      provider: "polygon",
      symbols: ["MSFT"],
      from: "2024-01-01",
      to: "2024-01-31",
      barsWritten: 512,
      startedUtc: "2024-01-31T10:00:00Z",
      completedUtc: "2024-01-31T10:00:05Z",
      error: null
    };

    const mockProgress: BackfillProgressResponse = {
      active: false,
      provider: null,
      symbols: [],
      message: null
    };

    vi.spyOn(api, "previewBackfill").mockResolvedValueOnce(mockPreview);
    vi.spyOn(api, "triggerBackfill").mockResolvedValueOnce(mockResult);
    vi.spyOn(api, "getBackfillProgress").mockResolvedValue(mockProgress);

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    await user.type(screen.getByRole("textbox", { name: "Backfill symbols" }), "MSFT");
    await user.click(screen.getByRole("button", { name: "Preview backfill request" }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Run previewed backfill request" })).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Run previewed backfill request" }));

    await waitFor(() => {
      const resultStatus = screen.getByRole("status", { name: /backfill complete — polygon/i });
      expect(resultStatus).toHaveTextContent("Written");
      expect(resultStatus).toHaveTextContent("MSFT");
      expect(resultStatus).toHaveTextContent("512");
    });
  });

  it("shows an error banner when previewBackfill rejects", async () => {
    const user = userEvent.setup();

    vi.spyOn(api, "previewBackfill").mockRejectedValueOnce(new Error("Provider offline"));

    renderWithRouter(<DataScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    await user.type(screen.getByRole("textbox", { name: "Backfill symbols" }), "SPY");
    await user.click(screen.getByRole("button", { name: "Preview backfill request" }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent("Provider offline");
    });
  });
});

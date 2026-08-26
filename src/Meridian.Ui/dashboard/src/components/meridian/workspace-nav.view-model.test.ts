import { describe, expect, it } from "vitest";
import { buildWorkspaceNavViewModel } from "@/components/meridian/workspace-nav.view-model";
import type { WorkspaceSummary } from "@/types";

describe("workspace nav view model", () => {
  it("marks the active canonical workspace route", () => {
    const model = buildWorkspaceNavViewModel("/portfolio/positions");

    expect(model.brandTitle).toBe("Meridian");
    expect(model.items).toHaveLength(7);
    expect(model.items.find((item) => item.key === "portfolio")).toMatchObject({
      route: "/portfolio",
      active: true,
      ariaCurrent: undefined,
      maturityLabel: "Preview · Current",
      maturityTone: "preview",
      ariaLabel: "Portfolio workspace, active section, Preview product maturity"
    });
    expect(model.currentWorkspace).toMatchObject({
      label: "Portfolio",
      maturityLabel: "Preview product maturity",
      maturityTone: "preview",
      route: "/portfolio",
      routeAriaLabel: "Canonical route /portfolio",
      ariaLabel: "Current workspace: Portfolio, Preview product maturity"
    });
    expect(model.deliveryShortcutLabel).toBe("Ctrl K");
    expect(model.items.find((item) => item.key === "trading")).toMatchObject({
      route: "/trading",
      active: false,
      ariaCurrent: undefined,
      maturityLabel: "Available",
      maturityTone: "available",
      ariaLabel: "Open Trading workspace, Available product maturity"
    });
    expect(model.items.find((item) => item.key === "trading")?.subItems.map((item) => item.route)).toEqual([
      "/trading",
      "/trading/orders",
      "/trading/positions",
      "/trading/risk",
      "/trading/readiness"
    ]);
  });

  it("normalizes legacy workspace aliases for current-route state", () => {
    const model = buildWorkspaceNavViewModel("/data-operations/backfills");

    expect(model.items.find((item) => item.key === "data")).toMatchObject({
      active: true,
      ariaCurrent: undefined,
      maturityLabel: "Available · Current",
      maturityTone: "available"
    });
    expect(model.currentWorkspace).toMatchObject({
      label: "Data",
      maturityLabel: "Available product maturity",
      maturityTone: "available"
    });
  });

  it("canonicalizes caller-provided workspace metadata before rendering root navigation", () => {
    const staleWorkspaces: WorkspaceSummary[] = [
      {
        key: "strategy",
        label: "Research",
        description: "Legacy research root label.",
        maturity: "Preview"
      },
      {
        key: "accounting",
        label: "Governance",
        description: "Legacy governance root label.",
        maturity: "Available"
      },
      {
        key: "data",
        label: "Data Operations",
        description: "Legacy data-operations root label.",
        maturity: "Setup"
      }
    ];

    const model = buildWorkspaceNavViewModel("/governance/reconciliation", staleWorkspaces);

    expect(model.items.map((item) => [item.key, item.label, item.maturityLabel])).toEqual([
      ["accounting", "Accounting", "Available · Current"],
      ["strategy", "Strategy", "Preview"],
      ["data", "Data", "Setup"]
    ]);
    expect(model.items.map((item) => item.label)).not.toEqual(
      expect.arrayContaining(["Research", "Governance", "Data Operations"])
    );
    expect(model.currentWorkspace).toMatchObject({
      label: "Accounting",
      description: "Ledger, cash-flow, reconciliation, Security Master coverage, and fund-account evidence.",
      maturityLabel: "Available product maturity"
    });
  });

  it("surfaces the cash-ladder and family-office routes under Portfolio", () => {
    const model = buildWorkspaceNavViewModel("/portfolio/family-office");
    const portfolio = model.items.find((item) => item.key === "portfolio");

    expect(portfolio?.subItems.map((item) => item.route)).toEqual([
      "/portfolio",
      "/portfolio/attribution",
      "/portfolio/asset-detail",
      "/portfolio/brokerage-sync",
      "/portfolio/cash-ladder",
      "/portfolio/family-office"
    ]);
    // Family Office left UNWIRED_WORKSTATION_ROUTES once the screen started loading
    // /api/workstation/family-office/overview, so it belongs in primary navigation again.
    expect(portfolio?.subItems.find((item) => item.route === "/portfolio/family-office")).toBeDefined();

    const cashLadder = buildWorkspaceNavViewModel("/portfolio/cash-ladder")
      .items.find((item) => item.key === "portfolio")
      ?.subItems.find((item) => item.route === "/portfolio/cash-ladder");
    expect(cashLadder).toMatchObject({
      label: "Cash ladder",
      active: true,
      ariaCurrent: "page"
    });
  });

  it("surfaces portfolio-native asset detail under Portfolio", () => {
    const model = buildWorkspaceNavViewModel("/portfolio/asset-detail");
    const portfolio = model.items.find((item) => item.key === "portfolio");

    expect(portfolio?.subItems.find((item) => item.route === "/portfolio/asset-detail")).toMatchObject({
      label: "Asset detail",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Asset detail, current page"
    });
  });

  it("groups one Accounting destination list by lifecycle", () => {
    const model = buildWorkspaceNavViewModel("/accounting/ledger");
    const accounting = model.items.find((item) => item.key === "accounting");

    expect(accounting?.subItems.map((item) => item.route)).toEqual([
      "/accounting",
      "/accounting/operations-continuity",
      "/accounting/close-calendar",
      "/accounting/ledger",
      "/accounting/journal-entries",
      "/accounting/capital-accounts",
      "/accounting/capital-calls",
      "/accounting/security-master",
      "/accounting/statement-import",
      "/accounting/reconciliation",
      "/accounting/reconciliation/external-gl",
      "/accounting/exceptions",
      "/accounting/approvals",
      "/accounting/entity-setup",
      "/accounting/configure"
    ]);
    expect(accounting?.subItemGroups.map((group) => [
      group.label,
      group.items.map((item) => item.label)
    ])).toEqual([
      ["Close", ["Today", "Operations continuity", "Close calendar"]],
      ["Records", ["Ledger explorer", "Adjustments", "Capital accounts", "Capital calls", "Security Master"]],
      ["Reconciliation", ["Import statement", "Casework", "External GL"]],
      ["Review", ["Exceptions", "Approvals"]],
      ["Administration", ["Entity setup", "Configure"]]
    ]);
    expect(accounting?.subItems[0]).toMatchObject({
      label: "Today",
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Today"
    });
    expect(accounting?.subItems[1]).toMatchObject({
      label: "Operations continuity",
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Operations continuity"
    });
    expect(accounting?.subItems[3]).toMatchObject({
      label: "Ledger explorer",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Ledger explorer, current page"
    });
  });

  it("keeps the evidence workbench out of Accounting navigation after canonicalization", () => {
    const model = buildWorkspaceNavViewModel("/accounting/ledger");
    const accounting = model.items.find((item) => item.key === "accounting");
    const reporting = model.items.find((item) => item.key === "reporting");

    expect(accounting?.subItems.map((item) => item.route)).not.toContain("/accounting/evidence");
    expect(reporting?.subItems.find((item) => item.route === "/reporting/evidence")).toMatchObject({
      label: "Evidence"
    });
  });

  it("keeps Accounting route identity aligned for Security Master and Configure", () => {
    const securityMaster = buildWorkspaceNavViewModel("/accounting/security-master/detail")
      .items.find((item) => item.key === "accounting")
      ?.subItems.find((item) => item.route === "/accounting/security-master");
    expect(securityMaster).toMatchObject({
      label: "Security Master",
      active: true,
      ariaCurrent: "page"
    });

    const configure = buildWorkspaceNavViewModel("/accounting/configure")
      .items.find((item) => item.key === "accounting")
      ?.subItems.find((item) => item.route === "/accounting/configure");
    expect(configure).toMatchObject({
      label: "Configure",
      active: true,
      ariaCurrent: "page"
    });
  });

  it("keeps external GL reconciliation distinct from the Meridian break queue", () => {
    const model = buildWorkspaceNavViewModel("/accounting/reconciliation/external-gl");
    const accounting = model.items.find((item) => item.key === "accounting");

    expect(accounting?.subItems.find((item) => item.route === "/accounting/reconciliation/external-gl")).toMatchObject({
      label: "External GL",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "External GL, current page"
    });
    expect(accounting?.subItems.find((item) => item.route === "/accounting/reconciliation")?.active).toBe(false);
  });

  it.each([
    ["/accounting/accounts/detail", "Ledger explorer"],
    ["/accounting/reconciliation/match", "Casework"],
    ["/accounting/journal-entries/detail", "Adjustments"],
    ["/accounting/approvals/inbox", "Approvals"]
  ])("selects one owning Accounting destination for the %s deep link", (pathname, label) => {
    const accounting = buildWorkspaceNavViewModel(pathname)
      .items.find((item) => item.key === "accounting");
    const activeItems = accounting?.subItems.filter((item) => item.active) ?? [];

    expect(activeItems).toHaveLength(1);
    expect(activeItems[0]).toMatchObject({
      label,
      ariaCurrent: "page"
    });
  });

  it("preserves shared Accounting scope but drops route-local selection", () => {
    const model = buildWorkspaceNavViewModel(
      "/accounting/approvals",
      undefined,
      "?fundAccountId=account-alpha&runId=run-42&fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-05&workflowStatus=Blocked&approvalId=approval-1&tab=reference"
    );
    const accounting = model.items.find((item) => item.key === "accounting");
    const ledger = accounting?.subItems.find((item) => item.label === "Ledger explorer");

    expect(ledger?.route).toBe(
      "/accounting/ledger?fundAccountId=account-alpha&runId=run-42&fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-05&workflowStatus=Blocked"
    );
    expect(ledger?.route).not.toContain("approvalId");
    expect(ledger?.route).not.toContain("tab=");
  });

  it("surfaces the operations-record release route under Reporting", () => {
    const model = buildWorkspaceNavViewModel("/reporting/operations-record");
    const reporting = model.items.find((item) => item.key === "reporting");

    expect(model.items.map((item) => item.label)).toEqual([
      "Trading",
      "Portfolio",
      "Accounting",
      "Reporting",
      "Strategy",
      "Data",
      "Settings"
    ]);
    expect(reporting?.subItems.map((item) => item.route)).toEqual([
      "/reporting",
      "/reporting/library",
      "/reporting/scheduled",
      "/reporting/run",
      "/reporting/operations-record",
      "/reporting/report-packs",
      "/reporting/evidence",
      "/reporting/exports"
    ]);
    expect(reporting?.subItems.find((item) => item.route === "/reporting/operations-record")).toMatchObject({
      label: "Operations record",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Operations record, current page"
    });
  });

  it("surfaces scheduled reporting and report parameter pages under Reporting", () => {
    const model = buildWorkspaceNavViewModel("/reporting/scheduled");
    const reporting = model.items.find((item) => item.key === "reporting");

    expect(reporting?.subItems.find((item) => item.route === "/reporting/scheduled")).toMatchObject({
      label: "Scheduled Reports",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Scheduled Reports, current page"
    });
    expect(reporting?.subItems.find((item) => item.route === "/reporting/run")).toMatchObject({
      label: "Run Report",
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Run Report"
    });
  });

  it("surfaces the implemented covered-call backtest route under Strategy", () => {
    const model = buildWorkspaceNavViewModel("/strategy/covered-call");
    const strategy = model.items.find((item) => item.key === "strategy");

    expect(strategy?.subItems.map((item) => item.route)).toEqual([
      "/strategy",
      "/strategy/designer",
      "/strategy/covered-call",
      "/strategy/promotions",
      "/strategy/lab",
      "/strategy/quant-lab",
      "/strategy/run-ledger"
    ]);
    expect(strategy?.subItems.find((item) => item.route === "/strategy/covered-call")).toMatchObject({
      label: "Covered call",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Covered call, current page"
    });
    expect(strategy?.subItems.find((item) => item.route === "/strategy/lab")).toMatchObject({
      label: "Strategy Lab",
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Strategy Lab"
    });
    expect(strategy?.subItems.map((item) => item.route)).not.toContain("/strategy/formula-workbench");
    expect(strategy?.subItems.map((item) => item.label)).not.toContain("Research Lab");
  });

  it("surfaces the consolidated market data desk under Data", () => {
    const model = buildWorkspaceNavViewModel("/data/quotes");
    const data = model.items.find((item) => item.key === "data");

    expect(data?.subItems.map((item) => item.route)).toEqual([
      "/data",
      "/data/import",
      "/data/providers",
      "/data/quotes",
      "/data/operations",
      "/data/assurance",
      "/data/exports",
      "/data/query"
    ]);
    expect(data?.subItems.find((item) => item.route === "/data/quotes")).toMatchObject({
      label: "Market data",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Market data, current page"
    });
  });

  it("keeps the evidence workbench out of Data navigation after canonicalization", () => {
    const model = buildWorkspaceNavViewModel("/data/operations");
    const data = model.items.find((item) => item.key === "data");

    expect(data?.subItems.map((item) => item.route)).not.toContain("/data/evidence");
  });

  it("surfaces the provider catalog lane under Data", () => {
    const model = buildWorkspaceNavViewModel("/data/providers");
    const data = model.items.find((item) => item.key === "data");

    expect(data?.subItems[2]).toMatchObject({
      label: "Providers",
      route: "/data/providers",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Providers, current page"
    });
  });

  it("keeps file import separate from provider connection management", () => {
    const model = buildWorkspaceNavViewModel("/data/import");
    const data = model.items.find((item) => item.key === "data");

    expect(data?.subItems.find((item) => item.route === "/data/import")).toMatchObject({
      label: "Import data",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Import data, current page"
    });
    expect(data?.subItems.find((item) => item.route === "/data/providers")?.active).toBe(false);
  });

  it("surfaces focused canonical Settings tasks and selects nested provider routes", () => {
    const model = buildWorkspaceNavViewModel("/settings/providers/alpaca/advanced");
    const settings = model.items.find((item) => item.key === "settings");

    expect(settings?.subItems.map((item) => [item.label, item.route])).toEqual([
      ["Overview", "/settings"],
      ["Preferences", "/settings/preferences"],
      ["Access", "/settings/access"],
      ["Provider Connections", "/settings/providers"],
      ["Accounting Systems", "/settings/accounting-systems"],
      ["Diagnostics", "/settings/diagnostics"]
    ]);
    expect(settings?.subItems.find((item) => item.label === "Provider Connections")).toMatchObject({
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Provider Connections, current page"
    });
    expect(settings?.subItems.find((item) => item.label === "Overview")?.active).toBe(false);
  });

  it("preserves operating scope across workspace and subroute navigation", () => {
    const model = buildWorkspaceNavViewModel("/data/quotes", undefined, "?symbol=aapl&provider=alpaca");
    const trading = model.items.find((item) => item.key === "trading");
    const data = model.items.find((item) => item.key === "data");
    const quotes = data?.subItems.find((item) => item.label === "Market data");

    expect(model.operatingScopeLabel).toBe("Subject: AAPL / Provider: alpaca");
    expect(trading).toMatchObject({
      route: "/trading?symbol=AAPL&provider=alpaca",
      ariaLabel: "Open Trading workspace, Available product maturity, preserving Subject: AAPL / Provider: alpaca"
    });
    expect(data).toMatchObject({
      route: "/data?symbol=AAPL&provider=alpaca",
      ariaLabel: "Data workspace, active section, Available product maturity, preserving Subject: AAPL / Provider: alpaca"
    });
    expect(quotes).toMatchObject({
      route: "/data/quotes?symbol=AAPL&provider=alpaca",
      active: true,
      ariaLabel: "Market data, current page, preserving Subject: AAPL / Provider: alpaca"
    });
  });

  it("uses stored operating scope when the current route has no scope query", () => {
    const model = buildWorkspaceNavViewModel("/portfolio", undefined, "", {
      fundAccountId: "fund-001",
      runId: "run-44"
    });

    expect(model.currentWorkspace).toMatchObject({
      route: "/portfolio?fundAccountId=fund-001&runId=run-44",
      routeAriaLabel: "Scoped route /portfolio?fundAccountId=fund-001&runId=run-44"
    });
    expect(model.items.find((item) => item.key === "accounting")).toMatchObject({
      route: "/accounting?fundAccountId=fund-001&runId=run-44",
      ariaLabel: "Open Accounting workspace, Available product maturity, preserving Account: fund-001 / Run: Selected run"
    });
  });
});

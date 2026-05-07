import type { WorkspaceKey } from "@/types";

export interface MegaMenuLink {
  label: string;
  route: string;
  description: string;
}

export interface MegaMenuSection {
  key: WorkspaceKey;
  label: string;
  eyebrow: string;
  links: MegaMenuLink[];
}

export interface MegaMenuViewModel {
  sections: MegaMenuSection[];
  triggerAriaLabel: string;
  panelAriaLabel: string;
  closeAriaLabel: string;
  footerLabel: string;
  footerShortcut: string;
}

const MENU_SECTIONS: MegaMenuSection[] = [
  {
    key: "trading",
    label: "Trading",
    eyebrow: "Execution & orders",
    links: [
      { label: "Cockpit", route: "/trading", description: "Live trading dashboard" },
      { label: "Orders", route: "/trading/orders", description: "Order blotter and status" },
      { label: "Positions", route: "/trading/positions", description: "Open position tracking" },
      { label: "Risk", route: "/trading/risk", description: "Real-time risk exposure" },
      { label: "Readiness", route: "/trading/readiness", description: "Pre-trade readiness check" }
    ]
  },
  {
    key: "portfolio",
    label: "Portfolio",
    eyebrow: "Exposure & attribution",
    links: [
      { label: "Exposure", route: "/portfolio", description: "Portfolio exposure overview" },
      { label: "Attribution", route: "/portfolio/attribution", description: "Performance attribution" },
      { label: "Brokerage", route: "/portfolio/brokerage", description: "Brokerage account details" }
    ]
  },
  {
    key: "accounting",
    label: "Accounting",
    eyebrow: "Ledger & reconciliation",
    links: [
      { label: "Ledger", route: "/accounting", description: "Fund ledger entries" },
      { label: "Reconciliation", route: "/accounting/reconciliation", description: "Position reconciliation" },
      { label: "Approvals", route: "/accounting/approvals", description: "Pending approval queue" }
    ]
  },
  {
    key: "reporting",
    label: "Reporting",
    eyebrow: "Reports & exports",
    links: [
      { label: "Report Packs", route: "/reporting", description: "Scheduled report packs" },
      { label: "Exports", route: "/reporting/exports", description: "Data export queue" }
    ]
  },
  {
    key: "strategy",
    label: "Strategy",
    eyebrow: "Research & backtesting",
    links: [
      { label: "Backtest Runs", route: "/strategy", description: "Strategy backtest results" },
      { label: "Promotions", route: "/strategy/promotions", description: "Paper-to-live promotions" },
      { label: "Research", route: "/strategy/research", description: "Signal research workspace" }
    ]
  },
  {
    key: "data",
    label: "Data",
    eyebrow: "Providers & feeds",
    links: [
      { label: "Provider Posture", route: "/data", description: "Data provider health" },
      { label: "Backfill Queues", route: "/data/backfill", description: "Historical data backfill" },
      { label: "Feed Monitor", route: "/data/feeds", description: "Live feed status" }
    ]
  },
  {
    key: "settings",
    label: "Settings",
    eyebrow: "Session & preferences",
    links: [
      { label: "Session", route: "/settings", description: "Active session details" },
      { label: "Preferences", route: "/settings/preferences", description: "User preferences" },
      { label: "Integrations", route: "/settings/integrations", description: "API and broker connections" }
    ]
  }
];

export function buildMegaMenuViewModel(): MegaMenuViewModel {
  return {
    sections: MENU_SECTIONS,
    triggerAriaLabel: "Open workspace navigation menu",
    panelAriaLabel: "Workspace navigation",
    closeAriaLabel: "Close navigation menu",
    footerLabel: "All workspaces · Palette-first routing",
    footerShortcut: "Ctrl K"
  };
}

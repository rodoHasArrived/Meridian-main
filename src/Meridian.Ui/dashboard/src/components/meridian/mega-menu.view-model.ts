import { useCallback, useEffect, useMemo, useState } from "react";
import {
  appendOperatingScopeToRoute,
  buildOperatingScopeFromSearch,
  summarizeOperatingScopeForRoute,
  type AppShellOperatingScopeInput
} from "@/app-shell.view-model";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { WorkspaceKey } from "@/types";

export interface MegaMenuLink {
  label: string;
  route: string;
  description: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
}

export interface MegaMenuSection {
  key: WorkspaceKey;
  label: string;
  eyebrow: string;
  links: MegaMenuLink[];
  active: boolean;
  ariaCurrent: "page" | undefined;
}

export interface MegaMenuViewModel {
  open: boolean;
  sections: MegaMenuSection[];
  operatingScopeLabel: string | null;
  operatingScopeAriaLabel: string | null;
  triggerAriaLabel: string;
  triggerControlsId: string;
  triggerExpanded: boolean;
  panelAriaLabel: string;
  panelId: string;
  closeAriaLabel: string;
  footerLabel: string;
  footerShortcut: string;
  openMenu: () => void;
  closeMenu: () => void;
  toggleMenu: () => void;
}

export type MegaMenuFocusBoundary = "outside" | "first" | "middle" | "last" | "none";
export type MegaMenuKeyCommand = "close" | "focus-first" | "focus-last" | null;

type StaticMegaMenuLink = Omit<MegaMenuLink, "active" | "ariaCurrent" | "ariaLabel">;
type StaticMegaMenuSection = Omit<MegaMenuSection, "active" | "ariaCurrent" | "links"> & {
  links: StaticMegaMenuLink[];
};

const MEGA_MENU_PANEL_ID = "workspace-mega-menu-panel";

const MENU_SECTIONS: StaticMegaMenuSection[] = [
  {
    key: "trading",
    label: "Trading",
    eyebrow: "Execution & orders",
    links: [
      { label: "Cockpit", route: WORKSTATION_ROUTE_CATALOG.trading, description: "Live trading dashboard" },
      { label: "Orders", route: WORKSTATION_ROUTE_CATALOG.tradingOrders, description: "Order blotter and status" },
      { label: "Positions", route: WORKSTATION_ROUTE_CATALOG.tradingPositions, description: "Open position tracking" },
      { label: "Risk", route: WORKSTATION_ROUTE_CATALOG.tradingRisk, description: "Real-time risk exposure" },
      { label: "Readiness", route: WORKSTATION_ROUTE_CATALOG.tradingReadiness, description: "Pre-trade readiness check" }
    ]
  },
  {
    key: "portfolio",
    label: "Portfolio",
    eyebrow: "Exposure & attribution",
    links: [
      { label: "Exposure", route: WORKSTATION_ROUTE_CATALOG.portfolio, description: "Portfolio exposure overview" },
      { label: "Attribution", route: WORKSTATION_ROUTE_CATALOG.portfolioAttribution, description: "Performance attribution" },
      { label: "Brokerage sync", route: WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync, description: "Brokerage account sync review" }
    ]
  },
  {
    key: "accounting",
    label: "Accounting",
    eyebrow: "Ledger & reconciliation",
    links: [
      { label: "Ledger", route: WORKSTATION_ROUTE_CATALOG.accounting, description: "Fund ledger entries" },
      { label: "Reconciliation", route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation, description: "Position reconciliation" },
      { label: "Security Master", route: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster, description: "Reference-data readiness" },
      { label: "Approvals", route: WORKSTATION_ROUTE_CATALOG.accountingApprovals, description: "Pending approval queue" }
    ]
  },
  {
    key: "reporting",
    label: "Reporting",
    eyebrow: "Reports & exports",
    links: [
      { label: "Profiles", route: WORKSTATION_ROUTE_CATALOG.reporting, description: "Governed export profiles" },
      { label: "Report packs", route: WORKSTATION_ROUTE_CATALOG.reportingReportPacks, description: "Approval-ready report packet review" },
      { label: "Evidence", route: WORKSTATION_ROUTE_CATALOG.reportingEvidence, description: "Packet completeness and lineage" },
      { label: "Exports", route: WORKSTATION_ROUTE_CATALOG.reportingExports, description: "Data export queue" }
    ]
  },
  {
    key: "strategy",
    label: "Strategy",
    eyebrow: "Research & backtesting",
    links: [
      { label: "Backtest runs", route: WORKSTATION_ROUTE_CATALOG.strategy, description: "Strategy backtest results" },
      { label: "Designer", route: WORKSTATION_ROUTE_CATALOG.strategyDesigner, description: "Visual multi-leg strategy designer with payoff and participation visualizations" },
      { label: "Covered call", route: WORKSTATION_ROUTE_CATALOG.strategyCoveredCall, description: "Run covered-call backtests with chain preview and payoff evidence" },
      { label: "Promotions", route: WORKSTATION_ROUTE_CATALOG.strategyPromotions, description: "Paper-to-live promotions" },
      { label: "Research", route: WORKSTATION_ROUTE_CATALOG.strategyResearch, description: "Signal research workspace" },
      { label: "Quant Lab", route: WORKSTATION_ROUTE_CATALOG.strategyQuantLab, description: "Run C# scripts with plots and metrics" }
    ]
  },
  {
    key: "data",
    label: "Data",
    eyebrow: "Providers & feeds",
    links: [
      { label: "Provider posture", route: WORKSTATION_ROUTE_CATALOG.data, description: "Data provider health" },
      { label: "Watchlist", route: WORKSTATION_ROUTE_CATALOG.dataWatchlist, description: "Symbol subscription setup" },
      { label: "Live quotes", route: WORKSTATION_ROUTE_CATALOG.dataQuotes, description: "Quote, trade, and depth workbench" },
      { label: "Price alerts", route: WORKSTATION_ROUTE_CATALOG.dataAlerts, description: "Local quote-threshold alerts and trigger review" },
      { label: "Backfill queues", route: WORKSTATION_ROUTE_CATALOG.dataBackfills, description: "Historical data backfill" }
    ]
  },
  {
    key: "settings",
    label: "Settings",
    eyebrow: "Session & preferences",
    links: [
      { label: "Session", route: WORKSTATION_ROUTE_CATALOG.settings, description: "Active session details" },
      { label: "Preferences", route: WORKSTATION_ROUTE_CATALOG.settingsPreferences, description: "User preferences" },
      { label: "Integrations", route: WORKSTATION_ROUTE_CATALOG.settingsIntegrations, description: "API and broker connections" }
    ]
  }
];

export function useMegaMenuViewModel(
  pathname: string,
  search = "",
  operatingContextScope: AppShellOperatingScopeInput | null = null
): MegaMenuViewModel {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    setOpen(false);
  }, [pathname]);

  const openMenu = useCallback(() => setOpen(true), []);
  const closeMenu = useCallback(() => setOpen(false), []);
  const toggleMenu = useCallback(() => setOpen((current) => !current), []);

  return useMemo(
    () => buildMegaMenuViewModel({
      pathname,
      open,
      search,
      operatingContextScope,
      openMenu,
      closeMenu,
      toggleMenu
    }),
    [closeMenu, open, openMenu, operatingContextScope, pathname, search, toggleMenu]
  );
}

export function buildMegaMenuViewModel({
  pathname,
  open,
  search = "",
  operatingContextScope = null,
  openMenu,
  closeMenu,
  toggleMenu
}: {
  pathname: string;
  open: boolean;
  search?: string;
  operatingContextScope?: AppShellOperatingScopeInput | null;
  openMenu: () => void;
  closeMenu: () => void;
  toggleMenu: () => void;
}): MegaMenuViewModel {
  const operatingScope = buildOperatingScopeFromSearch(search, operatingContextScope);
  return {
    open,
    sections: buildMegaMenuSections(pathname, operatingScope),
    operatingScopeLabel: operatingScope.hasScope ? operatingScope.summary : null,
    operatingScopeAriaLabel: operatingScope.hasScope ? `Mega menu preserves operating scope: ${operatingScope.summary}` : null,
    triggerAriaLabel: open ? "Close workspace navigation menu" : "Open workspace navigation menu",
    triggerControlsId: MEGA_MENU_PANEL_ID,
    triggerExpanded: open,
    panelAriaLabel: "Workspace navigation",
    panelId: MEGA_MENU_PANEL_ID,
    closeAriaLabel: "Close navigation menu",
    footerLabel: "All workspaces / Palette-first routing",
    footerShortcut: "Ctrl K",
    openMenu,
    closeMenu,
    toggleMenu
  };
}

export function resolveMegaMenuKeyCommand({
  key,
  shiftKey,
  focusBoundary
}: {
  key: string;
  shiftKey: boolean;
  focusBoundary: MegaMenuFocusBoundary;
}): MegaMenuKeyCommand {
  if (key === "Escape") {
    return "close";
  }

  if (key !== "Tab") {
    return null;
  }

  if (focusBoundary === "none") {
    return "focus-first";
  }

  if (shiftKey && (focusBoundary === "first" || focusBoundary === "outside")) {
    return "focus-last";
  }

  if (!shiftKey && (focusBoundary === "last" || focusBoundary === "outside")) {
    return "focus-first";
  }

  return null;
}

function buildMegaMenuSections(
  pathname: string,
  operatingScope: ReturnType<typeof buildOperatingScopeFromSearch>
): MegaMenuSection[] {
  return MENU_SECTIONS.map((section) => {
    const sectionActive = isWorkspaceSectionActive(pathname, section.key);
    return {
      ...section,
      active: sectionActive,
      ariaCurrent: sectionActive ? "page" : undefined,
      links: section.links.map((link) => {
        const active = isRouteActive(pathname, link.route);
        const route = appendOperatingScopeToRoute(link.route, operatingScope);
        const scopeSummary = summarizeOperatingScopeForRoute(link.route, operatingScope);
        return {
          ...link,
          route,
          active,
          ariaCurrent: active ? "page" : undefined,
          ariaLabel: active
            ? `${link.label}, current route, ${section.label} workspace${formatPreservedScopeAriaSuffix(scopeSummary)}`
            : `Open ${link.label}, ${section.label} workspace${formatPreservedScopeAriaSuffix(scopeSummary)}`
        };
      })
    };
  });
}

function formatPreservedScopeAriaSuffix(scopeSummary: string | null): string {
  return scopeSummary ? `, preserving ${scopeSummary}` : "";
}

function isWorkspaceSectionActive(pathname: string, key: WorkspaceKey): boolean {
  const workspaceRoot = WORKSTATION_ROUTE_CATALOG[key];
  return pathname === workspaceRoot || pathname.startsWith(`${workspaceRoot}/`);
}

function isRouteActive(pathname: string, route: string): boolean {
  const routeSegments = route.split("/").filter(Boolean);
  if (routeSegments.length <= 1) {
    return pathname === route;
  }

  return pathname === route || pathname.startsWith(`${route}/`);
}

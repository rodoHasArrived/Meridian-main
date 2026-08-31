import {
  WORKSTATION_ROUTE_CATALOG,
  workstationRouteWithQuery
} from "@/lib/workspace";

export interface DevelopmentFixtureNoticeStep {
  id: "watchlist" | "quotes" | "readiness" | "connect";
  step: string;
  href: string;
  label: string;
  ariaLabel: string;
  active: boolean;
}

export interface DevelopmentFixtureNoticeViewModel {
  role: "status";
  ariaLive: "polite";
  title: string;
  detail: string;
  workflowLabel: string;
  retryLabel: string;
  retryAriaLabel: string;
  retryDisabled: boolean;
  retryBusy: boolean;
  steps: DevelopmentFixtureNoticeStep[];
}

export function buildDevelopmentFixtureNoticeViewModel({
  pathname,
  search = "",
  hash = "",
  refreshing = false
}: {
  pathname: string;
  search?: string;
  hash?: string;
  refreshing?: boolean;
}): DevelopmentFixtureNoticeViewModel {
  return {
    role: "status",
    ariaLive: "polite",
    title: "Demo data",
    detail: "Showing demo data because live Meridian data is unavailable; use the evidence path for watchlist, quotes, readiness, and Alpaca setup.",
    workflowLabel: "Evidence path",
    retryLabel: refreshing ? "Retrying live data" : "Retry live data",
    retryAriaLabel: refreshing
      ? "Retrying live Meridian workspace data"
      : "Retry live Meridian workspace data",
    retryDisabled: refreshing,
    retryBusy: refreshing,
    steps: developmentFixtureDemoSteps.map((item) => ({
      ...item,
      active: isCurrentDevelopmentFixtureDemoStep(item, pathname, search, hash)
    }))
  };
}

const developmentFixtureDemoSteps = [
  {
    id: "watchlist",
    step: "1",
    href: workstationRouteWithQuery("dataQuotes", { view: "watchlist" }),
    matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes,
    matchView: "watchlist",
    label: "Watchlist",
    ariaLabel: "Open sample watchlist demo lane"
  },
  {
    id: "quotes",
    step: "2",
    href: workstationRouteWithQuery("dataQuotes", { symbol: "AAPL" }),
    matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes,
    matchView: "quotes",
    label: "Quotes",
    ariaLabel: "Open sample live quotes for AAPL"
  },
  {
    id: "readiness",
    step: "3",
    href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    label: "Readiness",
    ariaLabel: "Open sample readiness console"
  },
  {
    id: "connect",
    step: "4",
    href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
    matchPath: WORKSTATION_ROUTE_CATALOG.settings,
    matchHash: "#alpaca-provider-setup",
    label: "Connect",
    ariaLabel: "Open Alpaca paper provider setup"
  }
] as const;

function isCurrentDevelopmentFixtureDemoStep(
  item: (typeof developmentFixtureDemoSteps)[number],
  pathname: string,
  search: string,
  hash: string
) {
  if (item.matchPath !== pathname) {
    return false;
  }

  if ("matchView" in item && item.matchView !== resolveMarketDataDemoView(search)) {
    return false;
  }

  return !("matchHash" in item) || item.matchHash === hash;
}

/** Mirror the Market Data desk's view resolution: unknown views fall back to quotes. */
function resolveMarketDataDemoView(search: string): "quotes" | "watchlist" | "alerts" {
  const view = new URLSearchParams(search).get("view");
  return view === "watchlist" || view === "alerts" ? view : "quotes";
}

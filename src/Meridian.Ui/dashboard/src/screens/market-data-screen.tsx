import { useSearchParams } from "react-router-dom";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { LiveQuotesScreen } from "@/screens/live-quotes-screen";
import { PriceAlertsScreen } from "@/screens/price-alerts-screen";
import { WatchlistScreen } from "@/screens/watchlist-screen";

const MARKET_DATA_TABS = [
  { id: "quotes", label: "Live quotes" },
  { id: "watchlist", label: "Watchlist" },
  { id: "alerts", label: "Price alerts" }
];

type MarketDataView = "quotes" | "watchlist" | "alerts";

function resolveMarketDataView(requested: string | null): MarketDataView {
  return requested === "watchlist" || requested === "alerts" ? requested : "quotes";
}

/**
 * Market Data desk: one routed home for the live-quote, watchlist, and
 * price-alert panels over the shared quote stream. Only the active view
 * mounts, so the stream subscription and alert polling stay scoped to the
 * visible panel.
 */
export function MarketDataScreen() {
  const [searchParams, setSearchParams] = useSearchParams();
  const view = resolveMarketDataView(searchParams.get("view"));

  return (
    <Tabs
      tabs={MARKET_DATA_TABS}
      value={view}
      onValueChange={(nextView) => {
        const nextParams = new URLSearchParams(searchParams);
        if (nextView === "quotes") {
          nextParams.delete("view");
        } else {
          nextParams.set("view", nextView);
        }
        setSearchParams(nextParams, { replace: true });
      }}
    >
      <TabPanel>{view === "quotes" ? <LiveQuotesScreen /> : null}</TabPanel>
      <TabPanel>{view === "watchlist" ? <WatchlistScreen /> : null}</TabPanel>
      <TabPanel>{view === "alerts" ? <PriceAlertsScreen /> : null}</TabPanel>
    </Tabs>
  );
}

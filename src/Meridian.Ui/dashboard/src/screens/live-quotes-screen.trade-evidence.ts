import type { ChartEvidence } from "@/components/charts/chart-evidence";
import type { TradeDataResponse } from "@/types";

export function liveQuoteTradeId(trade: TradeDataResponse): string {
  return `${trade.sequenceNumber}-${trade.timestamp}`;
}

export function liveTradeChartEvidence(trade: TradeDataResponse, price: number, selectTrade?: (id: string) => void, panelId?: string): ChartEvidence {
  const tradeId = liveQuoteTradeId(trade);
  return {
    id: `${trade.symbol}:${tradeId}`,
    label: `${trade.symbol} print ${trade.sequenceNumber}`,
    asOf: trade.timestamp,
    values: [{ series: `${trade.symbol} trade price`, value: price, unit: "Price per unit (currency unreported)", basis: "Price of the individual print received by the live pipeline." }],
    proof: selectTrade && panelId ? { href: `#${panelId}`, label: `Open trade print ${trade.sequenceNumber}`, onOpen: () => selectTrade(tradeId) } : null,
    sourceDetail: `Venue ${trade.venue ?? "unreported"} · stream ${trade.streamId ?? "unreported"}`
  };
}

// Explains a Portfolio header strip and Portfolio Explorer that report different figures. Kept out
// of the screen view model so the comparison rules have one small home.
import { normalizeFinancialLabel } from "@/lib/format";
import { buildFigureDifferenceNote, type FigureDifferenceNote } from "@/lib/metric-trust";
import type { FinancialRecordExplorerDto, MetricProvenance, MetricSnapshot } from "@/types";

interface PortfolioFigureKey {
  key: string;
  basis?: string;
}

// The header's "Total exposure" sums each position's absolute exposure, so it is gross.
const PORTFOLIO_HEADER_FIGURE_KEYS: Record<string, PortfolioFigureKey> = {
  "portfolio-open-positions": { key: "positions" },
  "portfolio-total-exposure": { key: "exposure", basis: "gross" },
  "portfolio-unrealized-pnl": { key: "unrealized-pnl" }
};

// The host explorer reports "Net Exposure" (signed long plus short market value). With short
// positions that is a different measure from the header's gross total, so the basis is carried
// with the figure and a gross-against-net gap is never presented as a conflict by itself.
function portfolioExplorerFigureKey(label: string): PortfolioFigureKey | null {
  const normalized = normalizeFinancialLabel(label).trim().toLowerCase();
  if (normalized === "positions" || normalized === "open positions") {
    return { key: "positions" };
  }
  if (normalized === "net exposure") {
    return { key: "exposure", basis: "net" };
  }
  if (normalized === "gross exposure" || normalized === "total exposure") {
    return { key: "exposure", basis: "gross" };
  }
  if (normalized === "market value") {
    return { key: "exposure" };
  }
  return normalized === "unrealized p&l" ? { key: "unrealized-pnl" } : null;
}

/**
 * The header strip is computed from open positions in the workspace read model; the explorer
 * summarizes the position records retained for a strategy run. They can legitimately disagree, so
 * when they do the page says which figure comes from where instead of leaving two unexplained
 * totals side by side. Pass the explorer as presented (`buildPortfolioExplorerPresentation`) so
 * the run and as-of time read the way the explorer shows them.
 */
export function buildPortfolioFigureDifferenceNote({
  headerMetrics,
  headerProvenance,
  explorer,
  retainedRunLabel
}: {
  headerMetrics: readonly MetricSnapshot[];
  headerProvenance: MetricProvenance;
  explorer: FinancialRecordExplorerDto | null;
  retainedRunLabel?: string;
}): FigureDifferenceNote | null {
  if (!explorer || explorer.isBlocked) {
    return null;
  }

  const scopeValue = (label: string) =>
    explorer.scopeItems.find((item) => item.label.trim().toLowerCase() === label)?.value.trim() || null;
  const explorerFigures = explorer.summaryItems.flatMap((item) => {
    const figure = portfolioExplorerFigureKey(item.label);
    return figure ? [{ ...figure, label: normalizeFinancialLabel(item.label), value: item.value }] : [];
  });
  const headerFigures = headerMetrics.flatMap((metric) => {
    const figure = PORTFOLIO_HEADER_FIGURE_KEYS[metric.id];
    return figure ? [{ ...figure, label: metric.label, value: metric.value }] : [];
  });

  return buildFigureDifferenceNote(
    { name: "Live holdings", provenance: headerProvenance, figures: headerFigures },
    {
      name: "Retained run records",
      provenance: {
        // Identify the retained record set independently of the current source selection.
        source: /^demo\b/i.test(scopeValue("source") ?? "")
          ? "Retained run records (demo portfolio data)"
          : "Retained run records",
        asOf: scopeValue("as of"),
        scope: retainedRunLabel ?? scopeValue("run") ?? scopeValue("run id")
      },
      figures: explorerFigures
    }
  );
}

interface ComparablePosition {
  symbol: string;
  quantity: string;
  averagePrice: string;
}

function decimalOf(value: string | null | undefined): number | null {
  const text = value?.replace(/[$,\s]/g, "").replace(/−/g, "-");
  if (!text) return null;
  const parsed = Number(text);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * Symbols that read differently in Portfolio's live holdings (every account) and Trading's
 * brokerage session (the selected account). Neither figure is wrong: they come from different
 * sources, and the page names both instead of leaving two unexplained numbers on two screens.
 */
export function buildPositionSourceDifferences({
  holdings,
  session,
  sessionLabel
}: {
  holdings: readonly ComparablePosition[] | null | undefined;
  session: readonly ComparablePosition[] | null | undefined;
  sessionLabel: string;
}): string[] {
  if (!holdings?.length || !session?.length) return [];
  const sessionBySymbol = new Map(session.map((position) => [position.symbol.trim().toUpperCase(), position]));
  return holdings.flatMap((holding) => {
    const match = sessionBySymbol.get(holding.symbol.trim().toUpperCase());
    if (!match) return [];
    const quantityDiffers = decimalOf(holding.quantity) !== decimalOf(match.quantity);
    const priceDiffers = decimalOf(holding.averagePrice) !== decimalOf(match.averagePrice);
    if (!quantityDiffers && !priceDiffers) return [];
    return [`${holding.symbol}: ${holding.quantity} at ${holding.averagePrice} in live holdings vs ${match.quantity} at ${match.averagePrice} in the ${sessionLabel.charAt(0).toLowerCase()}${sessionLabel.slice(1)}. Different sources, not a conflict.`];
  });
}

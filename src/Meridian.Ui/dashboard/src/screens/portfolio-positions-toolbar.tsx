import { useId } from "react";
import { Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import {
  hasPortfolioPositionControls,
  PORTFOLIO_POSITION_SEARCH_MAX_LENGTH,
  type PortfolioPositionsTableState
} from "@/lib/portfolio-positions-table";

interface PortfolioPositionsToolbarProps {
  state: PortfolioPositionsTableState;
  sides: string[];
  resultCount: number;
  loadedCount: number;
  onSearch: (search: string) => void;
  onSearchBlur: () => void;
  onSide: (side: string | null) => void;
  onReadiness: (readiness: PortfolioPositionsTableState["readiness"]) => void;
  onReset: () => void;
  shareError?: string | null;
}

/** Controls describe loaded holdings only; financial scope and totals remain server-owned. */
export function PortfolioPositionsToolbar({
  state, sides, resultCount, loadedCount, onSearch, onSearchBlur, onSide, onReadiness, onReset, shareError
}: PortfolioPositionsToolbarProps) {
  const id = useId();
  return (
    <div className="mb-3 space-y-2" role="group" aria-label="Open positions controls">
      <div className="grid gap-2 sm:grid-cols-[minmax(8rem,1fr)_minmax(6rem,0.6fr)_minmax(9rem,0.8fr)]">
        <div className="min-w-0">
          <label htmlFor={`${id}-search`} className="text-xs font-medium">Search positions by symbol</label>
          <Input
            id={`${id}-search`}
            type="search"
            aria-label="Search positions by symbol"
            placeholder="Find a symbol"
            maxLength={PORTFOLIO_POSITION_SEARCH_MAX_LENGTH}
            value={state.search}
            onChange={(event) => onSearch(event.target.value)}
            onBlur={onSearchBlur}
            leadingIcon={<Search className="size-4" />}
          />
        </div>
        <div className="min-w-0">
          <label htmlFor={`${id}-side`} className="text-xs font-medium">Side</label>
          <Select id={`${id}-side`} value={state.side ?? ""} onChange={(event) => onSide(event.target.value || null)}>
            <option value="">All</option>
            {state.side !== null && !sides.includes(state.side) ? <option value={state.side}>{state.side} (not loaded)</option> : null}
            {sides.filter(Boolean).map((side) => <option key={side} value={side}>{side}</option>)}
          </Select>
        </div>
        <div className="min-w-0">
          <label htmlFor={`${id}-readiness`} className="text-xs font-medium">Mark readiness</label>
          <Select id={`${id}-readiness`} value={state.readiness} onChange={(event) => onReadiness(event.target.value as PortfolioPositionsTableState["readiness"])}>
            <option value="all">All</option>
            <option value="current">Current</option>
            <option value="review-required">Review required</option>
          </Select>
        </div>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p role="status" aria-live="polite" className="text-xs text-muted-foreground">Showing {resultCount} of {loadedCount} loaded positions</p>
        <Button type="button" size="sm" variant="ghost" disabled={!hasPortfolioPositionControls(state)} onClick={onReset}>Reset controls</Button>
      </div>
      {shareError ? <p role="alert" className="text-xs text-danger">{shareError}</p> : null}
    </div>
  );
}

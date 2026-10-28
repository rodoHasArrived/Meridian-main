import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import {
  DEFAULT_PORTFOLIO_POSITIONS_TABLE_STATE,
  normalizePortfolioPositionsTableState,
  type PortfolioPositionsTableState
} from "@/lib/portfolio-positions-table";
import {
  encodeViewStateEnvelope,
  readViewStateFromSearch,
  VIEW_STATE_ENVELOPE_VERSION,
  VIEW_STATE_QUERY_KEY
} from "@/lib/view-state-envelope";
import { WORKSTATION_SCREEN_VIEW_STATE_SCREENS } from "@/lib/workstation-screen-view-states";

const SCREEN = WORKSTATION_SCREEN_VIEW_STATE_SCREENS.portfolioWorkstation;
const SEARCH_URL_DELAY_MS = 250;

function controlsFromSearch(search: string): PortfolioPositionsTableState {
  return normalizePortfolioPositionsTableState(readViewStateFromSearch(search, SCREEN)?.state.positionsTable);
}

/** Screen-local controls update immediately; only their search URL write is delayed.
 * Wire flushSearch to the search field's blur so adjacent Copy/Save commands capture it.
 */
export function usePortfolioPositionsTable() {
  const location = useLocation();
  const navigate = useNavigate();
  const [state, setState] = useState<PortfolioPositionsTableState>(() => controlsFromSearch(location.search));
  const [shareError, setShareError] = useState<string | null>(null);
  const stateRef = useRef(state);
  const locationRef = useRef(location);
  const pendingTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchPending = useRef(false);
  const ownNavigation = useRef<string | null>(null);
  stateRef.current = state;
  locationRef.current = location;

  const cancelSearch = useCallback(() => {
    if (pendingTimer.current !== null) clearTimeout(pendingTimer.current);
    pendingTimer.current = null;
    searchPending.current = false;
  }, []);

  useLayoutEffect(() => {
    const currentRoute = `${location.pathname}${location.search}${location.hash}`;
    if (ownNavigation.current === currentRoute) {
      ownNavigation.current = null;
      return;
    }
    ownNavigation.current = null;
    cancelSearch();
    const restored = controlsFromSearch(location.search);
    stateRef.current = restored;
    setState(restored);
    setShareError(null);
  }, [location.key, location.pathname, location.search, location.hash, cancelSearch]);

  useEffect(() => cancelSearch, [cancelSearch]);

  const writeUrl = useCallback((next: PortfolioPositionsTableState, replace: boolean) => {
    cancelSearch();
    const current = locationRef.current;
    const existing = readViewStateFromSearch(current.search, SCREEN);
    const token = encodeViewStateEnvelope({
      v: VIEW_STATE_ENVELOPE_VERSION,
      screen: SCREEN,
      state: {
        ...(existing?.state ?? {}),
        positionsTable: next
      }
    });
    if (token === null) {
      setShareError("These table controls could not fit in the shared view link. They remain applied locally, but the link still contains its previous settings.");
      return;
    }
    setShareError(null);
    const params = new URLSearchParams(current.search);
    params.set(VIEW_STATE_QUERY_KEY, token);
    const target = `${current.pathname}?${params.toString()}${current.hash}`;
    if (target === `${current.pathname}${current.search}${current.hash}`) return;
    ownNavigation.current = target;
    navigate(target, { replace, preventScrollReset: true });
  }, [cancelSearch, navigate]);

  const update = useCallback((next: PortfolioPositionsTableState, debounceSearch = false) => {
    const normalized = normalizePortfolioPositionsTableState(next);
    stateRef.current = normalized;
    setState(normalized);
    cancelSearch();
    if (debounceSearch) {
      searchPending.current = true;
      pendingTimer.current = setTimeout(() => writeUrl(stateRef.current, true), SEARCH_URL_DELAY_MS);
    } else {
      writeUrl(normalized, false);
    }
  }, [cancelSearch, writeUrl]);

  const flushSearch = useCallback(() => {
    if (searchPending.current) writeUrl(stateRef.current, true);
  }, [writeUrl]);
  const setSearch = useCallback((search: string) => update({ ...stateRef.current, search }, true), [update]);
  const setSide = useCallback((side: string | null) => update({ ...stateRef.current, side }), [update]);
  const setReadiness = useCallback((readiness: PortfolioPositionsTableState["readiness"]) => update({ ...stateRef.current, readiness }), [update]);
  const toggleSort = useCallback((columnId: string) => {
    if (columnId !== "symbol" && columnId !== "side" && columnId !== "mark-readiness") return;
    const current = stateRef.current.sort;
    update({ ...stateRef.current, sort: { columnId, direction: current?.columnId === columnId && current.direction === "asc" ? "desc" : "asc" } });
  }, [update]);
  const resetControls = useCallback(() => update({ ...DEFAULT_PORTFOLIO_POSITIONS_TABLE_STATE }), [update]);
  const clearFilters = useCallback(() => update({ ...stateRef.current, search: "", side: null, readiness: "all" }), [update]);

  return { state, setSearch, setSide, setReadiness, toggleSort, resetControls, clearFilters, flushSearch, shareError };
}

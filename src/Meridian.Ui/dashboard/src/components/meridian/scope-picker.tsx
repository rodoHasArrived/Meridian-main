import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { searchSymbolsQuery } from "@/lib/api";
import {
  normalizeOperatingContextSymbol,
  type AppShellOperatingScopeInput,
  type AppShellOperatingScopeQueryKey
} from "@/app-shell.operating-scope";
import type { ScopeFundAccountOption } from "@/lib/operating-scope/fund-accounts";
import type { SymbolRecord } from "@/types";

const SYMBOL_SEARCH_DEBOUNCE_MS = 250;
const SYMBOL_SEARCH_MIN_LENGTH = 2;
const SYMBOL_SUGGESTION_LIMIT = 8;
const RETAINED_FIELDS = [
  ["runId", "Run", "runId"],
  ["from", "From", "window"],
  ["to", "To", "window"],
  ["date", "Date", "window"],
  ["asOf", "As of", "window"]
] as const;

type SymbolSearchState = { query: string; status: "idle" | "loading" | "ready" | "error"; records: SymbolRecord[] };

export interface ScopePickerProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  scope: AppShellOperatingScopeInput;
  fundAccounts: ScopeFundAccountOption[];
  onApply: (next: AppShellOperatingScopeInput) => void;
  scopeDimensionsInEffect?: AppShellOperatingScopeQueryKey[];
  /** Overridable for tests. */
  searchSymbols?: typeof searchSymbolsQuery;
}

/** Draft-only editing: all scope dimensions remain inspectable, and only Apply changes scope. */
export function ScopePicker({ open, onOpenChange, scope, fundAccounts, onApply, scopeDimensionsInEffect, searchSymbols = searchSymbolsQuery }: ScopePickerProps) {
  const [symbol, setSymbol] = useState("");
  const [fundAccountId, setFundAccountId] = useState("");
  const [provider, setProvider] = useState("");
  const [retained, setRetained] = useState<AppShellOperatingScopeInput>({});
  const [clearStaged, setClearStaged] = useState(false);
  const [search, setSearch] = useState<SymbolSearchState>({ query: "", status: "idle", records: [] });
  const [retry, setRetry] = useState(0);
  const [dismissed, setDismissed] = useState(false);
  const [symbolFocused, setSymbolFocused] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const symbolRef = useRef<HTMLInputElement>(null);
  const suggestionsRef = useRef<HTMLUListElement>(null);
  const id = useId();
  const suggestionsId = `${id}-suggestions`;
  const symbolQuery = symbol.trim();
  const currentSearch = search.query === symbolQuery ? search : { query: symbolQuery, status: "loading", records: [] };
  const suggestions = currentSearch.records;
  const listOpen = symbolFocused && !dismissed && suggestions.length > 0;
  const applicability = (key: AppShellOperatingScopeQueryKey) => scopeDimensionsInEffect
    ? scopeDimensionsInEffect.includes(key) ? "Applied on this screen" : "Retained for other screens"
    : "Carried across the workstation";

  useEffect(() => {
    if (!open) return;
    setSymbol(scope.symbol ?? "");
    setFundAccountId(scope.fundAccountId ?? "");
    setProvider(scope.provider ?? "");
    setRetained({ runId: scope.runId, from: scope.from, to: scope.to, date: scope.date, asOf: scope.asOf });
    setClearStaged(false);
    setDismissed(false);
    setActiveIndex(-1);
    setSearch({ query: "", status: "idle", records: [] });
  }, [open, scope.symbol, scope.fundAccountId, scope.provider, scope.runId, scope.from, scope.to, scope.date, scope.asOf]);

  useEffect(() => {
    if (!open || dismissed || symbolQuery.length < SYMBOL_SEARCH_MIN_LENGTH) return;
    let active = true;
    const controller = new AbortController();
    setSearch({ query: symbolQuery, status: "loading", records: [] });
    const timer = window.setTimeout(() => {
      void searchSymbols(symbolQuery, { signal: controller.signal }).then((records) => {
        if (active) setSearch({ query: symbolQuery, status: "ready", records: records.slice(0, SYMBOL_SUGGESTION_LIMIT) });
      }).catch(() => {
        if (active) setSearch({ query: symbolQuery, status: "error", records: [] });
      });
    }, SYMBOL_SEARCH_DEBOUNCE_MS);
    return () => {
      active = false;
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [open, dismissed, searchSymbols, symbolQuery, retry]);

  useEffect(() => {
    if (listOpen && activeIndex >= 0) {
      suggestionsRef.current?.children[activeIndex]?.scrollIntoView?.({ block: "nearest" });
    }
  }, [activeIndex, listOpen]);

  const chooseSymbol = (record: SymbolRecord) => {
    setSymbol(record.symbol);
    setDismissed(true);
    setActiveIndex(-1);
    symbolRef.current?.focus();
  };
  const apply = () => {
    const next = {
      symbol: normalizeOperatingContextSymbol(symbol),
      fundAccountId: fundAccountId.trim() || null,
      provider: provider.trim() || null,
      runId: retained.runId ?? null,
      from: retained.from ?? null,
      to: retained.to ?? null,
      date: retained.date ?? null,
      asOf: retained.asOf ?? null
    };
    onApply(Object.values(next).some(Boolean) ? next : {});
    onOpenChange(false);
  };
  const clearAll = () => {
    setSymbol("");
    setFundAccountId("");
    setProvider("");
    setRetained({});
    setClearStaged(true);
    setDismissed(true);
    setActiveIndex(-1);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent aria-labelledby={`${id}-title`} aria-describedby={`${id}-description`}>
        <DialogHeader>
          <DialogTitle id={`${id}-title`}>Operating scope</DialogTitle>
          <DialogDescription id={`${id}-description`}>
            Set the subject, fund account, and provider carried across the workstation. Each screen applies the dimensions it supports. Changes take effect when you apply.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3 pt-2">
          <div>
            <label className="text-sm font-medium text-foreground" htmlFor={`${id}-symbol`}>Subject symbol</label>
            <Input
              ref={symbolRef}
              id={`${id}-symbol`}
              role="combobox"
              aria-autocomplete="list"
              aria-expanded={listOpen}
              aria-controls={listOpen ? suggestionsId : undefined}
              aria-activedescendant={listOpen && activeIndex >= 0 ? `${id}-option-${activeIndex}` : undefined}
              aria-describedby={`${id}-symbol-help ${id}-search-status`}
              autoComplete="off"
              placeholder="e.g. AAPL"
              value={symbol}
              onChange={(event) => { setSymbol(event.target.value); setDismissed(false); setActiveIndex(-1); }}
              onFocus={() => { setSymbolFocused(true); setDismissed(false); }}
              onBlur={() => { setSymbolFocused(false); setActiveIndex(-1); }}
              onKeyDown={(event) => {
                if ((event.key === "ArrowDown" || event.key === "ArrowUp") && listOpen) {
                  event.preventDefault();
                  setActiveIndex((index) => event.key === "ArrowDown"
                    ? (index + 1) % suggestions.length
                    : (index <= 0 ? suggestions.length : index) - 1);
                } else if (event.key === "Enter" && listOpen && activeIndex >= 0) {
                  event.preventDefault();
                  chooseSymbol(suggestions[activeIndex]);
                } else if (event.key === "Escape" && listOpen) {
                  event.preventDefault();
                  event.stopPropagation();
                  setDismissed(true);
                  setActiveIndex(-1);
                }
              }}
            />
            <p id={`${id}-symbol-help`} className="mt-1 text-xs text-muted-foreground">{applicability("symbol")}. You can also enter a symbol manually.</p>
            <p id={`${id}-search-status`} role="status" className="mt-1 text-xs text-muted-foreground">
              {!dismissed && symbolQuery.length >= SYMBOL_SEARCH_MIN_LENGTH ? (
                currentSearch.status === "loading" ? "Searching symbols…" :
                currentSearch.status === "error" ? "Symbol search failed. Your input is retained." :
                currentSearch.status === "ready" ? suggestions.length > 0 ? `${suggestions.length} symbol suggestions available.` : "No matching symbols. You can enter a symbol manually." : null
              ) : null}
            </p>
            {!dismissed && currentSearch.status === "error" && symbolQuery.length >= SYMBOL_SEARCH_MIN_LENGTH ? (
              <Button type="button" size="sm" variant="outline" onClick={() => setRetry((value) => value + 1)}>Retry symbol search</Button>
            ) : null}
            {listOpen ? (
              <ul ref={suggestionsRef} id={suggestionsId} role="listbox" className="mt-1 max-h-40 overflow-y-auto rounded-md border border-border/70 bg-background" aria-label="Symbol suggestions">
                {suggestions.map((record, index) => (
                  <li
                    key={`${record.symbol}-${record.provider ?? ""}`}
                    id={`${id}-option-${index}`}
                    role="option"
                    aria-selected={activeIndex === index}
                    className={`flex cursor-pointer items-center justify-between gap-2 px-3 py-1.5 text-sm hover:bg-secondary/60 ${activeIndex === index ? "bg-secondary" : ""}`}
                    onMouseDown={(event) => event.preventDefault()}
                    onClick={() => chooseSymbol(record)}
                  >
                    <span className="font-mono">{record.symbol}</span>
                    {record.provider ? <span className="text-xs text-muted-foreground">{record.provider}</span> : null}
                  </li>
                ))}
              </ul>
            ) : null}
          </div>
          <div>
            <label className="text-sm font-medium text-foreground" htmlFor={`${id}-fund-account`}>Fund account</label>
            <Input id={`${id}-fund-account`} autoComplete="off" list={fundAccounts.length > 0 ? `${id}-fund-accounts` : undefined} aria-describedby={`${id}-fund-help`} placeholder="Fund account id" value={fundAccountId} onChange={(event) => setFundAccountId(event.target.value)} />
            <p id={`${id}-fund-help`} className="mt-1 text-xs text-muted-foreground">{applicability("fundAccountId")}</p>
            {fundAccounts.length > 0 ? <datalist id={`${id}-fund-accounts`}>{fundAccounts.map((account) => <option key={account.id} value={account.id}>{account.label}</option>)}</datalist> : null}
          </div>
          <div>
            <label className="text-sm font-medium text-foreground" htmlFor={`${id}-provider`}>Provider</label>
            <Input id={`${id}-provider`} autoComplete="off" aria-describedby={`${id}-provider-help`} placeholder="e.g. alpaca" value={provider} onChange={(event) => setProvider(event.target.value)} />
            <p id={`${id}-provider-help`} className="mt-1 text-xs text-muted-foreground">{applicability("provider")}</p>
          </div>
          {RETAINED_FIELDS.some(([key]) => retained[key]) ? (
            <section aria-label="Retained scope" className="rounded-sm border border-border p-3">
              <h3 className="text-sm font-medium">Retained scope</h3>
              <p className="mt-1 text-xs text-muted-foreground">Read-only here. Use the originating workflow to change these values.</p>
              <dl className="mt-2 space-y-2 text-xs">
                {RETAINED_FIELDS.filter(([key]) => retained[key]).map(([key, label, dimension]) => (
                  <div key={key} className="grid grid-cols-[4rem_minmax(0,1fr)] gap-x-2">
                    <dt className="text-muted-foreground">{label}</dt>
                    <dd className="break-words"><span className="font-mono">{retained[key]}</span><span className="mt-0.5 block text-muted-foreground">{applicability(dimension)}</span></dd>
                  </div>
                ))}
              </dl>
            </section>
          ) : null}
          {clearStaged ? <p role="status" className="text-sm text-muted-foreground">Scope reset staged, including date and run context. Apply scope to save these changes, or Cancel to keep the current scope.</p> : null}
          <div className="flex items-center justify-between gap-3 pt-2">
            <Button type="button" size="sm" variant="ghost" onClick={clearAll}>Clear scope</Button>
            <div className="flex items-center gap-2">
              <Button type="button" size="sm" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
              <Button type="button" size="sm" onClick={apply}>Apply scope</Button>
            </div>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

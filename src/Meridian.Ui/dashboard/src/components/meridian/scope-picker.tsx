import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { searchSymbolsQuery } from "@/lib/api";
import { normalizeOperatingContextSymbol, type AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import type { ScopeFundAccountOption } from "@/lib/operating-scope/fund-accounts";
import type { SymbolRecord } from "@/types";

const SYMBOL_SEARCH_DEBOUNCE_MS = 250;
const SYMBOL_SEARCH_MIN_LENGTH = 2;
const SYMBOL_SUGGESTION_LIMIT = 8;

export interface ScopePickerProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  scope: AppShellOperatingScopeInput;
  fundAccounts: ScopeFundAccountOption[];
  onApply: (next: AppShellOperatingScopeInput) => void;
  /** Overridable for tests. */
  searchSymbols?: typeof searchSymbolsQuery;
}

/**
 * Editable operating-scope picker. Sets the sticky scope dimensions an operator
 * carries across the workstation: subject symbol (live search), fund account
 * (typed, with discovered suggestions), and provider. Apply persists + reflects
 * into the URL via the caller; the picker itself only collects the values.
 */
export function ScopePicker({ open, onOpenChange, scope, fundAccounts, onApply, searchSymbols = searchSymbolsQuery }: ScopePickerProps) {
  const [symbol, setSymbol] = useState("");
  const [fundAccountId, setFundAccountId] = useState("");
  const [provider, setProvider] = useState("");
  const [symbolSuggestions, setSymbolSuggestions] = useState<SymbolRecord[]>([]);
  const fundAccountListId = useId();

  // Seed the fields from the active scope each time the dialog opens.
  useEffect(() => {
    if (!open) {
      return;
    }
    setSymbol(scope.symbol ?? "");
    setFundAccountId(scope.fundAccountId ?? "");
    setProvider(scope.provider ?? "");
    setSymbolSuggestions([]);
  }, [open, scope.symbol, scope.fundAccountId, scope.provider]);

  const symbolQuery = symbol.trim();
  const suggestionsRef = useRef(setSymbolSuggestions);
  suggestionsRef.current = setSymbolSuggestions;
  useEffect(() => {
    if (!open || symbolQuery.length < SYMBOL_SEARCH_MIN_LENGTH) {
      suggestionsRef.current([]);
      return;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      void searchSymbols(symbolQuery, { signal: controller.signal })
        .then((records) => suggestionsRef.current(records.slice(0, SYMBOL_SUGGESTION_LIMIT)))
        .catch(() => undefined);
    }, SYMBOL_SEARCH_DEBOUNCE_MS);

    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [open, searchSymbols, symbolQuery]);

  const apply = () => {
    // Emit the complete next scope: this dialog's fields plus the dimensions it
    // doesn't edit, so the caller can treat it as a replace.
    onApply({
      symbol: normalizeOperatingContextSymbol(symbol),
      fundAccountId: fundAccountId.trim() || null,
      provider: provider.trim() || null,
      runId: scope.runId ?? null,
      from: scope.from ?? null,
      to: scope.to ?? null,
      date: scope.date ?? null,
      asOf: scope.asOf ?? null
    });
    onOpenChange(false);
  };

  const clearAll = () => {
    onApply({});
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent aria-labelledby="scope-picker-title" aria-describedby="scope-picker-description">
        <DialogHeader>
          <DialogTitle id="scope-picker-title">Operating scope</DialogTitle>
          <DialogDescription id="scope-picker-description">
            Set the subject, fund account, and provider carried across the workstation. Each screen
            applies the dimensions it supports.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3 pt-2">
          <div>
            <label className="text-sm font-medium text-foreground" htmlFor="scope-picker-symbol">
              Subject symbol
            </label>
            <Input
              id="scope-picker-symbol"
              autoComplete="off"
              placeholder="e.g. AAPL"
              value={symbol}
              onChange={(event) => setSymbol(event.target.value)}
            />
            {symbolSuggestions.length > 0 ? (
              <ul className="mt-1 max-h-40 overflow-y-auto rounded-md border border-border/70 bg-background" aria-label="Symbol suggestions">
                {symbolSuggestions.map((record) => (
                  <li key={record.symbol}>
                    <button
                      type="button"
                      className="flex w-full items-center justify-between gap-2 px-3 py-1.5 text-left text-sm hover:bg-secondary/60 focus-visible:outline-none focus-visible:bg-secondary/60"
                      onClick={() => {
                        setSymbol(record.symbol);
                        setSymbolSuggestions([]);
                      }}
                    >
                      <span className="font-mono">{record.symbol}</span>
                      {record.provider ? <span className="text-xs text-muted-foreground">{record.provider}</span> : null}
                    </button>
                  </li>
                ))}
              </ul>
            ) : null}
          </div>

          <div>
            <label className="text-sm font-medium text-foreground" htmlFor="scope-picker-fund-account">
              Fund account
            </label>
            <Input
              id="scope-picker-fund-account"
              autoComplete="off"
              list={fundAccounts.length > 0 ? fundAccountListId : undefined}
              placeholder="Fund account id"
              value={fundAccountId}
              onChange={(event) => setFundAccountId(event.target.value)}
            />
            {fundAccounts.length > 0 ? (
              <datalist id={fundAccountListId}>
                {fundAccounts.map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.label}
                  </option>
                ))}
              </datalist>
            ) : null}
          </div>

          <div>
            <label className="text-sm font-medium text-foreground" htmlFor="scope-picker-provider">
              Provider
            </label>
            <Input
              id="scope-picker-provider"
              autoComplete="off"
              placeholder="e.g. alpaca"
              value={provider}
              onChange={(event) => setProvider(event.target.value)}
            />
          </div>

          <div className="flex items-center justify-between gap-3 pt-2">
            <Button type="button" size="sm" variant="ghost" onClick={clearAll}>
              Clear scope
            </Button>
            <div className="flex items-center gap-2">
              <Button type="button" size="sm" variant="outline" onClick={() => onOpenChange(false)}>
                Cancel
              </Button>
              <Button type="button" size="sm" onClick={apply}>
                Apply scope
              </Button>
            </div>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

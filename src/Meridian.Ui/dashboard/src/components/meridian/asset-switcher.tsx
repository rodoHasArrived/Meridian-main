import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { searchSecurities } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { SecurityMasterEntry } from "@/types";

export interface AssetSwitchTarget {
  securityId: string;
  symbol: string;
  displayName: string;
}

export interface AssetSwitcherProps extends AssetSwitchTarget {
  onSelect: (target: AssetSwitchTarget) => void;
}

const recentAssetsKey = "meridian.asset-detail.recent.v1";
const recentLimit = 6;

function isTarget(value: unknown): value is AssetSwitchTarget {
  if (!value || typeof value !== "object") return false;
  const target = value as Record<string, unknown>;
  return ["securityId", "symbol", "displayName"].every(
    (key) => typeof target[key] === "string" && target[key].trim().length > 0 && target[key].length <= 256
  );
}

function readRecentAssets(): AssetSwitchTarget[] {
  try {
    const raw = window.localStorage.getItem(recentAssetsKey);
    if (!raw || raw.length > 10_000) return [];
    const stored: unknown = JSON.parse(raw);
    if (
      !stored ||
      typeof stored !== "object" ||
      !("version" in stored) ||
      stored.version !== 1 ||
      !("items" in stored) ||
      !Array.isArray(stored.items)
    )
      return [];
    const seen = new Set<string>();
    return stored.items
      .filter((item): item is AssetSwitchTarget => {
        if (!isTarget(item) || seen.has(item.securityId)) return false;
        seen.add(item.securityId);
        return true;
      })
      .slice(0, recentLimit);
  } catch {
    return [];
  }
}

function writeRecentAssets(items: AssetSwitchTarget[]) {
  try {
    window.localStorage.setItem(recentAssetsKey, JSON.stringify({ version: 1, items }));
  } catch {
    // Navigation remains available when browser storage is unavailable.
  }
}

function targetFromEntry(entry: SecurityMasterEntry): AssetSwitchTarget {
  return {
    securityId: entry.securityId,
    symbol: entry.classification.primaryIdentifierValue ?? entry.securityId,
    displayName: entry.displayName
  };
}

interface SearchState {
  query: string;
  status: "idle" | "loading" | "ready" | "error";
  items: AssetSwitchTarget[];
}

/** Asset navigation only; recent history never substitutes for the selected asset's server record. */
export function AssetSwitcher({ securityId, symbol, displayName, onSelect }: AssetSwitcherProps) {
  const id = useId();
  const [query, setQuery] = useState("");
  const [expanded, setExpanded] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const [recent, setRecent] = useState(readRecentAssets);
  const [retry, setRetry] = useState(0);
  const [search, setSearch] = useState<SearchState>({
    query: "",
    status: "idle",
    items: []
  });
  const requestRef = useRef(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const trimmedQuery = query.trim();
  const searching = trimmedQuery.length >= 2;
  const currentSearch = search.query === trimmedQuery ? search : { query: trimmedQuery, status: "loading", items: [] };
  const options = searching ? currentSearch.items : recent.filter((item) => item.securityId !== securityId);
  const listId = `${id}-options`;
  const helpId = `${id}-help`;

  useEffect(() => {
    if (expanded && activeIndex >= 0) {
      document.getElementById(`${id}-option-${activeIndex}`)?.scrollIntoView?.({ block: "nearest" });
    }
  }, [activeIndex, expanded, id]);

  useEffect(() => {
    const next = [
      { securityId, symbol, displayName },
      ...readRecentAssets().filter((item) => item.securityId !== securityId)
    ].slice(0, recentLimit);
    writeRecentAssets(next);
    setRecent(next);
    setQuery("");
    setExpanded(false);
    setActiveIndex(-1);
  }, [securityId, symbol, displayName]);

  useEffect(() => {
    const generation = ++requestRef.current;
    if (trimmedQuery.length < 2) {
      setSearch({ query: trimmedQuery, status: "idle", items: [] });
      return;
    }
    const controller = new AbortController();
    setSearch({ query: trimmedQuery, status: "loading", items: [] });
    const timer = window.setTimeout(() => {
      searchSecurities(trimmedQuery, 12, false, {
        signal: controller.signal
      })
        .then((entries) => {
          if (controller.signal.aborted || generation !== requestRef.current) return;
          const seen = new Set<string>();
          const items = entries.map(targetFromEntry).filter((item) => {
            if (item.securityId === securityId || seen.has(item.securityId)) return false;
            seen.add(item.securityId);
            return true;
          });
          setSearch({ query: trimmedQuery, status: "ready", items });
        })
        .catch(() => {
          if (!controller.signal.aborted && generation === requestRef.current) {
            setSearch({
              query: trimmedQuery,
              status: "error",
              items: []
            });
          }
        });
    }, 250);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [trimmedQuery, retry, securityId]);

  const select = (target: AssetSwitchTarget) => {
    setExpanded(false);
    setActiveIndex(-1);
    setQuery("");
    onSelect(target);
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      setExpanded(false);
      setActiveIndex(-1);
      return;
    }
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      setExpanded(true);
      if (options.length) {
        setActiveIndex((current) =>
          event.key === "ArrowDown" ? (current + 1) % options.length : (current <= 0 ? options.length : current) - 1
        );
      }
    } else if (event.key === "Enter" && expanded && activeIndex >= 0 && options[activeIndex]) {
      event.preventDefault();
      select(options[activeIndex]);
    }
  };

  return (
    <div
      className="relative w-full max-w-sm"
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          setExpanded(false);
          setActiveIndex(-1);
        }
      }}
    >
      <Input
        ref={inputRef}
        role="combobox"
        aria-label="Switch asset"
        aria-autocomplete="list"
        aria-expanded={expanded}
        aria-controls={expanded ? listId : undefined}
        aria-activedescendant={expanded && options[activeIndex] ? `${id}-option-${activeIndex}` : undefined}
        aria-describedby={expanded ? helpId : undefined}
        autoComplete="off"
        leadingIcon={<Search className="h-4 w-4" />}
        placeholder="Switch asset by name or identifier"
        value={query}
        onChange={(event) => {
          setQuery(event.target.value);
          setExpanded(true);
          setActiveIndex(-1);
        }}
        onFocus={() => setExpanded(true)}
        onClick={() => setExpanded(true)}
        onKeyDown={handleKeyDown}
      />
      {expanded ? (
        <div className="absolute left-0 right-0 top-full z-30 mt-1 border border-border bg-background p-2 shadow-lg">
          <div className="mb-1 flex items-center justify-between gap-2">
            <p id={helpId} className="text-xs text-muted-foreground">
              {searching ? "Search results · ↑ ↓ to choose, Enter to open" : "Recent assets on this browser"}
            </p>
            {!searching && options.length ? (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={() => {
                  writeRecentAssets([]);
                  setRecent([]);
                  setActiveIndex(-1);
                  inputRef.current?.focus();
                }}
              >
                Clear history
              </Button>
            ) : null}
          </div>
          <ul
            id={listId}
            role="listbox"
            aria-label={searching ? "Asset search results" : "Recent assets"}
            className="max-h-64 overflow-auto"
          >
            {options.map((item, index) => (
              <li key={item.securityId} role="presentation">
                <button
                  id={`${id}-option-${index}`}
                  type="button"
                  role="option"
                  aria-selected={index === activeIndex}
                  tabIndex={-1}
                  className={cn(
                    "block w-full border-l-2 border-transparent px-2 py-2 text-left hover:bg-secondary/40",
                    index === activeIndex && "border-primary bg-secondary/40"
                  )}
                  onMouseDown={(event) => event.preventDefault()}
                  onClick={() => select(item)}
                >
                  <span className="block text-sm font-semibold">{item.displayName}</span>
                  <span className="block break-all font-mono text-xs text-muted-foreground">{item.symbol}</span>
                </button>
              </li>
            ))}
          </ul>
          {searching && currentSearch.status === "loading" ? (
            <p role="status" className="px-2 py-2 text-sm text-muted-foreground">
              Searching assets…
            </p>
          ) : null}
          {searching && currentSearch.status === "error" ? (
            <div className="flex items-center justify-between gap-2 px-2 py-2">
              <p role="alert" className="text-sm">
                Couldn’t search assets.
              </p>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => {
                  setRetry((value) => value + 1);
                  inputRef.current?.focus();
                }}
              >
                Retry search
              </Button>
            </div>
          ) : null}
          {searching && currentSearch.status === "ready" && !options.length ? (
            <p role="status" className="px-2 py-2 text-sm text-muted-foreground">
              No other assets match “{trimmedQuery}”.
            </p>
          ) : null}
          {!searching && !options.length ? (
            <p className="px-2 py-2 text-sm text-muted-foreground">
              Enter at least two characters to find another asset.
            </p>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

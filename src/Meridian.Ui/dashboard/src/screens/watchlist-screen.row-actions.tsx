import { Copy, Eye, LineChart, MoreHorizontal, Trash2 } from "lucide-react";
import {
  useCallback,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent,
  type ReactNode
} from "react";
import {
  ContextMenu,
  useContextMenu,
  type ContextMenuEntry,
  type ContextMenuPosition
} from "@/components/ui/context-menu";
import { copyTextToClipboard } from "@/lib/csv";
import { useToast, type ToastApi } from "@/components/ui/toast";
import { cn } from "@/lib/utils";
import type { WatchlistRowViewModel } from "@/screens/watchlist-screen.view-model";

/**
 * Row-level context actions for the symbol watchlist: right-click (or the
 * keyboard/hover-revealed "⋯" affordance) on a subscribed-symbol row surfaces
 * the operations that live on that symbol — inspect, open live quote, copy the
 * ticker, and remove — without hunting through the inline button strip. The
 * menu reuses the shared {@link ContextMenu} primitive and mirrors the Data
 * screen's cell-actions pattern so both surfaces feel identical.
 */

/** Stable identifiers for every row action, shared by the builder, hook, and tests. */
export type WatchlistRowActionId = "inspect" | "open-quote" | "copy-symbol" | "remove";

/** Minimal projection of a row needed to derive its menu entries. */
export interface WatchlistRowActionContext {
  symbol: string;
  isRemoving: boolean;
}

export interface BuildWatchlistRowActionsOptions {
  /** Disable the destructive remove while another mutation for the row is in flight. */
  busy?: boolean;
}

/**
 * Pure projection of a watchlist row onto its context-menu entries. Every
 * actionable item routes back through {@link onAction}; the hook decides what
 * each id does.
 */
export function buildWatchlistRowActions(
  row: WatchlistRowActionContext,
  onAction: (actionId: WatchlistRowActionId) => void,
  options: BuildWatchlistRowActionsOptions = {}
): ContextMenuEntry[] {
  const busy = options.busy ?? false;
  const item = (
    id: WatchlistRowActionId,
    label: ReactNode,
    icon: ReactNode,
    extra: { danger?: boolean; disabled?: boolean } = {}
  ): ContextMenuEntry => ({
    id,
    type: "item",
    label,
    icon,
    danger: extra.danger,
    disabled: extra.disabled,
    onSelect: () => onAction(id)
  });

  return [
    item("inspect", "Inspect symbol", <Eye className="h-4 w-4" aria-hidden="true" />),
    item("open-quote", "Open live quote", <LineChart className="h-4 w-4" aria-hidden="true" />),
    item("copy-symbol", "Copy symbol", <Copy className="h-4 w-4" aria-hidden="true" />),
    { id: "watchlist-row-divider", type: "divider" },
    item("remove", `Remove ${row.symbol}`, <Trash2 className="h-4 w-4" aria-hidden="true" />, {
      danger: true,
      disabled: busy || row.isRemoving
    })
  ];
}

export interface UseWatchlistRowActionsOptions {
  /** Select the symbol (opens the detail/inspector panel). */
  onInspect: (symbol: string) => void;
  /** Open the live quote view for the row. */
  onOpenQuote: (row: WatchlistRowViewModel) => void;
  /** Remove the symbol from the watchlist. */
  onRemove: (symbol: string) => void;
  /** Injectable clipboard writer; defaults to {@link copyTextToClipboard}. */
  copySymbol?: (symbol: string) => Promise<boolean>;
  /** Injectable toast surface; defaults to the app toast (no-op without a provider). */
  toast?: ToastApi;
}

export interface UseWatchlistRowActionsResult {
  /** Open the context menu for a row from a right-click or the "⋯" trigger. */
  openFor: (
    event: ReactMouseEvent<HTMLElement> | ReactKeyboardEvent<HTMLElement>,
    row: WatchlistRowViewModel
  ) => void;
  /** Menu wiring to spread onto {@link ContextMenu}. */
  menu: {
    open: boolean;
    position: ContextMenuPosition | null;
    items: ContextMenuEntry[];
    close: () => void;
    label: string;
  };
  /** Execute a known action directly (used by the inline affordance / tests). */
  run: (row: WatchlistRowViewModel, actionId: WatchlistRowActionId) => void;
}

/**
 * Shared row-action hook for the watchlist. Owns a single context menu whose
 * entries are derived from the currently targeted row and dispatches each
 * action back to the owning screen (inspect / quote / remove) or handles the
 * clipboard copy inline with toast feedback.
 */
export function useWatchlistRowActions(options: UseWatchlistRowActionsOptions): UseWatchlistRowActionsResult {
  // Keep the latest callbacks in a ref so the memoized dispatchers below stay
  // stable across renders even though the parent recreates these handlers.
  const optionsRef = useRef(options);
  optionsRef.current = options;
  const copySymbol = options.copySymbol ?? copyTextToClipboard;
  const fallbackToast = useToast();
  const toast = options.toast ?? fallbackToast;
  const menu = useContextMenu();
  const { onContextMenu, openAt } = menu;
  const [row, setRow] = useState<WatchlistRowViewModel | null>(null);

  const openFor = useCallback(
    (
      event: ReactMouseEvent<HTMLElement> | ReactKeyboardEvent<HTMLElement>,
      target: WatchlistRowViewModel
    ) => {
      setRow(target);
      if ("clientX" in event && (event.clientX !== 0 || event.clientY !== 0)) {
        onContextMenu(event as ReactMouseEvent<HTMLElement>);
        return;
      }
      // Keyboard / synthetic activation: anchor to the triggering element instead of (0,0).
      event.preventDefault();
      const rect = event.currentTarget.getBoundingClientRect();
      openAt({ x: rect.left, y: rect.bottom + 4 });
    },
    [onContextMenu, openAt]
  );

  const runFor = useCallback(
    (target: WatchlistRowViewModel, actionId: WatchlistRowActionId) => {
      const { onInspect, onOpenQuote, onRemove } = optionsRef.current;
      switch (actionId) {
        case "inspect":
          onInspect(target.symbol);
          return;
        case "open-quote":
          onOpenQuote(target);
          return;
        case "copy-symbol":
          // copyTextToClipboard resolves false rather than rejecting, but a custom
          // injected writer might reject; guard so a rejection can't go unhandled.
          void copySymbol(target.symbol)
            .then((copied) => {
              if (copied) {
                toast.success(`Copied ${target.symbol}`);
              } else {
                toast.danger(`Could not copy ${target.symbol}`, "Clipboard access was blocked.");
              }
            })
            .catch((error: unknown) => {
              toast.danger(
                `Could not copy ${target.symbol}`,
                error instanceof Error ? error.message : "Clipboard access was blocked."
              );
            });
          return;
        case "remove":
          onRemove(target.symbol);
          return;
        default:
          return;
      }
    },
    [copySymbol, toast]
  );

  const dispatch = useCallback(
    (actionId: WatchlistRowActionId) => {
      if (row) runFor(row, actionId);
    },
    [row, runFor]
  );

  const items = row
    ? buildWatchlistRowActions(row, dispatch, { busy: row.isRemoving })
    : [];

  return {
    openFor,
    menu: {
      open: menu.open,
      position: menu.position,
      items,
      close: menu.closeMenu,
      label: "Watchlist row actions"
    },
    run: runFor
  };
}

/**
 * The keyboard-and-hover-revealed "⋯" affordance that pairs with the invisible
 * right-click gesture. Lives inside a `group` row; stays quiet until the row is
 * hovered or the trigger is focused.
 */
export function WatchlistRowActionsTrigger({
  onOpen,
  label,
  className
}: {
  onOpen: (event: ReactMouseEvent<HTMLElement> | ReactKeyboardEvent<HTMLElement>) => void;
  label: string;
  className?: string;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      aria-haspopup="menu"
      onClick={onOpen}
      className={cn(
        "inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-[2px] text-muted-foreground transition-colors",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 hover:text-foreground",
        className
      )}
    >
      <MoreHorizontal className="h-4 w-4" aria-hidden="true" />
    </button>
  );
}

/** Re-export for consumers rendering the menu inline. */
export { ContextMenu };

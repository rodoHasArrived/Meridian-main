import {
  CheckCircle2,
  DatabaseZap,
  MoreHorizontal,
  ShieldCheck,
  Table2,
  Trash2
} from "lucide-react";
import {
  useCallback,
  useMemo,
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
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { acknowledgeAnomaly, archiveSymbol, remediateQualityGap, triggerBackfill } from "@/lib/api";
import { useActivityLog } from "@/lib/activity-log/store";
import type { ActivityEntryInput } from "@/lib/activity-log/types";
import { describeApiError } from "@/lib/api-errors";
import type { ToastApi } from "@/components/ui/toast";
import { cn } from "@/lib/utils";

/**
 * Contextual actions on the data itself: right-click (or the hover-revealed "⋯" affordance) on a
 * coverage-gap row or an anomaly surfaces the operations that live on that datum — backfill,
 * exclude, master, acknowledge, compare — routed through the workstation endpoint layer rather
 * than a separate jobs screen three clicks away.
 */

/** A red coverage-gap cell: an active symbol with no security-master record. */
export interface CoverageGapCellContext {
  kind: "coverage-gap";
  symbol: string;
  sourcesLabel: string;
}

/** An anomalous tick flagged by the quality monitor. */
export interface AnomalyCellContext {
  kind: "anomaly";
  symbol: string;
  anomalyId: string;
  anomalyType: string;
}

/** A server-resolved open quality gap from the canonical composite dashboard. */
export interface QualityGapCellContext {
  kind: "quality-gap";
  symbol: string;
  gapId: string;
  dashboardVersion: string;
  provider: string | null;
  from: string;
  to: string;
  canBackfill: boolean;
  disabledReason: string | null;
}

export type CellActionContext = CoverageGapCellContext | AnomalyCellContext | QualityGapCellContext;

/** Stable identifiers for every contextual action, shared by the builder, hook, and tests. */
export type CellActionId =
  | "backfill"
  | "master"
  | "exclude"
  | "acknowledge"
  | "compare-providers"
  | "open-capability-matrix";

export interface BuildCellActionsOptions {
  /** Disable mutating actions while another action for the same context is in flight. */
  busy?: boolean;
}

/**
 * Pure projection of a cell context onto its context-menu entries. Every actionable item routes
 * back through {@link onAction}; the hook decides what each id does (direct call vs confirm gate).
 */
export function buildCellActions(
  context: CellActionContext,
  onAction: (actionId: CellActionId) => void,
  options: BuildCellActionsOptions = {}
): ContextMenuEntry[] {
  const busy = options.busy ?? false;
  const item = (
    id: CellActionId,
    label: string,
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

  if (context.kind === "coverage-gap") {
    return [
      item("backfill", "Backfill this range", <DatabaseZap className="h-4 w-4" aria-hidden="true" />, { disabled: busy }),
      item("master", "Master this symbol", <ShieldCheck className="h-4 w-4" aria-hidden="true" />, { disabled: busy }),
      { id: "coverage-divider", type: "divider" },
      item("open-capability-matrix", "Open provider capability matrix", <Table2 className="h-4 w-4" aria-hidden="true" />),
      { id: "coverage-danger-divider", type: "divider" },
      item("exclude", "Exclude symbol", <Trash2 className="h-4 w-4" aria-hidden="true" />, { danger: true, disabled: busy })
    ];
  }

  if (context.kind === "quality-gap") {
    return [
      item("backfill", "Backfill this exact gap", <DatabaseZap className="h-4 w-4" aria-hidden="true" />, {
        disabled: busy || !context.canBackfill
      }),
      item("compare-providers", "Compare across providers", <Table2 className="h-4 w-4" aria-hidden="true" />),
      { id: "quality-gap-divider", type: "divider" },
      item("open-capability-matrix", "Open provider capability matrix", <Table2 className="h-4 w-4" aria-hidden="true" />)
    ];
  }

  return [
    item("acknowledge", "Flag as acknowledged", <CheckCircle2 className="h-4 w-4" aria-hidden="true" />, { disabled: busy }),
    item("backfill", "Backfill symbol", <DatabaseZap className="h-4 w-4" aria-hidden="true" />, { disabled: busy }),
    item("compare-providers", "Compare across providers", <Table2 className="h-4 w-4" aria-hidden="true" />),
    { id: "anomaly-divider", type: "divider" },
    item("open-capability-matrix", "Open provider capability matrix", <Table2 className="h-4 w-4" aria-hidden="true" />)
  ];
}

/** A pending destructive action awaiting operator confirmation. */
interface CellActionConfirmState {
  title: string;
  description: string;
  confirmLabel: string;
  run: () => Promise<void>;
}

export interface CellActionApi {
  triggerBackfill: typeof triggerBackfill;
  remediateQualityGap: typeof remediateQualityGap;
  archiveSymbol: typeof archiveSymbol;
  acknowledgeAnomaly: typeof acknowledgeAnomaly;
}

const defaultCellActionApi: CellActionApi = {
  triggerBackfill,
  remediateQualityGap,
  archiveSymbol,
  acknowledgeAnomaly
};

export interface UseCellActionsOptions {
  toast: ToastApi;
  /** Reuse the coverage-gaps draft flow for "Master this symbol". */
  onMasterSymbol?: (symbol: string) => void;
  /** Navigate/scroll to the provider capability matrix, optionally focused on a symbol. */
  onOpenCapabilityMatrix?: (symbol: string) => void;
  /** Re-fetch the owning panel after a successful mutation. */
  onAfterMutation?: () => void;
  /** Injectable endpoint surface for tests. */
  api?: Partial<CellActionApi>;
}

export interface UseCellActionsResult {
  /** Open the context menu for a cell from a right-click or the hover-revealed trigger. */
  openFor: (
    event: ReactMouseEvent<HTMLElement> | ReactKeyboardEvent<HTMLElement>,
    context: CellActionContext
  ) => void;
  /** Menu wiring to spread onto {@link ContextMenu}. */
  menu: {
    open: boolean;
    position: ContextMenuPosition | null;
    items: ContextMenuEntry[];
    close: () => void;
    label: string;
  };
  /** Pending destructive confirmation, or null. */
  confirm: CellActionConfirmState | null;
  resolveConfirm: (accept: boolean) => void;
  /** True while a mutating action is in flight. */
  running: boolean;
  /** Execute a known action directly for an accessible inline affordance. */
  run: (context: CellActionContext, actionId: CellActionId) => void;
}

/**
 * Shared contextual-action hook for the Data screen. Owns a single context menu whose entries are
 * derived from the currently targeted cell, executes mutating actions through the workstation
 * endpoint layer, gates destructive actions behind a confirm dialog, and reports outcomes on toast.
 */
export function useCellActions(options: UseCellActionsOptions): UseCellActionsResult {
  const { toast, onMasterSymbol, onOpenCapabilityMatrix, onAfterMutation } = options;
  const optionsApi = options.api;
  const api = useMemo<CellActionApi>(() => ({ ...defaultCellActionApi, ...optionsApi }), [optionsApi]);
  const activityLog = useActivityLog();
  const menu = useContextMenu();
  const [context, setContext] = useState<CellActionContext | null>(null);
  const [running, setRunning] = useState(false);
  const [confirm, setConfirm] = useState<CellActionConfirmState | null>(null);

  const openFor = useCallback(
    (
      event: ReactMouseEvent<HTMLElement> | ReactKeyboardEvent<HTMLElement>,
      target: CellActionContext
    ) => {
      setContext(target);
      if ("clientX" in event && (event.clientX !== 0 || event.clientY !== 0)) {
        menu.onContextMenu(event as ReactMouseEvent<HTMLElement>);
        return;
      }
      // Keyboard / synthetic activation: anchor to the triggering element instead of (0,0).
      event.preventDefault();
      const rect = event.currentTarget.getBoundingClientRect();
      menu.openAt({ x: rect.left, y: rect.bottom + 4 });
    },
    [menu]
  );

  const runAction = useCallback(
    async (
      successTitle: string,
      fallback: string,
      effect: () => Promise<unknown>,
      activity?: { kind: string; tone?: ActivityEntryInput["tone"]; route?: string; routeLabel?: string; detail?: string }
    ) => {
      setRunning(true);
      try {
        await effect();
        toast.success(successTitle);
        if (activity) {
          activityLog.record({
            kind: activity.kind,
            title: successTitle,
            detail: activity.detail,
            tone: activity.tone ?? "success",
            route: activity.route,
            routeLabel: activity.routeLabel
          });
        }
        onAfterMutation?.();
      } catch (error) {
        toast.danger(fallback, describeApiError(error, fallback).summary);
      } finally {
        setRunning(false);
      }
    },
    [toast, onAfterMutation, activityLog]
  );

  const runFor = useCallback(
    (target: CellActionContext, actionId: CellActionId) => {
      const { symbol } = target;
      switch (actionId) {
        case "backfill":
          if (target.kind === "quality-gap") {
            void runAction(
              `Backfilled the ${symbol} quality gap`,
              `Could not backfill the selected gap for ${symbol}.`,
              () => api.remediateQualityGap(symbol, {
                gapId: target.gapId,
                dashboardVersion: target.dashboardVersion
              }),
              {
                kind: "quality.gap.remediate",
                route: "/data",
                routeLabel: "Data quality",
                detail: `${target.provider ?? "Default provider"}: ${target.from} to ${target.to}.`
              }
            );
            return;
          }
          void runAction(
            `Backfill queued for ${symbol}`,
            `Could not queue a backfill for ${symbol}.`,
            () => api.triggerBackfill({ provider: null, symbols: [symbol], from: null, to: null }),
            { kind: "data.backfill", route: "/data", routeLabel: "Data", detail: "Historical backfill requested." }
          );
          return;
        case "master":
          onMasterSymbol?.(symbol);
          return;
        case "acknowledge":
          if (target.kind === "anomaly") {
            const anomalyId = target.anomalyId;
            void runAction(
              `Acknowledged anomaly for ${symbol}`,
              `Could not acknowledge the anomaly for ${symbol}.`,
              () => api.acknowledgeAnomaly(anomalyId),
              { kind: "quality.anomaly.acknowledge", route: "/data", routeLabel: "Data" }
            );
          }
          return;
        case "compare-providers":
        case "open-capability-matrix":
          onOpenCapabilityMatrix?.(symbol);
          return;
        case "exclude":
          setConfirm({
            title: `Exclude ${symbol}?`,
            description: `${symbol} will be archived and dropped from coverage tracking. You can re-add it from the symbol registry.`,
            confirmLabel: "Exclude symbol",
            run: () =>
              runAction(
                `${symbol} excluded from coverage`,
                `Could not exclude ${symbol}.`,
                () => api.archiveSymbol(symbol),
                {
                  kind: "symbol.exclude",
                  tone: "warning",
                  route: "/data/watchlist",
                  routeLabel: "Re-add in watchlist",
                  detail: "Symbol archived and dropped from coverage tracking."
                }
              )
          });
          return;
        default:
          return;
      }
    },
    [api, runAction, onMasterSymbol, onOpenCapabilityMatrix]
  );

  const dispatch = useCallback(
    (actionId: CellActionId) => {
      if (context) runFor(context, actionId);
    },
    [context, runFor]
  );

  const resolveConfirm = useCallback(
    (accept: boolean) => {
      if (!confirm) {
        return;
      }
      if (!accept) {
        setConfirm(null);
        return;
      }
      // Keep the dialog mounted (showing its in-flight state) until the action settles.
      void confirm.run().finally(() => setConfirm(null));
    },
    [confirm]
  );

  const items = context ? buildCellActions(context, dispatch, { busy: running }) : [];

  return {
    openFor,
    menu: {
      open: menu.open,
      position: menu.position,
      items,
      close: menu.closeMenu,
      label: "Cell actions"
    },
    confirm,
    resolveConfirm,
    running,
    run: runFor
  };
}

/**
 * The hover/focus-revealed "⋯" affordance that pairs with the invisible right-click gesture. Lives
 * inside a `group` row; stays out of the way until the row is hovered or the trigger is focused.
 */
export function CellActionTrigger({
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
        "inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-[2px] text-muted-foreground transition-opacity",
        "opacity-0 focus-visible:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
        "group-hover:opacity-100 group-focus-within:opacity-100 hover:text-foreground",
        className
      )}
    >
      <MoreHorizontal className="h-4 w-4" aria-hidden="true" />
    </button>
  );
}

/** Confirmation gate for destructive contextual actions (e.g. excluding a symbol). */
export function CellActionConfirmDialog({
  confirm,
  running,
  onResolve
}: {
  confirm: CellActionConfirmState | null;
  running: boolean;
  onResolve: (accept: boolean) => void;
}) {
  return (
    <Dialog
      open={confirm !== null}
      onOpenChange={(open) => {
        // Ignore backdrop/Escape dismissal while the action is still running.
        if (!open && !running) {
          onResolve(false);
        }
      }}
    >
      {confirm ? (
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>{confirm.title}</DialogTitle>
            <DialogDescription>{confirm.description}</DialogDescription>
          </DialogHeader>
          <div className="mt-4 flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => onResolve(false)} disabled={running}>
              Cancel
            </Button>
            <Button type="button" variant="destructive" onClick={() => onResolve(true)} disabled={running}>
              {running ? "Working…" : confirm.confirmLabel}
            </Button>
          </div>
        </DialogContent>
      ) : null}
    </Dialog>
  );
}

/** Re-export for consumers rendering the menu inline. */
export { ContextMenu };

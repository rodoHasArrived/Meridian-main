// Meridian SelectionCompare — comparison by selection. Dashboard-native port of the design-system
// 4.3.0 `SelectionCompare` (components/data/SelectionCompare.jsx, release-candidate maturity: pin
// to the named props, not to class names). Two or more committed selections — table rows, figure
// series, ledger lines — are promoted into a PlateGrid from the screen itself. The tray keeps the
// candidate set visible while it is built and the sheet applies the comparison-plate contract:
// one shared domain, one tick set, plates never resized.
//
// Kept from the package: the set's state machine (empty → building → comparable → full), the cap
// (default 6) that REFUSES a further add in words rather than degrading, the tray / sheet / toggle
// vocabulary, `aria-pressed` toggles that never select the row they sit in, and the region /
// section naming.
//
// Adapted or added for the workstation (each is a deliberate, documented difference):
//  - styling uses dashboard tokens and 12px-or-larger type; nothing is injected at runtime;
//  - `onChange` fires only when the set really changes (the package also fires on a refused add);
//  - `onCompare` is wired (the package declares it and never calls it) and `onOpenChange` is new;
//  - `seriesFor` lets the sheet draw series that arrive AFTER an item joined the set, and shows an
//    item whose series is loading or unavailable as a plate that says so — never a silent drop;
//  - the sheet states its `basis` and carries a table of the plotted values as text alternative;
//  - `format` also receives the shared scale (domain, ticks, tick step), and the sheet uses that
//    one formatter for tick labels, end labels, figure names and the values table, so a host can
//    keep label precision at or below the tick step and the table never contradicts the picture;
//  - the sheet CLOSES when the set falls below two (cleared, or a member removed). The package
//    leaves it open-but-hidden, so it reappears by itself — and a lazy host reads series — as soon
//    as a second item is ticked. Here the sheet is only ever opened by the tray's action (or
//    `setOpen`), which keeps table rows from moving under the pointer while a set is being built;
//  - focus is never dropped to <body>: opening from the tray moves focus to the sheet, Close
//    returns it to the tray action, removing a chip moves it to the neighbouring chip (or the
//    tray), Clear moves it to the tray, and a sheet that closes while holding focus hands it to
//    the tray. A tray without `emptyLabel` unmounts when emptied and cannot keep focus;
//  - accessible names are overridable so a host screen can avoid clashing with its own commands.
//
// Not ported: the overlay sheet (`mode="overlay"`) and the corner-docked tray (`docked="corner"`).
// Both are fixed-position surfaces whose offsets and stacking assume the package shell.
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import type { ButtonHTMLAttributes, ReactNode, RefObject } from "react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { PlateGrid, plateDomain, plateScale } from "./ComparisonPlate";
import type { PlateRole, PlateScale, PlateSpec } from "./ComparisonPlate";

/** The scale the sheet draws on, handed to `format` with every value. */
export type CompareScale = PlateScale;
/** Formats a plotted value. `scale.step` is the distance between adjacent ticks of the shared scale. */
export type CompareValueFormat = (value: number, scale: CompareScale) => string;

/** The thing a row / series / ledger line becomes when it is compared. Identity is `id` only. */
export interface CompareItem {
  id: string;
  title: string;
  tag?: string;
  /**
   * The series to plot, already commensurable with the rest of the set (same basis, same number
   * of points as the categories). Optional here because a host may supply it later through the
   * sheet's `seriesFor`.
   */
  values?: ReadonlyArray<number | null>;
  categories?: readonly string[];
  role?: PlateRole;
  footerLabel?: string;
  footerValue?: string;
}

/** What the sheet knows about one item's series when the host resolves it lazily. */
export type CompareSeriesState =
  | { status: "ready"; values: ReadonlyArray<number | null>; footerValue?: string; role?: PlateRole }
  | { status: "loading"; text?: string }
  | { status: "unavailable"; reason: string };

export interface SelectionCompareApi {
  items: readonly CompareItem[];
  max: number;
  has: (id: string) => boolean;
  /** Removes the item when present; otherwise adds it unless the set is full (then it is refused). */
  toggle: (item: CompareItem) => void;
  remove: (id: string) => void;
  clear: () => void;
  open: boolean;
  setOpen: (open: boolean) => void;
}

interface CompareSetOptions {
  max: number;
  onChange?: (items: CompareItem[]) => void;
  onOpenChange?: (open: boolean) => void;
  onCompare?: (items: CompareItem[]) => void;
}

interface CompareSetState extends SelectionCompareApi {
  /** The tray's primary action: opens the sheet and asks it to take focus. */
  compare: () => void;
  sheetFocusRequest: number;
  trayRef: RefObject<HTMLDivElement | null>;
  trayActionRef: RefObject<HTMLButtonElement | null>;
  sheetRef: RefObject<HTMLElement | null>;
}

interface SelectionCompareContextValue extends CompareSetState {
  categories?: readonly string[];
  domain?: readonly [number, number];
  format?: CompareValueFormat;
}

const SelectionCompareContext = createContext<SelectionCompareContextValue | null>(null);

const DEFAULT_MAX = 6;
/** Tick count of the sheet's shared scale (the PlateGrid default, named so both agree). */
const SHEET_TICK_COUNT = 4;

function useCompareSetState(options: CompareSetOptions): CompareSetState {
  const [items, setItems] = useState<readonly CompareItem[]>([]);
  const [open, setOpenState] = useState(false);
  const [sheetFocusRequest, setSheetFocusRequest] = useState(0);
  const itemsRef = useRef(items);
  const openRef = useRef(open);
  // Latest handlers and cap, read from event handlers only, so they never recreate the set.
  const optionsRef = useRef(options);
  optionsRef.current = options;
  const trayRef = useRef<HTMLDivElement | null>(null);
  const trayActionRef = useRef<HTMLButtonElement | null>(null);
  const sheetRef = useRef<HTMLElement | null>(null);

  const setOpen = useCallback((next: boolean) => {
    if (openRef.current === next) return;
    openRef.current = next;
    setOpenState(next);
    optionsRef.current.onOpenChange?.(next);
  }, []);

  const commit = useCallback((next: readonly CompareItem[]) => {
    if (next === itemsRef.current) return;
    itemsRef.current = next;
    setItems(next);
    optionsRef.current.onChange?.([...next]);
    // Below two there is nothing to compare, so the sheet closes rather than waiting hidden to
    // reappear by itself: it is opened again only by the tray's action.
    if (next.length < 2 && openRef.current) {
      // The sheet is about to unmount; if it holds focus, hand it to the tray first.
      if (sheetRef.current?.contains(document.activeElement)) {
        trayRef.current?.focus();
      }
      setOpen(false);
    }
  }, [setOpen]);

  const toggle = useCallback((item: CompareItem) => {
    const current = itemsRef.current;
    if (current.some((candidate) => candidate.id === item.id)) {
      commit(current.filter((candidate) => candidate.id !== item.id));
      return;
    }
    // Past the cap the add is refused, not degraded: the set is left exactly as it was.
    if (current.length >= optionsRef.current.max) return;
    commit([...current, item]);
  }, [commit]);

  const remove = useCallback((id: string) => {
    const current = itemsRef.current;
    if (!current.some((candidate) => candidate.id === id)) return;
    commit(current.filter((candidate) => candidate.id !== id));
  }, [commit]);

  const clear = useCallback(() => {
    if (itemsRef.current.length === 0) return;
    commit([]);
  }, [commit]);

  const compare = useCallback(() => {
    setOpen(true);
    setSheetFocusRequest((request) => request + 1);
    optionsRef.current.onCompare?.([...itemsRef.current]);
  }, [setOpen]);

  const has = useCallback((id: string) => items.some((candidate) => candidate.id === id), [items]);

  return useMemo(() => ({
    items,
    max: options.max,
    has,
    toggle,
    remove,
    clear,
    open,
    setOpen,
    compare,
    sheetFocusRequest,
    trayRef,
    trayActionRef,
    sheetRef
  }), [clear, compare, has, items, open, options.max, remove, setOpen, sheetFocusRequest, toggle]);
}

export interface SelectionCompareProviderProps {
  /** Cap on the set. A further add is refused. @default 6 */
  max?: number;
  /** Category labels shared by every plate. */
  categories?: readonly string[];
  /** Explicit shared value domain. Computed across the drawn plates when omitted. */
  domain?: readonly [number, number];
  /** Formats every plotted value; receives the shared scale so precision can follow the tick step. */
  format?: CompareValueFormat;
  /** The set changed (add, remove, clear). Not called for a refused add. */
  onChange?: (items: CompareItem[]) => void;
  /** The sheet was opened or closed (it closes by itself when the set falls below two). */
  onOpenChange?: (open: boolean) => void;
  /** The tray's compare action was used. */
  onCompare?: (items: CompareItem[]) => void;
  children?: ReactNode;
}

/** Owns one compare set and the sheet's open state. The set is view state only: nothing is stored or sent. */
export function SelectionCompareProvider({
  max = DEFAULT_MAX,
  categories,
  domain,
  format,
  onChange,
  onOpenChange,
  onCompare,
  children
}: SelectionCompareProviderProps) {
  const set = useCompareSetState({ max, onChange, onOpenChange, onCompare });
  const value = useMemo<SelectionCompareContextValue>(
    () => ({ ...set, categories, domain, format }),
    [categories, domain, format, set]
  );
  return <SelectionCompareContext.Provider value={value}>{children}</SelectionCompareContext.Provider>;
}

SelectionCompareProvider.displayName = "SelectionCompareProvider";

/** Outside a provider each caller gets its own private set, as in the package: the parts only cooperate inside one. */
function useCompareContext(): SelectionCompareContextValue {
  const context = useContext(SelectionCompareContext);
  const local = useCompareSetState({ max: DEFAULT_MAX });
  return context ?? local;
}

export function useSelectionCompare(): SelectionCompareApi {
  const { items, max, has, toggle, remove, clear, open, setOpen } = useCompareContext();
  return useMemo(
    () => ({ items, max, has, toggle, remove, clear, open, setOpen }),
    [clear, has, items, max, open, remove, setOpen, toggle]
  );
}

const FOCUS_RING = "focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-[var(--interaction-focus)]";

export interface CompareToggleProps
  extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "onClick" | "type" | "disabled" | "title" | "children" | "aria-pressed" | "aria-label"> {
  /** The PlateSpec this row / series becomes when compared. */
  item: CompareItem;
  /** @default "Compare" */
  label?: string;
  /** @default "In compare" */
  pressedLabel?: string;
  /** Glyph only; the accessible name still names the action and the item. @default false */
  compact?: boolean;
  /** Accessible name. @default "Add to comparison: {title}" / "Remove from comparison: {title}" */
  actionLabel?: (item: CompareItem, inSet: boolean) => string;
  /** Words for the refusal when the set is full. @default "Compare set is full ({max})" */
  fullReason?: (max: number) => string;
}

/** Per-row / per-series control. A real `<button aria-pressed>` that never selects or opens its row. */
export function CompareToggle({
  item,
  label = "Compare",
  pressedLabel = "In compare",
  compact = false,
  actionLabel,
  fullReason,
  className,
  ...rest
}: CompareToggleProps) {
  const set = useCompareContext();
  const inSet = set.has(item.id);
  const full = !inSet && set.items.length >= set.max;
  const refusal = full ? (fullReason ? fullReason(set.max) : `Compare set is full (${set.max})`) : undefined;
  return (
    <button
      {...rest}
      type="button"
      aria-pressed={inSet}
      aria-label={actionLabel ? actionLabel(item, inSet) : `${inSet ? "Remove from" : "Add to"} comparison: ${item.title}`}
      disabled={full}
      title={refusal}
      data-compare-toggle={item.id}
      className={cn(
        "inline-flex min-h-6 items-center gap-1 whitespace-nowrap rounded-[var(--radius-chip)] border px-2 font-mono text-xs font-semibold transition-colors",
        inSet
          ? "border-[var(--interaction-selected)] bg-[var(--interaction-selected-surface)] text-foreground"
          : "border-border bg-background text-muted-foreground hover:border-[var(--border-hover)] hover:bg-[var(--bg-hover)]",
        "disabled:cursor-default disabled:opacity-50",
        FOCUS_RING,
        className
      )}
      onClick={(event) => {
        // The toggle lives in clickable rows: toggling must never select or open the row.
        event.stopPropagation();
        set.toggle(item);
      }}
    >
      <span aria-hidden="true" className="text-sm leading-none">{inSet ? "▣" : "▢"}</span>
      {compact ? null : (inSet ? pressedLabel : label)}
    </button>
  );
}

CompareToggle.displayName = "CompareToggle";

export interface CompareTrayProps {
  /** Only the in-flow tray is ported. @default "inline" */
  docked?: "inline";
  /** Follows the count, e.g. "2 of 6 runs to compare". @default "to compare" */
  label?: string;
  /** Shown while the set is empty. Without it the tray hides when empty. */
  emptyLabel?: ReactNode;
  /** Accessible name of the tray region. @default "Comparison set" */
  regionLabel?: string;
  /** Text of the primary action. @default "Compare" below two, "Compare {n}" from two */
  compareLabel?: (count: number) => string;
  /** Why the primary action is disabled below two. */
  compareDisabledReason?: string;
  /** @default "Clear" */
  clearLabel?: string;
  /** Accessible name of a chip's remove button. @default "Remove {title} from comparison" */
  removeLabel?: (item: CompareItem) => string;
  /** Stated when the set reaches the cap. @default "Set is full ({max}). Remove one to add another." */
  fullLabel?: (max: number) => string;
  className?: string;
}

/** The candidate set, visible while it is being built. Hides when empty unless `emptyLabel` is given. */
export function CompareTray({
  docked = "inline",
  label = "to compare",
  emptyLabel = null,
  regionLabel = "Comparison set",
  compareLabel,
  compareDisabledReason = "Add at least two items to compare.",
  clearLabel = "Clear",
  removeLabel,
  fullLabel,
  className
}: CompareTrayProps) {
  const set = useCompareContext();
  const removeButtons = useRef(new Map<string, HTMLButtonElement>());
  const count = set.items.length;
  if (count === 0 && !emptyLabel) return null;
  const full = count >= set.max;
  const comparable = count >= 2;

  // Both actions below unmount the control that was activated. Focus is handed on BEFORE the set
  // changes, to something that stays mounted, so a keyboard user never lands back on <body>.
  const removeChip = (id: string) => {
    const index = set.items.findIndex((candidate) => candidate.id === id);
    const neighbour = set.items[index + 1] ?? set.items[index - 1];
    const target = neighbour ? removeButtons.current.get(neighbour.id) : undefined;
    (target ?? set.trayRef.current)?.focus();
    set.remove(id);
  };
  const clearSet = () => {
    set.trayRef.current?.focus();
    set.clear();
  };
  return (
    <div
      ref={set.trayRef}
      role="region"
      aria-label={regionLabel}
      tabIndex={-1}
      data-compare-tray={docked}
      className={cn(
        "flex min-h-9 flex-wrap items-center gap-2 border border-border bg-[var(--card-surface-raised)] px-3 py-1 text-xs text-muted-foreground",
        FOCUS_RING,
        className
      )}
    >
      <span aria-live="polite" className={cn("flex flex-wrap items-baseline gap-2", count === 0 ? "min-w-0 flex-1 basis-64" : undefined)}>
        <span className="whitespace-nowrap font-mono font-semibold tabular-nums text-foreground">{count} of {set.max}</span>
        <span>{count > 0 ? label : emptyLabel}</span>
        {full ? (
          <span className="text-foreground">
            {fullLabel ? fullLabel(set.max) : `Set is full (${set.max}). Remove one to add another.`}
          </span>
        ) : null}
      </span>
      {count > 0 ? (
        <ul className="flex min-w-0 flex-wrap gap-1" aria-label={`${regionLabel} members`}>
          {set.items.map((item) => (
            <li
              key={item.id}
              className="inline-flex min-h-6 items-center gap-1 whitespace-nowrap rounded-[var(--radius-chip)] border border-border bg-background pl-2 font-mono text-xs text-foreground"
            >
              {item.title}
              {item.tag ? <span className="font-semibold text-muted-foreground">{item.tag}</span> : null}
              <button
                type="button"
                ref={(node) => {
                  if (node) removeButtons.current.set(item.id, node);
                  else removeButtons.current.delete(item.id);
                }}
                aria-label={removeLabel ? removeLabel(item) : `Remove ${item.title} from comparison`}
                onClick={() => removeChip(item.id)}
                className={cn(
                  "min-h-6 min-w-6 rounded-xs text-xs text-muted-foreground hover:bg-[var(--bg-hover)] hover:text-foreground",
                  FOCUS_RING
                )}
              >
                <span aria-hidden="true">×</span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}
      <span className="ml-auto flex shrink-0 items-center gap-2">
        {count > 0 ? (
          <Button size="sm" variant="outline" onClick={clearSet}>{clearLabel}</Button>
        ) : null}
        <Button
          ref={set.trayActionRef}
          size="sm"
          disabled={!comparable}
          disabledReason={comparable ? null : compareDisabledReason}
          onClick={set.compare}
        >
          {compareLabel ? compareLabel(count) : comparable ? `Compare ${count}` : "Compare"}
        </Button>
      </span>
    </div>
  );
}

CompareTray.displayName = "CompareTray";

export interface CompareSheetColumn {
  id: string;
  label: string;
  /** Right-aligned figures in the data face. */
  numeric?: boolean;
  render: (item: CompareItem) => ReactNode;
}

export interface CompareSheetProps {
  /** Only the in-flow sheet is ported. @default "inline" */
  mode?: "inline";
  /** @default "Comparison by selection" */
  title?: string;
  categories?: readonly string[];
  domain?: readonly [number, number];
  /** Formats every plotted value; receives the shared scale so precision can follow the tick step. */
  format?: CompareValueFormat;
  /** One footer metric label for the whole set; only the value varies per item. */
  footerLabel?: string;
  footerValue?: (item: CompareItem) => string | undefined;
  /**
   * Resolve an item's series when it is not on the item itself (for example, fetched when the
   * sheet opens). An item that resolves to `loading` or `unavailable` keeps its plate and says so.
   */
  seriesFor?: (item: CompareItem) => CompareSeriesState | undefined;
  /** The stated basis of the shared scale and of the category axis. Rendered above the plates. */
  basis?: ReactNode;
  /** Column headers for the values table when the plot's category labels are sparse. */
  categoryLabels?: readonly string[];
  /** Extra per-item columns for the values table, ahead of the plotted values. */
  tableColumns?: readonly CompareSheetColumn[];
  /** Header of the table's row-label column. @default "Item" */
  itemColumnLabel?: string;
  /** @default "Clear set" */
  clearLabel?: string;
  /** Accessible name of the Close button. @default "Close comparison" */
  closeLabel?: string;
  className?: string;
}

interface ResolvedCompareRow {
  item: CompareItem;
  series: CompareSeriesState;
  footerValue: string | undefined;
}

function resolveSeries(item: CompareItem, seriesFor: CompareSheetProps["seriesFor"]): CompareSeriesState {
  const resolved = seriesFor?.(item);
  if (resolved) return resolved;
  if (item.values) return { status: "ready", values: item.values };
  return { status: "unavailable", reason: "No series was provided for this item." };
}

function statusText(series: CompareSeriesState): string {
  if (series.status === "loading") return series.text ?? "Loading series…";
  if (series.status === "unavailable") return `Unavailable. ${series.reason}`;
  return "";
}

/**
 * The PlateGrid built from the set, in flow where it is placed. Shown while open with two or more
 * items; the set closes it when it falls below two.
 */
export function CompareSheet({
  mode = "inline",
  title = "Comparison by selection",
  categories,
  domain,
  format,
  footerLabel,
  footerValue,
  seriesFor,
  basis,
  categoryLabels,
  tableColumns = [],
  itemColumnLabel = "Item",
  clearLabel = "Clear set",
  closeLabel = "Close comparison",
  className
}: CompareSheetProps) {
  const set = useCompareContext();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const sectionRef = set.sheetRef;
  const handledFocusRequest = useRef(set.sheetFocusRequest);
  const visible = set.open && set.items.length >= 2;

  // Focus moves into the sheet only when the tray's compare action asked for it — never when a
  // host shows the sheet through `setOpen`.
  useEffect(() => {
    if (!visible || handledFocusRequest.current === set.sheetFocusRequest) return;
    handledFocusRequest.current = set.sheetFocusRequest;
    sectionRef.current?.focus();
  }, [sectionRef, set.sheetFocusRequest, visible]);

  if (!visible) return null;

  const rows: ResolvedCompareRow[] = set.items.map((item) => {
    const series = resolveSeries(item, seriesFor);
    return {
      item,
      series,
      footerValue: item.footerValue ?? (series.status === "ready" ? series.footerValue : undefined) ?? footerValue?.(item)
    };
  });
  const firstReady = rows.find((row) => row.series.status === "ready");
  const firstReadyLength = firstReady?.series.status === "ready" ? firstReady.series.values.length : 0;
  const resolvedCategories = categories
    ?? set.categories
    ?? set.items[0]?.categories
    ?? Array.from({ length: firstReadyLength }, (_, index) => String(index + 1));
  // ONE scale for the plates AND the values table: the formatter is bound to it here, so tick
  // labels, end labels, figure names and table cells all share one precision.
  const sharedDomain = domain
    ?? set.domain
    ?? plateDomain(rows.flatMap((row) => (row.series.status === "ready" ? [row.series.values] : [])));
  const scale = plateScale(sharedDomain, SHEET_TICK_COUNT);
  const rawFormat: CompareValueFormat = format ?? set.format ?? String;
  const valueFormat = (value: number) => rawFormat(value, scale);
  const drawnCount = rows.filter((row) => row.series.status === "ready").length;
  const loadingCount = rows.filter((row) => row.series.status === "loading").length;
  const unavailableCount = rows.filter((row) => row.series.status === "unavailable").length;

  const plates: PlateSpec[] = rows.map(({ item, series, footerValue: rowFooterValue }) => ({
    id: item.id,
    title: item.title,
    tag: item.tag,
    values: series.status === "ready" ? series.values : [],
    categories: item.categories,
    role: (series.status === "ready" ? series.role : undefined) ?? item.role,
    footerLabel: item.footerLabel ?? footerLabel,
    footerValue: rowFooterValue,
    status: series.status === "ready" ? undefined : { kind: series.status, text: statusText(series) }
  }));

  const meta = [
    drawnCount === rows.length ? `${rows.length} plates` : `${drawnCount} of ${rows.length} plates drawn`,
    loadingCount > 0 ? `${loadingCount} loading` : null,
    unavailableCount > 0 ? `${unavailableCount} unavailable` : null,
    "one shared domain",
    `${resolvedCategories.length} categories`
  ].filter(Boolean).join(" · ");

  const columnLabel = (index: number) =>
    categoryLabels?.[index] || resolvedCategories[index] || `Point ${index + 1}`;
  const showFooterColumn = Boolean(footerLabel);

  const close = () => {
    set.setOpen(false);
    set.trayActionRef.current?.focus();
  };

  return (
    <section
      ref={sectionRef}
      aria-label={title}
      tabIndex={-1}
      data-compare-sheet={mode}
      className={cn("flex min-h-0 flex-col border border-border bg-background text-xs text-muted-foreground", FOCUS_RING, className)}
    >
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1 border-b border-border px-3 py-2">
        <h4 className="text-sm font-semibold text-foreground">{title}</h4>
        <span aria-live="polite" className="font-mono text-xs text-muted-foreground">{meta}</span>
        <span className="flex-1" />
        {/* Clearing closes the sheet; the set hands the focus this button holds to the tray. */}
        <Button size="sm" variant="outline" onClick={set.clear}>{clearLabel}</Button>
        <Button size="sm" variant="outline" onClick={close} aria-label={closeLabel}>Close</Button>
      </div>
      <div className="space-y-3 p-3">
        {basis ? <div data-compare-basis className="text-xs leading-5 text-muted-foreground">{basis}</div> : null}
        <PlateGrid
          plates={plates}
          categories={resolvedCategories}
          domain={sharedDomain}
          tickCount={SHEET_TICK_COUNT}
          format={valueFormat}
          selectedId={selectedId}
          onSelect={setSelectedId}
        />
        <details className="border border-border">
          <summary className={cn("cursor-pointer px-3 py-2 text-xs font-semibold text-foreground", FOCUS_RING)}>
            View plotted values
          </summary>
          <div
            role="region"
            aria-label={`${title} plotted values scroll area`}
            tabIndex={0}
            className={cn("overflow-x-auto border-t border-border", FOCUS_RING)}
          >
            <table className="w-full border-collapse text-xs">
              <caption className="sr-only">{title}: plotted values</caption>
              <thead>
                <tr>
                  <th scope="col" className="border-b border-border px-2 py-1 text-left font-medium text-muted-foreground">
                    {itemColumnLabel}
                  </th>
                  {tableColumns.map((column) => (
                    <th
                      key={column.id}
                      scope="col"
                      className={cn(
                        "border-b border-border px-2 py-1 font-medium text-muted-foreground",
                        column.numeric ? "text-right" : "text-left"
                      )}
                    >
                      {column.label}
                    </th>
                  ))}
                  {showFooterColumn ? (
                    <th scope="col" className="border-b border-border px-2 py-1 text-right font-medium text-muted-foreground">
                      {footerLabel}
                    </th>
                  ) : null}
                  {resolvedCategories.length === 0 ? (
                    <th scope="col" className="border-b border-border px-2 py-1 text-left font-medium text-muted-foreground">
                      Values
                    </th>
                  ) : resolvedCategories.map((_, index) => (
                    <th
                      key={index}
                      scope="col"
                      className="border-b border-border px-2 py-1 text-right font-medium text-muted-foreground"
                    >
                      {columnLabel(index)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map(({ item, series, footerValue: rowFooterValue }) => (
                  <tr key={item.id}>
                    <th scope="row" className="border-b border-border px-2 py-1 text-left font-semibold text-foreground">
                      {item.title}
                      {item.tag ? <span className="ml-2 font-mono font-normal text-muted-foreground">{item.tag}</span> : null}
                    </th>
                    {tableColumns.map((column) => (
                      <td
                        key={column.id}
                        className={cn(
                          "border-b border-border px-2 py-1 text-foreground",
                          column.numeric ? "text-right font-mono tabular-nums" : "text-left"
                        )}
                      >
                        {column.render(item)}
                      </td>
                    ))}
                    {showFooterColumn ? (
                      <td className="border-b border-border px-2 py-1 text-right font-mono tabular-nums text-foreground">
                        {rowFooterValue ?? "—"}
                      </td>
                    ) : null}
                    {series.status === "ready" ? (
                      resolvedCategories.map((_, index) => {
                        const value = series.values[index];
                        return (
                          <td key={index} className="border-b border-border px-2 py-1 text-right font-mono tabular-nums text-foreground">
                            {typeof value === "number" && Number.isFinite(value) ? valueFormat(value) : "—"}
                          </td>
                        );
                      })
                    ) : (
                      <td
                        colSpan={Math.max(1, resolvedCategories.length)}
                        className="border-b border-border px-2 py-1 text-left text-muted-foreground"
                      >
                        {statusText(series)}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      </div>
      <p className="border-t border-[var(--border-divider)] px-3 py-2 font-mono text-xs text-muted-foreground">
        Plates share plot size, domain and ticks; a visible difference is a real difference. Selecting a plate never resizes it.
      </p>
    </section>
  );
}

CompareSheet.displayName = "CompareSheet";

import { ChevronDown, X } from "lucide-react";
import {
  forwardRef,
  useId,
  useState,
  type HTMLAttributes,
  type ReactNode
} from "react";
import { cn } from "@/lib/utils";

/**
 * `ScreenLayout` — the one canonical screen skeleton every operator route inherits.
 *
 * The problem it solves: before this, every screen invented its own layout, so an
 * operator's eyes had to re-learn where things were on every route. `ScreenLayout`
 * fixes a four-zone skeleton so the same kind of thing is always in the same place:
 *
 *  1. **Header zone** — screen title, ambient scope, and the 1–3 primary actions
 *     (always top-right, always the same place).
 *  2. **Focus zone** — a compact, opinionated summary of "what needs my attention
 *     here right now" (≤4 signals, not a metric wall). Collapsible.
 *  3. **Work zone** — the main table/chart/form, taking the majority of the space.
 *  4. **Context zone** — a right rail for evidence/detail/provenance that slides in
 *     on selection. This standardizes the home for companion panes and passports.
 *
 * Zones are named slots. The work zone is `children`; the rest are props so the
 * skeleton stays deterministic and every screen renders each zone in the same
 * structural position regardless of what a screen chooses to put in it.
 *
 * @example
 * <ScreenLayout
 *   title="Trial Balance"
 *   scope="All entities · Primary GL · Current period"
 *   actions={<Button>Export</Button>}
 *   focus={[
 *     { id: "breaks", label: "Open breaks", value: "3", tone: "caution" },
 *     { id: "basis", label: "Basis", value: "IFRS" }
 *   ]}
 *   context={selected ? <RowDetail row={selected} /> : null}
 *   contextOpen={Boolean(selected)}
 *   contextLabel="Account detail"
 *   onContextClose={clearSelection}
 * >
 *   <DenseDataTable … />
 * </ScreenLayout>
 */

/** Tone maps a focus signal to a semantic accent without a wall of raw metrics. */
export type FocusSignalTone = "neutral" | "positive" | "caution" | "critical";

export interface FocusSignal {
  /** Stable key for the signal. */
  id: string;
  /** Short label describing what the signal measures. */
  label: ReactNode;
  /** The headline value operators scan for. */
  value: ReactNode;
  /** Optional one-line qualifier shown under the value. */
  hint?: ReactNode;
  /** Semantic accent. Defaults to `"neutral"`. */
  tone?: FocusSignalTone;
}

/** How many focus signals the zone will render before the rest are dropped. */
export const FOCUS_SIGNAL_LIMIT = 4;

const focusToneClasses: Record<FocusSignalTone, string> = {
  neutral: "text-foreground",
  positive: "text-success",
  caution: "text-warning",
  critical: "text-danger"
};

export interface ScreenLayoutProps extends Omit<HTMLAttributes<HTMLDivElement>, "title"> {
  // ── Header zone ─────────────────────────────────────────────────────────
  /** Screen title. Rendered as the labelled heading for the whole layout. */
  title: ReactNode;
  /**
   * Ambient scope for this screen (entity, book, period, environment…). Rendered
   * just under the title so operators always see what they are looking at.
   */
  scope?: ReactNode;
  /** Optional secondary description under the title. */
  description?: ReactNode;
  /**
   * The 1–3 primary actions for this screen. Always rendered top-right. Keep this
   * to the screen's primary verbs — secondary controls belong in the work zone.
   */
  actions?: ReactNode;

  // ── Focus zone ──────────────────────────────────────────────────────────
  /**
   * "What needs my attention here right now." Pass a `FocusSignal[]` for the
   * opinionated signal row (capped at {@link FOCUS_SIGNAL_LIMIT}), or arbitrary
   * content. Omit to hide the zone entirely.
   */
  focus?: FocusSignal[] | ReactNode;
  /** Accessible label / eyebrow for the focus zone. Defaults to "Focus". */
  focusLabel?: string;
  /** When `false`, the focus zone cannot be collapsed. Defaults to `true`. */
  focusCollapsible?: boolean;
  /** Start the focus zone collapsed. Defaults to `false`. */
  focusDefaultCollapsed?: boolean;

  // ── Work zone ───────────────────────────────────────────────────────────
  /** The main table / chart / form. Takes the majority of the space. */
  children: ReactNode;
  /**
   * Extra classes for the work-zone container. The zone stacks its children with
   * the standard `space-y-4` rhythm by default (the common "column of cards" case);
   * pass this to layer on a grid, custom gap, or full-height flex when a screen
   * needs it.
   */
  workClassName?: string;

  // ── Context zone ────────────────────────────────────────────────────────
  /** Evidence / detail / provenance for the current selection. */
  context?: ReactNode;
  /** Whether the context rail is open. When `false`, the work zone fills the width. */
  contextOpen?: boolean;
  /** Accessible label for the context rail. Defaults to "Context". */
  contextLabel?: string;
  /** Called when the operator dismisses the context rail. Omit to hide the close control. */
  onContextClose?: () => void;
}

/**
 * True when a node would actually render something. `ReactNode` admits `false`,
 * `null`, `undefined`, and `""` — all of which render nothing — so zones use this
 * to avoid drawing their chrome (border, header, toggle) around empty content.
 */
function isRenderableNode(node: unknown): boolean {
  return node != null && node !== false && node !== "";
}

function isFocusSignalArray(value: unknown): value is FocusSignal[] {
  return (
    Array.isArray(value) &&
    value.every(
      (item) => item != null && typeof item === "object" && "id" in item && "value" in item
    )
  );
}

function FocusSignalRow({ signals }: { signals: FocusSignal[] }) {
  const visible = signals.slice(0, FOCUS_SIGNAL_LIMIT);
  return (
    <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      {visible.map((signal) => (
        <div key={signal.id} className="min-w-0">
          <dt className="truncate text-xs font-medium text-muted-foreground">{signal.label}</dt>
          <dd className={cn("mt-0.5 truncate text-lg font-semibold leading-tight", focusToneClasses[signal.tone ?? "neutral"])}>
            {signal.value}
          </dd>
          {signal.hint ? <p className="mt-0.5 truncate text-xs text-muted-foreground">{signal.hint}</p> : null}
        </div>
      ))}
    </dl>
  );
}

/**
 * The canonical four-zone screen skeleton. See {@link ScreenLayoutProps} for the
 * named slots. Migrate screens onto this so every route reads the same way.
 */
export const ScreenLayout = forwardRef<HTMLDivElement, ScreenLayoutProps>(function ScreenLayout(
  {
    className,
    title,
    scope,
    description,
    actions,
    focus,
    focusLabel = "Focus",
    focusCollapsible = true,
    focusDefaultCollapsed = false,
    children,
    workClassName,
    context,
    contextOpen = false,
    contextLabel = "Context",
    onContextClose,
    ...props
  },
  ref
) {
  const reactId = useId();
  const titleId = `${reactId}-title`;
  const focusRegionId = `${reactId}-focus`;
  const [focusCollapsed, setFocusCollapsed] = useState(focusDefaultCollapsed);

  // `focus`/`context` are `ReactNode`, so a caller writing `focus={cond && <X/>}`
  // can pass `false` (or `""`). Those are `!= null`, so guard them explicitly or
  // the zone chrome (border, header, toggle) renders around empty content.
  const hasFocus = isRenderableNode(focus) && (!Array.isArray(focus) || focus.length > 0);
  const focusBody = hasFocus
    ? isFocusSignalArray(focus)
      ? <FocusSignalRow signals={focus} />
      : focus
    : null;

  const showContext = contextOpen && isRenderableNode(context);

  return (
    <div
      ref={ref}
      className={cn("flex min-h-0 flex-col gap-4", className)}
      aria-labelledby={titleId}
      {...props}
    >
      {/* 1. Header zone — title, scope, primary actions (always top-right). */}
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 id={titleId} className="truncate text-base font-semibold leading-snug tracking-normal text-foreground">
            {title}
          </h1>
          {scope ? <div className="mt-0.5 text-xs text-muted-foreground">{scope}</div> : null}
          {description ? <p className="mt-1 max-w-prose text-xs leading-5 text-muted-foreground">{description}</p> : null}
        </div>
        {actions ? (
          <div className="flex shrink-0 flex-wrap items-center justify-end gap-2">{actions}</div>
        ) : null}
      </header>

      {/* Body — focus + work stack in the main column; context is the right rail. */}
      <div className="flex min-h-0 flex-1 flex-col gap-4 xl:flex-row">
        <div className="flex min-w-0 flex-1 flex-col gap-4">
          {/* 2. Focus zone — ≤4 signals of "what needs my attention". Collapsible. */}
          {hasFocus ? (
            <section
              aria-label={focusLabel}
              className="rounded-[2px] border border-border bg-card text-card-foreground"
            >
              <div className="flex items-center justify-between gap-2 px-4 py-2">
                <span className="eyebrow-label text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {focusLabel}
                </span>
                {focusCollapsible ? (
                  <button
                    type="button"
                    className="inline-flex items-center gap-1 rounded-[2px] px-1.5 py-0.5 text-xs text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    aria-expanded={!focusCollapsed}
                    aria-controls={focusRegionId}
                    onClick={() => setFocusCollapsed((collapsed) => !collapsed)}
                  >
                    <span>{focusCollapsed ? "Show" : "Hide"}</span>
                    <ChevronDown
                      className={cn("h-3.5 w-3.5 transition-transform", focusCollapsed && "-rotate-90")}
                      aria-hidden="true"
                    />
                  </button>
                ) : null}
              </div>
              <div id={focusRegionId} hidden={focusCollapsed} className="border-t border-border/70 px-4 py-3">
                {focusBody}
              </div>
            </section>
          ) : null}

          {/* 3. Work zone — the main table / chart / form. Stacks cards with the
              standard rhythm by default; override via `workClassName`. */}
          <div className={cn("min-h-0 flex-1 space-y-4", workClassName)}>{children}</div>
        </div>

        {/* 4. Context zone — right rail that slides in on selection. */}
        {showContext ? (
          <aside
            role="complementary"
            aria-label={contextLabel}
            className="flex w-full shrink-0 flex-col rounded-[2px] border border-border bg-card text-card-foreground xl:w-[360px]"
          >
            <div className="flex items-center justify-between gap-2 border-b border-border/70 px-4 py-2">
              <span className="eyebrow-label text-xs font-medium uppercase tracking-wide text-muted-foreground">
                {contextLabel}
              </span>
              {onContextClose ? (
                <button
                  type="button"
                  className="inline-flex h-6 w-6 items-center justify-center rounded-[2px] text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  aria-label={`Close ${contextLabel}`}
                  onClick={onContextClose}
                >
                  <X className="h-4 w-4" aria-hidden="true" />
                </button>
              ) : null}
            </div>
            <div className="min-h-0 flex-1 overflow-auto p-4">{context}</div>
          </aside>
        ) : null}
      </div>
    </div>
  );
});

ScreenLayout.displayName = "ScreenLayout";

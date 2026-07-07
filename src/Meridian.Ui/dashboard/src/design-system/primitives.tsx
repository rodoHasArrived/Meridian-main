import {
  Children,
  cloneElement,
  forwardRef,
  isValidElement,
  type ReactNode
} from "react";
import { Loader2, Menu, Search } from "lucide-react";
import { Link } from "react-router-dom";
import { buildButtonCommandViewModel } from "@/components/ui/button.view-model";
import { DESIGN_SYSTEM_WORKSTATION_TOKENS } from "@/design-system/assets";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import { cn } from "@/lib/utils";
import type { SessionInfo } from "@/types";

/**
 * Dashboard-native adapters over the vendored **Meridian Design System** vocabulary.
 *
 * The vendored `Meridian Design System/` package is the source contract for tokens,
 * assets, and visual language, but its JSX runtime injects global styles, carries
 * governance failures, and ships no TypeScript/a11y wrappers — so the dashboard owns
 * production-ready React here instead of importing the runtime directly. Public shell
 * components (`@/components/ui/button`, `@/components/ui/badge`,
 * `@/components/meridian/workspace-nav`, and the shell masthead) delegate to these
 * adapters so screens keep their existing imports.
 *
 * The shell surfaces consume the workstation token contract exported from
 * {@link DESIGN_SYSTEM_WORKSTATION_TOKENS}; {@link DESIGN_SYSTEM_SHELL_TOKEN_CONTRACT}
 * re-exports it as the canonical list the primitives are wired against.
 */
export const DESIGN_SYSTEM_SHELL_TOKEN_CONTRACT = DESIGN_SYSTEM_WORKSTATION_TOKENS;

/* ─────────────────────────────────────────────────────────────────────────────
   Button
   ───────────────────────────────────────────────────────────────────────────── */

/**
 * Primary interactive element for user actions.
 *
 * **Variants:** `"default"` (cyan primary), `"secondary"` (muted surface),
 * `"outline"` (transparent with border), `"ghost"` (text-only), `"destructive"` (danger-toned).
 *
 * **Sizes:** `"sm"` (32 px), `"default"` (36 px), `"lg"` (44 px, for hero/full-width CTAs),
 * `"icon"` (36×36 px square, no horizontal padding — use for icon-only buttons).
 *
 * **Busy state:** pass `busy` to replace the label with a spinner and set `aria-busy`.
 * Pair with `busyLabel` to provide an accessible announcement (e.g. `"Saving…"`).
 *
 * **Disabled reason:** pass `disabledReason` to surface an explanatory tooltip when the
 * button is disabled, without changing visible text.
 *
 * **AsChild:** set `asChild` to render the button styles and accessibility props on a child
 * `<Link>` or `<a>` instead of a native `<button>`. The child must accept `className`.
 *
 * @example
 * <DesignSystemButton size="sm" variant="outline" busy={isLoading} busyLabel="Saving…">
 *   Save
 * </DesignSystemButton>
 */
export interface DesignSystemButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean;
  variant?: "default" | "secondary" | "outline" | "ghost" | "destructive";
  /** `"sm"` — compact (32 px min-height). `"default"` — standard (36 px). `"lg"` — full-width CTA or hero action (44 px). `"icon"` — square 36×36 px, no padding, for icon-only buttons. */
  size?: "sm" | "default" | "lg" | "icon";
  busy?: boolean;
  busyLabel?: string | null;
  disabledReason?: string | null;
}

type ButtonChildProps = React.HTMLAttributes<HTMLElement> & {
  className?: string;
  "aria-busy"?: true;
  "aria-disabled"?: true;
  tabIndex?: number;
  title?: string;
};

const buttonVariantClasses: Record<NonNullable<DesignSystemButtonProps["variant"]>, string> = {
  default: "border-primary bg-primary text-primary-foreground hover:bg-primary/85 active:bg-[#255B75]",
  secondary: "border-border bg-secondary text-secondary-foreground hover:border-[#ADB8C4] hover:bg-[#EAEEF3] active:bg-[#D7E5F1]",
  outline: "border-border bg-transparent text-foreground hover:border-[#ADB8C4] hover:bg-[#EAEEF3] active:bg-[#D7E5F1]",
  ghost: "border-transparent bg-transparent text-muted-foreground hover:bg-[#EAEEF3] hover:text-foreground active:bg-[#D7E5F1]",
  destructive: "border-danger/60 bg-danger/10 text-danger hover:bg-danger/15"
};

const buttonSizeClasses: Record<NonNullable<DesignSystemButtonProps["size"]>, string> = {
  sm: "min-h-8 px-3 py-1.5 text-xs",
  default: "min-h-9 px-4 py-2 text-sm",
  lg: "min-h-11 px-6 py-3 text-base",
  icon: "h-9 w-9 p-0 text-sm"
};

export const DesignSystemButton = forwardRef<HTMLButtonElement, DesignSystemButtonProps>(
  ({
    asChild = false,
    children,
    className,
    variant = "default",
    size = "default",
    busy = false,
    busyLabel = null,
    disabled = false,
    disabledReason = null,
    title,
    onClick,
    tabIndex,
    ...props
  }, ref) => {
    const vm = buildButtonCommandViewModel({ disabled, busy, busyLabel, disabledReason, title });
    const classes = cn(
      "inline-flex items-center justify-center gap-2 rounded-[2px] border font-semibold transition-[background-color,border-color,color] duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50",
      buttonVariantClasses[variant],
      buttonSizeClasses[size],
      vm.disabled && asChild && "pointer-events-none opacity-50",
      className
    );

    if (asChild && isValidElement(children)) {
      const child = Children.only(children) as React.ReactElement<ButtonChildProps>;
      const childOnClick = child.props.onClick;
      const handleClick: React.MouseEventHandler<HTMLElement> = (event) => {
        if (vm.disabled) {
          event.preventDefault();
          event.stopPropagation();
          return;
        }

        childOnClick?.(event);

        if (!event.defaultPrevented) {
          onClick?.(event as unknown as React.MouseEvent<HTMLButtonElement>);
        }
      };

      return cloneElement(child, {
        ...props,
        className: cn(classes, child.props.className),
        "aria-busy": vm.ariaBusy,
        "aria-disabled": vm.ariaDisabled,
        tabIndex: vm.asChildTabIndex ?? tabIndex ?? child.props.tabIndex,
        title: vm.title ?? child.props.title,
        onClick: handleClick
      });
    }

    return (
      <button
        ref={ref}
        className={classes}
        disabled={vm.disabled}
        aria-busy={vm.ariaBusy}
        onClick={onClick}
        tabIndex={tabIndex}
        title={vm.title}
        {...props}
      >
        {vm.showBusyIndicator && <Loader2 className="h-4 w-4 animate-spin" aria-hidden={vm.iconAriaHidden} />}
        {vm.displayBusyLabel ?? children}
      </button>
    );
  }
);

DesignSystemButton.displayName = "DesignSystemButton";

/* ─────────────────────────────────────────────────────────────────────────────
   Badge
   ───────────────────────────────────────────────────────────────────────────── */

/**
 * Compact status label in font-mono uppercase, used for environment state, data posture,
 * and event classification throughout the operator workstation.
 *
 * **Variants:**
 * - `"default"` — primary/cyan, general status
 * - `"outline"` — muted, secondary metadata
 * - `"success"` / `"warning"` / `"danger"` — semantic state tones
 * - `"paper"` — paper (simulated) trading mode (blue)
 * - `"live"` — **LIVE environment alarm** using `--live-env` (alarm red). NOT the same as
 *   data-posture "live" (cyan). Workspace status `live` maps to `"success"` in view-models.
 * - `"research"` — research/backtest mode
 *
 * **Dot:** set `dot` to prepend a filled circle indicator matched to the variant color.
 *
 * @example
 * <DesignSystemBadge variant="live" dot>LIVE</DesignSystemBadge>
 */
export interface DesignSystemBadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: "default" | "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
  dot?: boolean;
}

const badgeVariantClasses: Record<NonNullable<DesignSystemBadgeProps["variant"]>, string> = {
  default:  "border-primary/40 bg-primary/15 text-primary",
  outline:  "border-border bg-secondary/35 text-muted-foreground",
  success:  "border-success/35 bg-success/12 text-success",
  warning:  "border-warning/35 bg-warning/12 text-warning",
  danger:   "border-danger/35 bg-danger/12 text-danger",
  paper:    "border-paper/35 bg-paper/12 text-paper",
  // "live" variant = LIVE environment (real-money alarm). Uses --live-env (alarm red),
  // not --live (cyan data-posture). Workspace status "live" maps to "success" in view-models.
  live:     "border-live-env/40 bg-live-env/12 text-live-env",
  research: "border-primary/35 bg-primary/12 text-primary"
};

export function DesignSystemBadge({ children, className, dot = false, variant = "default", ...props }: DesignSystemBadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex min-h-6 items-center gap-1.5 rounded-[2px] border px-2.5 py-1 font-mono text-[10px] font-semibold uppercase tracking-[0.14em]",
        badgeVariantClasses[variant],
        className
      )}
      {...props}
    >
      {dot ? <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full bg-current" /> : null}
      {children}
    </span>
  );
}

/* ─────────────────────────────────────────────────────────────────────────────
   Status — the five canonical operator severities
   ───────────────────────────────────────────────────────────────────────────── */

/**
 * The Meridian operator status vocabulary collapses every readiness string onto five
 * canonical severities, mirroring `components/operations/status.js` in the vendored
 * design-system package.
 */
export const DESIGN_SYSTEM_SEVERITIES = ["ready", "review", "action", "blocked", "info"] as const;
export type DesignSystemSeverity = typeof DESIGN_SYSTEM_SEVERITIES[number];

const SEVERITY_ALIASES: Record<string, DesignSystemSeverity> = {
  ready: "ready", passed: "ready", healthy: "ready", complete: "ready", completed: "ready",
  cleared: "ready", certified: "ready", approved: "ready", posted: "ready", live: "ready",
  linked: "ready", verified: "ready", success: "ready", ok: "ready", matched: "ready",
  resolved: "ready", ontrack: "ready", signedoff: "ready",
  review: "review", reviewrequired: "review", inreview: "review", inprogress: "review",
  submitted: "review", awaitingoperatordecision: "review", readyforreview: "review",
  running: "review", queued: "review", awaitingapproval: "review", reopened: "review",
  action: "action", needsattention: "action", needsfix: "action", warning: "action",
  degraded: "action", attention: "action", stale: "action", drafted: "action",
  deferred: "action", breaksdetected: "action", atrisk: "action", partial: "action",
  blocked: "blocked", critical: "blocked", failed: "blocked", rejected: "blocked",
  error: "blocked", blocker: "blocked", breached: "blocked",
  info: "info", unknown: "info", notstarted: "info", missing: "info", pending: "info",
  draft: "info", neutral: "info", notrequired: "info", notready: "info", skipped: "info",
  paused: "info"
};

/** Collapse any Meridian status / severity string onto one of the five canonical severities. */
export function normalizeDesignSystemSeverity(status: string | null | undefined): DesignSystemSeverity {
  if (!status) {
    return "info";
  }

  const key = String(status).toLowerCase().replace(/[^a-z]/g, "");
  return SEVERITY_ALIASES[key] ?? "info";
}

const SEVERITY_BADGE_VARIANT: Record<DesignSystemSeverity, NonNullable<DesignSystemBadgeProps["variant"]>> = {
  ready: "success",
  review: "default",
  action: "warning",
  blocked: "danger",
  info: "outline"
};

const SEVERITY_DEFAULT_LABEL: Record<DesignSystemSeverity, string> = {
  ready: "Ready",
  review: "Review",
  action: "Action",
  blocked: "Blocked",
  info: "Info"
};

export interface DesignSystemStatusProps extends Omit<DesignSystemBadgeProps, "variant"> {
  /** A canonical severity, or any Meridian readiness string that will be normalized. */
  status: DesignSystemSeverity | string;
}

/**
 * Severity chip built on {@link DesignSystemBadge}, encoding the five canonical operator
 * severities (ready · review · action · blocked · info) with the design-system's semantic
 * state tokens. Any raw readiness string is normalized via
 * {@link normalizeDesignSystemSeverity}.
 *
 * @example
 * <DesignSystemStatus status="ReviewRequired" />       // → "Review"
 * <DesignSystemStatus status="blocked" dot>Halted</DesignSystemStatus>
 */
export function DesignSystemStatus({ status, children, dot = true, ...props }: DesignSystemStatusProps) {
  const severity = normalizeDesignSystemSeverity(status);
  return (
    <DesignSystemBadge variant={SEVERITY_BADGE_VARIANT[severity]} dot={dot} {...props}>
      {children ?? SEVERITY_DEFAULT_LABEL[severity]}
    </DesignSystemBadge>
  );
}

/* ─────────────────────────────────────────────────────────────────────────────
   Masthead (workstation topbar)
   ───────────────────────────────────────────────────────────────────────────── */

export interface DesignSystemMastheadCommandTrigger {
  label: string;
  placeholder: string;
  shortcutLabel: string;
  controlsId: string;
  expanded: boolean;
  hasPopup: "dialog";
}

export interface DesignSystemMastheadProps {
  brandMarkSrc: string;
  workspaceLabel: string;
  navOpen: boolean;
  onOpenNavigation: () => void;
  commandTrigger: DesignSystemMastheadCommandTrigger;
  onOpenCommandPalette: () => void;
  trustStrip: AppShellTrustStripState;
  session: SessionInfo | null;
  actions?: ReactNode;
}

/**
 * Dashboard-native workstation masthead. Preserves the operator shell chrome contract:
 * skip-link peer, workspace-navigation trigger, command-palette trigger, trust strip,
 * pluggable actions (activity center, notifications, onboarding), and the session badge —
 * all wired against the design-system masthead token surface.
 */
export function DesignSystemMasthead({
  brandMarkSrc,
  workspaceLabel,
  navOpen,
  onOpenNavigation,
  commandTrigger,
  onOpenCommandPalette,
  trustStrip,
  session,
  actions
}: DesignSystemMastheadProps) {
  return (
    <header className="workstation-masthead">
      <div className="workstation-brand-group">
        <button
          type="button"
          className="workstation-nav-toggle focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          aria-label="Open workspace navigation"
          aria-expanded={navOpen}
          aria-haspopup="dialog"
          onClick={onOpenNavigation}
        >
          <Menu className="h-4 w-4" aria-hidden="true" />
        </button>
        <div className="workstation-brand">
          <img src={brandMarkSrc} alt="" aria-hidden="true" />
          <div className="workstation-brand-copy min-w-0">
            <div className="name">Meridian</div>
            <div className="sub" aria-hidden="true">
              <span className="workstation-brand-sep">/</span>
              {workspaceLabel}
            </div>
          </div>
        </div>
      </div>

      <button
        type="button"
        className="workstation-search focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        onClick={onOpenCommandPalette}
        aria-label={commandTrigger.label}
        aria-controls={commandTrigger.controlsId}
        aria-expanded={commandTrigger.expanded}
        aria-haspopup={commandTrigger.hasPopup}
      >
        <Search className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
        <span className="workstation-search-placeholder">{commandTrigger.placeholder}</span>
        <span className="workstation-search-kbd" aria-hidden="true">{commandTrigger.shortcutLabel}</span>
      </button>

      <DesignSystemTrustStrip viewModel={trustStrip} />

      <div className="workstation-actions">
        {actions}
        {session ? (
          <div
            className="workstation-session-card"
            role="group"
            aria-label={`Current session: ${session.environment}, ${session.displayName}, ${session.role}`}
          >
            <DesignSystemBadge variant={session.environment} dot>{session.environment}</DesignSystemBadge>
            <span className="workstation-session-name">{session.displayName}</span>
            <span className="workstation-session-role text-muted-foreground">{session.role}</span>
          </div>
        ) : (
          <span className="text-xs text-muted-foreground">Loading session...</span>
        )}
      </div>
    </header>
  );
}

/** Build / environment / data-source / provider trust posture strip rendered inside the masthead. */
export function DesignSystemTrustStrip({ viewModel }: { viewModel: AppShellTrustStripState }) {
  return (
    <section className="workstation-trust-strip" aria-label={viewModel.ariaLabel}>
      {viewModel.items.map((item) => {
        const content = (
          <>
            <span className="workstation-trust-label">{item.label}</span>
            <span className="workstation-trust-value">{item.value}</span>
            <span className="sr-only">
              {item.detail}
              {item.actionLabel ? ` ${item.actionLabel}.` : ""}
            </span>
          </>
        );

        return item.href ? (
          <Link
            key={item.id}
            to={item.href}
            className={cn("workstation-trust-item", `workstation-trust-item-${item.tone}`)}
            aria-label={`${item.ariaLabel} ${item.actionLabel}.`}
          >
            {content}
          </Link>
        ) : (
          <span
            key={item.id}
            className={cn("workstation-trust-item", `workstation-trust-item-${item.tone}`)}
            aria-label={item.ariaLabel}
          >
            {content}
          </span>
        );
      })}
    </section>
  );
}

/* ─────────────────────────────────────────────────────────────────────────────
   Nav rail
   ───────────────────────────────────────────────────────────────────────────── */

/**
 * The operator rail class contract owned by the design-system layer. Consumers
 * (`WorkspaceNav`) compose these class names so the rail visual language stays defined
 * once, in the design-system bridge, rather than scattered across screens.
 */
export const designSystemNavRailClasses = {
  root: "operator-rail",
  compact: "operator-rail-compact",
  nav: "operator-rail-nav",
  section: "operator-rail-section",
  scope: "operator-nav-scope",
  group: "operator-nav-group",
  row: "operator-nav-row",
  item: "operator-nav-item",
  itemIcon: "operator-nav-item__icon",
  itemActive: "active",
  status: "operator-nav-status",
  statusDot: "operator-nav-status-dot",
  statusTone: (tone: string) => `operator-nav-status-${tone}`,
  expand: "operator-nav-expand",
  expandExpanded: "expanded",
  subItems: "operator-nav-subitems",
  subItemsCollapsed: "operator-nav-subitems-collapsed",
  subItem: "operator-nav-subitem"
} as const;

export interface DesignSystemNavRailProps {
  className?: string;
  compact?: boolean;
  ariaLabel: string;
  navAriaLabel: string;
  children: ReactNode;
}

/**
 * Presentational shell for the operator navigation rail: the labelled `<aside>` and its
 * inner `<nav>` landmark, styled through {@link designSystemNavRailClasses}. Callers render
 * the rail content (section label, workspace items, sub-items) as children so navigation
 * behavior and accessibility stay owned by the caller.
 */
export function DesignSystemNavRail({
  className,
  compact = false,
  ariaLabel,
  navAriaLabel,
  children
}: DesignSystemNavRailProps) {
  return (
    <aside
      className={cn(designSystemNavRailClasses.root, compact && designSystemNavRailClasses.compact, className)}
      aria-label={ariaLabel}
    >
      <nav className={designSystemNavRailClasses.nav} aria-label={navAriaLabel}>
        {children}
      </nav>
    </aside>
  );
}

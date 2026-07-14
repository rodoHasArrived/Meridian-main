import { Children, cloneElement, forwardRef, isValidElement, type HTMLAttributes, type ReactNode } from "react";
import { ChevronRight, Loader2, Menu, Search } from "lucide-react";
import { Link } from "react-router-dom";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import { buildButtonCommandViewModel } from "@/components/ui/button.view-model";
import {
  DESIGN_SYSTEM_MANIFEST_FILE,
  DESIGN_SYSTEM_PACKAGE_ROOT,
  DESIGN_SYSTEM_TOKEN_FILES
} from "@/design-system/assets";
import { cn } from "@/lib/utils";
import type { SessionInfo } from "@/types";

export const DESIGN_SYSTEM_TOKEN_METADATA = {
  packageRoot: DESIGN_SYSTEM_PACKAGE_ROOT,
  manifestFile: DESIGN_SYSTEM_MANIFEST_FILE,
  tokenFiles: DESIGN_SYSTEM_TOKEN_FILES,
  tokenNames: {
    canvas: "--theme-bg-canvas",
    surface: "--theme-bg-light",
    surfaceSubtle: "--theme-bg-hover",
    accent: "--theme-accent",
    accentHover: "--theme-accent-hover",
    accentPressed: "--theme-accent-dim",
    border: "--theme-border",
    borderFocus: "--theme-border-focus",
    mastheadBackground: "--theme-topbar-bg",
    mastheadText: "--theme-topbar-text",
    statusbarBackground: "--theme-statusbar-bg",
    statusbarText: "--theme-statusbar-text"
  },
  classContracts: {
    button: ["mds-btn", "mds-btn--primary", "mds-btn--ghost", "mds-btn--danger"],
    badge: ["mds-badge", "mds-badge--neutral", "mds-badge--success", "mds-badge--warning", "mds-badge--danger"],
    status: ["mds-status", "mds-status--ready", "mds-status--review", "mds-status--blocked"],
    masthead: ["workstation-masthead", "mds-masthead", "ws-masthead"],
    navRail: ["operator-rail", "mds-nav-rail", "op-rail"]
  }
} as const;

export interface DesignSystemButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean;
  variant?: "default" | "secondary" | "outline" | "ghost" | "destructive";
  size?: "sm" | "default" | "lg" | "icon";
  busy?: boolean;
  busyLabel?: string | null;
  disabledReason?: string | null;
}

type ButtonChildProps = HTMLAttributes<HTMLElement> & {
  className?: string;
  "aria-busy"?: true;
  "aria-disabled"?: true;
  "data-design-system-component"?: string;
  tabIndex?: number;
  title?: string;
};

const buttonVariantClasses: Record<NonNullable<DesignSystemButtonProps["variant"]>, string> = {
  default: "mds-btn--primary border-primary bg-primary text-primary-foreground hover:bg-primary/85 active:bg-[#255B75]",
  secondary:
    "mds-btn--ghost mds-btn--secondary border-border bg-secondary text-secondary-foreground hover:border-[#ADB8C4] hover:bg-[#EAEEF3] active:bg-[#D7E5F1]",
  outline:
    "mds-btn--ghost mds-btn--outline border-border bg-transparent text-foreground hover:border-[#ADB8C4] hover:bg-[#EAEEF3] active:bg-[#D7E5F1]",
  ghost:
    "mds-btn--link mds-btn--ghost-action border-transparent bg-transparent text-muted-foreground hover:bg-[#EAEEF3] hover:text-foreground active:bg-[#D7E5F1]",
  destructive: "mds-btn--danger border-danger/60 bg-danger/10 text-danger hover:bg-danger/15"
};

const buttonSizeClasses: Record<NonNullable<DesignSystemButtonProps["size"]>, string> = {
  sm: "mds-btn--sm min-h-8 px-3 py-1.5 text-xs",
  default: "mds-btn--default min-h-9 px-4 py-2 text-sm",
  lg: "mds-btn--lg min-h-11 px-6 py-3 text-base",
  icon: "mds-btn--icon h-9 w-9 p-0 text-sm"
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
      "mds-btn inline-flex items-center justify-center gap-2 rounded-[2px] border font-semibold transition-[background-color,border-color,color] duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50",
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
        "data-design-system-component": "Button",
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
        data-design-system-component="Button"
        {...props}
      >
        {vm.showBusyIndicator ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden={vm.iconAriaHidden} /> : null}
        {vm.displayBusyLabel ?? children}
      </button>
    );
  }
);

DesignSystemButton.displayName = "DesignSystemButton";

export interface DesignSystemBadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: "default" | "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
  dot?: boolean;
}

const badgeVariantClasses: Record<NonNullable<DesignSystemBadgeProps["variant"]>, string> = {
  default: "mds-badge--info border-primary/40 bg-primary/15 text-primary",
  outline: "mds-badge--neutral border-border bg-secondary/35 text-muted-foreground",
  success: "mds-badge--success border-success/35 bg-success/12 text-success",
  warning: "mds-badge--warning border-warning/35 bg-warning/12 text-warning",
  danger: "mds-badge--danger border-danger/35 bg-danger/12 text-danger",
  paper: "mds-badge--paper border-paper/35 bg-paper/12 text-paper",
  live: "mds-badge--live border-live-env/40 bg-live-env/12 text-live-env",
  research: "mds-badge--info border-primary/35 bg-primary/12 text-primary"
};

export function DesignSystemBadge({
  children,
  className,
  dot = false,
  variant = "default",
  ...props
}: DesignSystemBadgeProps) {
  return (
    <span
      className={cn(
        "mds-badge inline-flex min-h-6 items-center gap-1.5 rounded-[2px] border px-2.5 py-1 font-mono text-[10px] font-semibold uppercase tracking-[0.14em]",
        badgeVariantClasses[variant],
        className
      )}
      data-design-system-component="Badge"
      {...props}
    >
      {dot ? <span aria-hidden="true" className="mds-badge__dot h-1.5 w-1.5 rounded-full bg-current" /> : null}
      {children}
    </span>
  );
}

export type DesignSystemStatusTone = "ready" | "review" | "action" | "blocked" | "info" | "muted";

export interface DesignSystemStatusProps extends React.HTMLAttributes<HTMLSpanElement> {
  tone?: DesignSystemStatusTone;
  dot?: boolean;
}

const statusToneClasses: Record<DesignSystemStatusTone, string> = {
  ready: "mds-status--ready border-success/35 bg-success/12 text-success",
  review: "mds-status--review border-primary/35 bg-primary/12 text-primary",
  action: "mds-status--action border-warning/35 bg-warning/12 text-warning",
  blocked: "mds-status--blocked border-danger/35 bg-danger/12 text-danger",
  info: "mds-status--info border-border bg-secondary/35 text-muted-foreground",
  muted: "mds-status--muted border-border bg-transparent text-muted-foreground"
};

export function DesignSystemStatus({
  children,
  className,
  dot = true,
  tone = "info",
  ...props
}: DesignSystemStatusProps) {
  return (
    <span
      className={cn(
        "mds-status inline-flex min-h-5 items-center gap-1.5 rounded-[2px] border px-2 py-0.5 font-mono text-[10px] font-semibold uppercase",
        statusToneClasses[tone],
        className
      )}
      data-design-system-component="Status"
      {...props}
    >
      {dot ? <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" /> : null}
      {children}
    </span>
  );
}

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
    <header
      className="workstation-masthead mds-masthead ws-masthead"
      data-design-system-component="Masthead"
    >
      <div className="workstation-brand-group mds-masthead__brand-group ws-brand">
        <button
          type="button"
          className="workstation-nav-toggle mds-masthead__nav-toggle focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          aria-label="Open workspace navigation"
          aria-expanded={navOpen}
          aria-haspopup="dialog"
          onClick={onOpenNavigation}
        >
          <Menu className="h-4 w-4" aria-hidden="true" />
        </button>
        <div className="workstation-brand mds-masthead__brand">
          <img src={brandMarkSrc} alt="" aria-hidden="true" />
          <div className="workstation-brand-copy min-w-0">
            <div className="name ws-brand__name">Meridian</div>
            <div className="sub ws-brand__mod" aria-hidden="true">
              <span className="workstation-brand-sep ws-brand__sep">/</span>
              {workspaceLabel}
            </div>
            <span className="sr-only">Current workspace: {workspaceLabel}</span>
          </div>
        </div>
      </div>

      <button
        type="button"
        className="workstation-search mds-masthead__search ws-search focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        onClick={onOpenCommandPalette}
        aria-label={commandTrigger.label}
        aria-controls={commandTrigger.controlsId}
        aria-expanded={commandTrigger.expanded}
        aria-haspopup={commandTrigger.hasPopup}
      >
        <Search className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
        <span className="workstation-search-placeholder ws-search__txt">{commandTrigger.placeholder}</span>
        <span className="workstation-search-kbd ws-kbd" aria-hidden="true">{commandTrigger.shortcutLabel}</span>
      </button>

      <DesignSystemTrustStrip viewModel={trustStrip} />

      <div className="workstation-actions mds-masthead__actions">
        {actions}
        {session ? (
          <div
            className="workstation-session-card mds-session-card"
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

export function DesignSystemTrustStrip({
  viewModel
}: {
  viewModel: AppShellTrustStripState;
}) {
  return (
    <section
      className="workstation-trust-strip mds-trust-strip"
      aria-label={viewModel.ariaLabel}
      data-design-system-component="Status"
    >
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
            className={cn("workstation-trust-item", `workstation-trust-item-${item.tone}`, `mds-status--${item.tone}`)}
            aria-label={`${item.ariaLabel} ${item.actionLabel}.`}
          >
            {content}
          </Link>
        ) : (
          <span
            key={item.id}
            className={cn("workstation-trust-item", `workstation-trust-item-${item.tone}`, `mds-status--${item.tone}`)}
            aria-label={item.ariaLabel}
          >
            {content}
          </span>
        );
      })}
    </section>
  );
}

export interface DesignSystemNavRailSubItem {
  label: string;
  route: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
}

export interface DesignSystemNavRailItem {
  key: string;
  label: string;
  statusLabel: string;
  statusTone: string;
  route: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
  iconSrc: string;
  subItems: DesignSystemNavRailSubItem[];
}

export interface DesignSystemNavRailProps {
  brandTitle: string;
  navEyebrow: string;
  items: DesignSystemNavRailItem[];
  density?: "compact" | "detailed";
  className?: string;
  operatingScopeLabel?: string | null;
  operatingScopeAriaLabel?: string | null;
  expandedKeys: ReadonlySet<string>;
  onToggleWorkspace: (key: string) => void;
  onNavigate?: () => void;
}

export function DesignSystemNavRail({
  brandTitle,
  navEyebrow,
  items,
  density = "detailed",
  className,
  operatingScopeLabel = null,
  operatingScopeAriaLabel = null,
  expandedKeys,
  onToggleWorkspace,
  onNavigate
}: DesignSystemNavRailProps) {
  const compact = density === "compact";

  return (
    <aside
      className={cn("operator-rail mds-nav-rail op-rail", compact && "operator-rail-compact", className)}
      aria-label={`${brandTitle} navigation`}
      data-design-system-component="NavRail"
    >
      <nav className="operator-rail-nav op-rail__nav" aria-label="Workspaces">
        <div className="operator-rail-section op-rail__section">{navEyebrow}</div>
        {!compact && operatingScopeLabel ? (
          <div className="operator-nav-scope" aria-label={operatingScopeAriaLabel ?? undefined}>
            <span>Context</span>
            <span>{operatingScopeLabel}</span>
          </div>
        ) : null}
        {items.map((item) => {
          const expanded = expandedKeys.has(item.key);
          const subMenuId = `workspace-nav-${density}-${item.key}-sections`;
          return (
            <div className="operator-nav-shell-group" key={item.key}>
              <div className="operator-nav-group">
                <div className="operator-nav-row">
                  <Link
                    to={item.route}
                    aria-current={item.ariaCurrent}
                    aria-label={item.ariaLabel}
                    className={cn(
                      "operator-nav-item op-nav-item focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                      item.active ? "active" : ""
                    )}
                    onClick={onNavigate}
                  >
                    <img className="operator-nav-item__icon op-nav-item__icon" src={item.iconSrc} width="16" height="16" alt="" aria-hidden="true" />
                    <span className="truncate font-medium">{item.label}</span>
                    {!compact ? (
                      <span className={`operator-nav-status operator-nav-status-${item.statusTone}`}>
                        <span className="operator-nav-status-dot" aria-hidden="true" />
                        {item.statusLabel}
                      </span>
                    ) : null}
                  </Link>
                  {item.subItems.length > 0 ? (
                    <button
                      type="button"
                      className={cn(
                        "operator-nav-expand focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                        expanded && "expanded"
                      )}
                      aria-label={`${expanded ? "Collapse" : "Expand"} ${item.label} pages`}
                      aria-expanded={expanded}
                      aria-controls={subMenuId}
                      onClick={() => onToggleWorkspace(item.key)}
                    >
                      <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                    </button>
                  ) : null}
                </div>
              </div>
              {item.subItems.length > 0 ? (
                <div
                  id={subMenuId}
                  className={cn("operator-nav-subitems", !expanded && "operator-nav-subitems-collapsed")}
                  role="group"
                  aria-label={`${item.label} pages`}
                  aria-hidden={!expanded}
                >
                  {expanded ? item.subItems.map((sub) => (
                    <Link
                      key={sub.route}
                      to={sub.route}
                      aria-current={sub.ariaCurrent}
                      aria-label={sub.ariaLabel}
                      className={cn(
                        "operator-nav-subitem focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                        sub.active && "active"
                      )}
                      onClick={onNavigate}
                    >
                      <span className="truncate">{sub.label}</span>
                    </Link>
                  )) : null}
                </div>
              ) : null}
            </div>
          );
        })}
      </nav>
    </aside>
  );
}

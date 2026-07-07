import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Menu, Search } from "lucide-react";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { SessionInfo } from "@/types";

export interface WorkstationTopbarCommandTrigger {
  label: string;
  placeholder: string;
  shortcutLabel: string;
  controlsId: string;
  expanded: boolean;
  hasPopup: "dialog";
}

export interface WorkstationTopbarProps {
  brandMarkSrc: string;
  workspaceLabel: string;
  navOpen: boolean;
  onOpenNavigation: () => void;
  commandTrigger: WorkstationTopbarCommandTrigger;
  onOpenCommandPalette: () => void;
  trustStrip: AppShellTrustStripState;
  session: SessionInfo | null;
  actions?: ReactNode;
}

export function WorkstationTopbar({
  brandMarkSrc,
  workspaceLabel,
  navOpen,
  onOpenNavigation,
  commandTrigger,
  onOpenCommandPalette,
  trustStrip,
  session,
  actions
}: WorkstationTopbarProps) {
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

      <WorkstationTrustStrip viewModel={trustStrip} />

      <div className="workstation-actions">
        {actions}
        {session ? (
          <div
            className="workstation-session-card"
            role="group"
            aria-label={`Current session: ${session.environment}, ${session.displayName}, ${session.role}`}
          >
            <Badge variant={session.environment} dot>{session.environment}</Badge>
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

export function WorkstationTrustStrip({
  viewModel
}: {
  viewModel: AppShellTrustStripState;
}) {
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

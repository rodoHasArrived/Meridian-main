import type { ReactNode } from "react";
import { Menu, Search } from "lucide-react";
import { DesignSystemBadge } from "@/design-system/badge";
import { DesignSystemTrustStrip } from "@/design-system/trust-strip";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import type { SessionInfo } from "@/types";

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
 * pluggable actions (activity center, notifications, onboarding), and the session badge -
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

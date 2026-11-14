// Meridian workspace section (Concrete) — mirrors the desktop `.workspace-section-band`.
//
// A sticky section header (title + summary on the left, optional jump affordance on the
// right) over a content body. Used to band an operator workspace into labelled,
// jump-to-able regions that keep their heading pinned while the region scrolls.
import { SectionDescription } from "@/components/ui/section-description";
import type { ReactNode } from "react";
import { SectionHeading } from "@/components/meridian/section-heading";
import { injectStyle } from "./inject-style";

const CSS = `
.mds-wsection{display:grid;gap:12px;scroll-margin-top:12px;min-width:0;}
.mds-wsection__head{position:sticky;top:0;z-index:15;display:flex;flex-wrap:wrap;align-items:center;
  justify-content:space-between;gap:10px;min-width:0;
  border:1px solid var(--border-color,#D2CDC3);border-radius:var(--radius-card,2px);background:var(--bg-medium,#EBE8E1);
  padding:9px 12px;}
.mds-wsection__heading{min-width:0;}
.mds-wsection__title{margin:0;color:var(--text-primary,#20252B);font-size:13px;font-weight:700;line-height:1.2;}
.mds-wsection__summary{margin:1px 0 0;color:var(--text-muted,#606870);font-size:12px;line-height:1.35;}
.mds-wsection__jump{display:inline-flex;align-items:center;gap:6px;min-height:30px;
  border:1px solid var(--interaction-link,#2A5F96);border-radius:var(--radius-chip,2px);
  background:var(--blue-a10,rgba(47,100,138,.10));color:var(--interaction-link,#2A5F96);
  padding:4px 10px;font-family:var(--font-body);font-size:11px;font-weight:700;cursor:pointer;text-decoration:none;
  white-space:nowrap;}
.mds-wsection__jump:hover{background:var(--bg-active,#E7EDF2);}
.mds-wsection__jump:focus-visible{outline:var(--focus-ring,2px solid var(--interaction-focus,#2D6FB3));outline-offset:2px;}
.mds-wsection__body{display:grid;gap:12px;min-width:0;}
`;

export interface WorkspaceSectionProps {
  /** Anchor id (for jump links / scroll-margin). */
  id?: string;
  /** Section title. */
  title?: ReactNode;
  /** Muted one-line summary under the title. */
  summary?: ReactNode;
  /** Opt static introductory copy into remembered help. Keep live status summaries visible. */
  helpId?: string;
  /** Remember standalone introductory copy; omit for changing operational summaries. */
  descriptionKey?: string;
  /** Jump affordance label (omit to hide). */
  jump?: ReactNode;
  /** Jump click handler (button mode). */
  onJump?: () => void;
  /** Jump href (anchor mode — takes precedence over onJump). */
  jumpHref?: string;
  children?: ReactNode;
  className?: string;
}

/**
 * Workspace section — a sticky section header over a content body. Bands an operator
 * workspace into labelled regions whose heading stays pinned while the region scrolls.
 *
 * @example
 * <WorkspaceSection
 *   id="reconciliation"
 *   title="Reconciliation"
 *   summary="3 open exceptions above tolerance · custody cash lane"
 *   jump="Open lane" jumpHref="#recon"
 * >
 *   <ValidationIssueList issues={issues} />
 * </WorkspaceSection>
 */
export function WorkspaceSection({
  id,
  title,
  summary,
  helpId,
  descriptionKey,
  jump,
  onJump,
  jumpHref,
  children,
  className,
}: WorkspaceSectionProps) {
  injectStyle("workspace-section", CSS);
  return (
    <section id={id} className={`mds-wsection${className ? " " + className : ""}`}>
      <div className="mds-wsection__head">
        <div className="mds-wsection__heading flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
          {helpId && typeof summary === "string" ? (
            <SectionHeading title={title} description={summary} helpId={helpId} as="h3" headingClassName="mds-wsection__title" descriptionClassName="mds-wsection__summary" />
          ) : <>
            {title && <h3 className="mds-wsection__title">{title}</h3>}
            {summary && (descriptionKey ? <SectionDescription descriptionKey={descriptionKey} label={`About ${typeof title === "string" ? title : "this section"}`}>{summary}</SectionDescription> : <p className="mds-wsection__summary basis-full">{summary}</p>)}
          </>}
        </div>
        {jump &&
          (jumpHref ? (
            <a className="mds-wsection__jump" href={jumpHref}>
              {jump}
            </a>
          ) : (
            <button type="button" className="mds-wsection__jump" onClick={onJump}>
              {jump}
            </button>
          ))}
      </div>
      <div className="mds-wsection__body">{children}</div>
    </section>
  );
}

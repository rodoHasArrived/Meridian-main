/**
 * Workspace section — mirrors `.workspace-section-band`. A sticky section header
 * (title + summary, optional jump affordance) over a content body. Bands an operator
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
export interface WorkspaceSectionProps {
  /** Anchor id (for jump links / scroll-margin). */
  id?: string;
  /** Section title. */
  title?: React.ReactNode;
  /** Muted one-line summary under the title. */
  summary?: React.ReactNode;
  /** Jump affordance label (omit to hide). */
  jump?: React.ReactNode;
  /** Jump click handler (button mode). */
  onJump?: () => void;
  /** Jump href (anchor mode — takes precedence over onJump). */
  jumpHref?: string;
  children?: React.ReactNode;
  className?: string;
}
export declare function WorkspaceSection(props: WorkspaceSectionProps): JSX.Element;

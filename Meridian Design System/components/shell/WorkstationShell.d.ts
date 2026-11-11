/**
 * Slot-based workstation frame — composes the chrome (topbar · nav rail · status bar) around a
 * scrolling main area and an optional right rail. Every slot is optional and the layout degrades
 * gracefully; main and aside content are crash-isolated so a failing panel never takes the chrome
 * down. Use instead of hand-rolling the flex scaffolding in every template.
 */
export interface WorkstationShellProps {
  /** Masthead slot — usually a `<WorkstationTopbar/>`. */
  topbar?: React.ReactNode;
  /** Left rail slot — usually a `<NavRail/>`. Omit for a full-width workspace. */
  nav?: React.ReactNode;
  /** Optional right rail (inspector / detail panel). */
  aside?: React.ReactNode;
  /** Width of the right rail. @default 320 */
  asideWidth?: number | string;
  /** Accessible name for the right rail. @default "Evidence" */
  asideLabel?: string;
  /** Footer slot — usually a `<StatusBar/>`. */
  statusBar?: React.ReactNode;
  /** Pad the main area with `--space-lg`. @default false */
  padded?: boolean;
  /** CSS height of the shell frame. @default "100vh" */
  height?: number | string;
  /** Accessible name for the main workspace (use when no visible heading names it). */
  label?: string;
  /** Id of the heading that names the main workspace — wins over `label`. */
  labelledBy?: string;
  /** Workspace archetype, emitted as `data-workspace-archetype` for styling and audit. */
  archetype?: "monitor" | "analyze" | "inspect" | "compare" | "reconcile" | "compose";
  /** Main workspace content. */
  children?: React.ReactNode;
}
export declare function WorkstationShell(props: WorkstationShellProps): JSX.Element;

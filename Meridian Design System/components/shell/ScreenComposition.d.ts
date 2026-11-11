/**
 * ScreenComposition — a screen declared, not forked. `defineScreen()` states an
 * archetype and its regions; `ScreenComposition` lays them out on the 12-column
 * workstation grid and fills each region from `slots`. Changing an archetype then
 * propagates to every screen that declares it, instead of costing one edit per
 * copy-forked template.
 *
 * Archetypes and their legal regions live in `ARCHETYPES`
 * (`monitor · investigate · compose · report · admin · start`); a region outside
 * its archetype is reported in `screen.warnings` and rendered as a warning row,
 * so drift is visible rather than silent.
 *
 * Manifests for Meridian's own screens: `templates/screen-manifests.js`.
 * Law: `guidelines/SCREEN_COMPOSITION.md`.
 *
 * @example
 * const screen = defineScreen({
 *   id: "reconciliation-exception", archetype: "investigate",
 *   regions: [
 *     { id: "context", role: "context", title: "Workspace" },
 *     { id: "claim", role: "claim", title: "Variance" },
 *     { id: "primary", role: "primary", title: "Tie-out" },
 *     { id: "evidence", role: "evidence", title: "Evidence & consequence" },
 *     { id: "action", role: "action", title: "Resolution" },
 *   ],
 * });
 * <ScreenComposition screen={screen} slots={{ primary: <TieOut />, evidence: <EvidenceRail /> }} />
 */
export type ScreenArchetype = "monitor" | "investigate" | "compose" | "report" | "admin" | "start";

export interface ScreenRegion {
  id: string;
  /** Which archetype region this fills. Defaults to `id`. */
  role?: string;
  /** 12-column span. Defaults to the role's canonical span. */
  span?: number;
  title?: string;
  /** Placeholder text when no slot is supplied. */
  hint?: string;
}
export interface ScreenSpec {
  id?: string;
  archetype: ScreenArchetype;
  title?: string;
  regions?: (ScreenRegion | string)[];
}
export interface ScreenManifest extends ScreenSpec {
  regions: Required<Pick<ScreenRegion, "id" | "role" | "span">>[] & ScreenRegion[];
  warnings: string[];
}
/** Also reachable as `ScreenComposition.define`; archetypes as `ScreenComposition.archetypes`. */
export declare function defineScreen(spec: ScreenSpec): ScreenManifest;

export interface ScreenCompositionProps extends React.HTMLAttributes<HTMLDivElement> {
  screen: ScreenManifest | ScreenSpec;
  /** Region id → content. Markup children tagged `data-region="<id>"` fill regions too. */
  slots?: Record<string, React.ReactNode>;
  /** Label each region with its archetype role — for review and documentation. */
  showRoles?: boolean;
}
export declare function ScreenComposition(props: ScreenCompositionProps): JSX.Element;

export declare const ARCHETYPES: Record<ScreenArchetype, { label: string; regions: string[] }>;

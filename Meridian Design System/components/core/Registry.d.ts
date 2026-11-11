/**
 * Registry — one lookup for every controlled vocabulary in the system
 * (`density · severity · timeRole · seriesRole · condition · environment ·
 * authority`), replacing the four parallel constant carriers. `resolve()`
 * normalizes aliases to canonical keys, which is how the density naming drift is
 * retired: `compact` → `dense`, `spacious` → `review`, `default` → `standard`,
 * and on severity `ok/warning/error/critical` → `ready/review/action/blocked`.
 *
 * @example
 * resolve("density", "compact");   // "dense"
 * resolve("severity", "warning");  // "review"
 * REGISTRY.density.review.row;     // 44
 * <RegistryTable />                // every vocabulary, documented
 */
export declare const REGISTRY: Record<string, Record<string, any>>;
export declare const DENSITY: Record<"terminal" | "dense" | "standard" | "review", { label: string; row: number; body: number; aliases?: string[] }>;
export declare const SEVERITY: Record<"ready" | "info" | "review" | "action" | "blocked", { label: string; rank: number }>;
export declare const TIME_ROLES: Record<string, string>;
/** Also reachable as `Registry.resolve` (non-enumerable, so vocabulary iteration is unaffected). */
export declare function resolve(vocabulary: keyof typeof REGISTRY, value: string): string | null;
export interface RegistryTableProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Show one vocabulary instead of all. */
  vocabulary?: keyof typeof REGISTRY;
}
export declare function RegistryTable(props: RegistryTableProps): JSX.Element;

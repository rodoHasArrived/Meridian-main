// Types for design-system-ratchets.mjs, so the contract test type-checks under strict mode.

export interface DesignSystemRatchet {
  description: string;
  count(source: string): number;
}

export type RatchetCounts = Record<string, Record<string, number>>;

export interface RatchetComparison {
  regressions: string[];
  improvements: string[];
}

export const BASELINE_PATH: string;
export const RATCHETS: Record<"adHocStatusBadges" | "directIntlFormatting" | "rawTables", DesignSystemRatchet>;
export function isAdHocBadgeClass(value: string): boolean;
export function scanRatchets(): RatchetCounts;
export function readBaseline(): RatchetCounts;
export function compareWithBaseline(current?: RatchetCounts, baseline?: RatchetCounts): RatchetComparison;

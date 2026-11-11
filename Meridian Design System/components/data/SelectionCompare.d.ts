/**
 * SelectionCompare — comparison by selection (4.2, UX-depth track E-2). Two or more committed
 * selections — DataGrid rows, figure series, ledger lines — are promoted into a `PlateGrid` from the
 * screen itself, rather than only declaratively in template source.
 *
 * Three parts around one set: `CompareToggle` on the row / series / line (`aria-pressed`, disabled
 * when the set is full), `CompareTray` keeping the candidate set visible while it is built
 * (chips, Clear, `Compare N` — enabled from two), and `CompareSheet`, the PlateGrid under the
 * comparison-plate contract: one shared domain, one tick set, plates never resized. Outside a
 * provider each part keeps a private set (single-toggle fixtures).
 *
 * @example
 * <SelectionCompareProvider max={6} categories={["2Y","5Y","10Y","30Y"]} format={(v) => v.toFixed(2)}>
 *   <DataGrid columns={[…, { key:"cmp", label:"", cell:(r) => <CompareToggle compact item={{ id:r.id, title:r.name, tag:r.code, values:r.curve }} /> }]} … />
 *   <CompareTray docked="corner" />
 *   <CompareSheet footerLabel="10Y change" footerValue={(p) => p.meta} />
 * </SelectionCompareProvider>
 */
import type { PlateSpec } from "../charts/ComparisonPlate";

/** What a selected thing becomes when compared — a PlateSpec, plus anything the sheet's footer reads. */
export interface CompareItem extends PlateSpec { [extra: string]: unknown }

export interface SelectionCompareApi {
  items: CompareItem[];
  max: number;
  has(id: string): boolean;
  /** Adds, or removes if present; a full set ignores adds. */
  toggle(item: CompareItem): void;
  remove(id: string): void;
  clear(): void;
  open: boolean;
  setOpen(open: boolean): void;
}
export declare function useSelectionCompare(): SelectionCompareApi;
/** Bundle carrier. The compiler exposes only capitalized exports. */
export declare const SelectionCompareHooks: { useSelectionCompare: typeof useSelectionCompare };

export interface SelectionCompareProviderProps {
  /** Past ~12 plates the contract says table + one focused plate; the tray stops here. @default 6 */
  max?: number;
  /** Shared plate categories; falls back to the first item's. */
  categories?: string[];
  /** Shared domain; omit to compute one across every plate — never per plate. */
  domain?: [number, number];
  format?: (v: number) => string;
  onChange?: (items: CompareItem[]) => void;
  onCompare?: (items: CompareItem[]) => void;
  children?: React.ReactNode;
}
export declare function SelectionCompareProvider(props: SelectionCompareProviderProps): JSX.Element;

export interface CompareToggleProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  item: CompareItem;
  /** @default "Compare" */
  label?: string;
  /** @default "In compare" */
  pressedLabel?: string;
  /** Glyph only — for a narrow grid column. @default false */
  compact?: boolean;
}
export declare function CompareToggle(props: CompareToggleProps): JSX.Element;

export interface CompareTrayProps extends React.HTMLAttributes<HTMLDivElement> {
  /** `"corner"` floats above the plane; `"inline"` sits where placed. @default "inline" */
  docked?: "inline" | "corner";
  /** @default "to compare" */
  label?: string;
  /** Render the tray with this text when the set is empty (default: render nothing). */
  emptyLabel?: React.ReactNode;
}
export declare function CompareTray(props: CompareTrayProps): JSX.Element | null;

export interface CompareSheetProps extends React.HTMLAttributes<HTMLElement> {
  /** @default "overlay" */
  mode?: "overlay" | "inline";
  /** @default "Comparison by selection" */
  title?: string;
  categories?: string[];
  domain?: [number, number];
  format?: (v: number) => string;
  /** Footer metric — same label across the set; `footerValue` reads each item. */
  footerLabel?: string;
  footerValue?: (item: CompareItem) => string;
}
/** Renders only while `open` and the set holds two or more. Escape closes the overlay. */
export declare function CompareSheet(props: CompareSheetProps): JSX.Element | null;

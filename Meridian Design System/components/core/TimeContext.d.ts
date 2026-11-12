import type React from "react";
/**
 * TimeContext — the temporal contract. Meridian works with twelve kinds of time; a figure is only
 * interpretable when the surface states which ones it is stated in. `TimeContextProvider` declares
 * them once per screen, `TimeRole` and `TimeContextPanel` render them, and `checkTimeCoherence`
 * catches a temporally impossible record (settlement before trade, a figure calculated before its
 * evidence, a posting outside its reporting period).
 *
 * Reach for this instead of scattering `date`, `asOf`, `timestamp` props across a screen. It is a
 * declaration, not a picker: `AccountingPeriodSelector` still owns choosing the period, and
 * `WorkspaceContextBar` still shows period and as-of in the chrome — both read the same roles.
 *
 * @example
 * <TimeContextProvider value={{ trade: "2026-07-30", settlement: "2026-08-02",
 *   posting: "2026-07-31", reportingPeriod: "2026-07", asOf: "2026-07-31T23:59:00Z",
 *   evidenceAt: "2026-08-01T06:14:00Z", calculatedAt: "2026-08-01T04:02:00Z" }}>
 *   <TimeContextPanel sources={{ settlement: "Northern Trust" }} />
 *   <TimeRole role="settlement" />
 * </TimeContextProvider>
 */

/** The twelve roles. `event` = the world's clock · `book` = recognition · `observation` = this figure. */
export type TimeRoleId =
  | "trade" | "settlement" | "effective" | "received"
  | "posting" | "valuation" | "reportingPeriod" | "fiscalPeriod" | "closeDeadline"
  | "asOf" | "evidenceAt" | "calculatedAt";

export interface TimeRoleMeta {
  id: TimeRoleId;
  label: string;
  kind: "event" | "book" | "observation";
  /** `date` renders `2026-07-31` · `instant` renders `2026-07-31 23:59:00Z` · `period` renders verbatim. */
  precision: "date" | "instant" | "period";
  /** The system that owns this date — name it when two sources disagree. */
  authority: string;
  definition: string;
}
/** The role ledger, in reading order. */
export declare const TIME_ROLES: TimeRoleMeta[];

/** Role id → value. Dates accept `Date`, epoch ms, or ISO string; periods are strings (`"2026-07"`, `"FY2026"`). */
export type TimeContextValue = Partial<Record<TimeRoleId, Date | number | string | null>>;

export interface TimeCoherenceIssue {
  id: string;
  /** `error` — temporally impossible · `warning` — legal but consequential · `info` — a difference the reader must not assume away. */
  severity: "error" | "warning" | "info";
  message: string;
  /** The roles the issue is about; `TimeContextPanel` flags these rows. */
  roles: TimeRoleId[];
}
/** Runs the ordering, period, deadline, and staleness rules. Pure — safe to call in render or a gate. */
export declare function checkTimeCoherence(
  time?: TimeContextValue,
  options?: { now?: Date | number | string; closed?: boolean },
): TimeCoherenceIssue[];

export declare function TimeContextProvider(props: {
  value?: TimeContextValue;
  defaultValue?: TimeContextValue;
  onChange?: (value: TimeContextValue) => void;
  children?: React.ReactNode;
}): React.JSX.Element;

export declare function useTimeContext(): {
  time: TimeContextValue;
  update(patch: TimeContextValue | ((current: TimeContextValue) => TimeContextValue)): void;
  reset(): void;
  get(id: TimeRoleId): Date | number | string | null | undefined;
  coherence: TimeCoherenceIssue[];
};

/** Bundle carrier. The compiler exposes only capitalized exports, so consumers of
 *  `_ds_bundle.js` read the hook and helpers through this object. */
export declare const TimeHooks: {
  useTimeContext: typeof useTimeContext;
  checkTimeCoherence: typeof checkTimeCoherence;
  formatTimeRole(value: unknown, precision: TimeRoleMeta["precision"]): string;
  TIME_ROLES: TimeRoleMeta[];
};

export interface TimeRoleProps extends React.HTMLAttributes<HTMLSpanElement> {
  role: TimeRoleId;
  /** Omit to read the provider. */
  value?: Date | number | string | null;
  /** Override the ledger label. */
  label?: React.ReactNode;
  /** @default true */
  showLabel?: boolean;
}
/** One role with its name — for headers, cells, and inspector lines. */
export declare function TimeRole(props: TimeRoleProps): React.JSX.Element;

export interface TimeContextPanelProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "title"> {
  /** Merged over the provider's value. */
  value?: TimeContextValue;
  /** Roles to show, in ledger order. Omit to show every role the context carries. */
  roles?: TimeRoleId[];
  /** Render the coherence rules under the roles. @default true */
  coherence?: boolean;
  /** Show requested roles that have no value, as `—`. @default false */
  showMissing?: boolean;
  /** Role id → the system that supplied it. */
  sources?: Partial<Record<TimeRoleId, React.ReactNode>>;
  title?: React.ReactNode;
}
/** The screen's temporal declaration, grouped by whose clock each role is. */
export declare function TimeContextPanel(props: TimeContextPanelProps): React.JSX.Element;

/** Module carrier — the whole contract in one reference. */
export declare const TimeContext: {
  Provider: typeof TimeContextProvider;
  Panel: typeof TimeContextPanel;
  Role: typeof TimeRole;
  ROLES: TimeRoleMeta[];
  check: typeof checkTimeCoherence;
  use: typeof useTimeContext;
  format(value: unknown, precision: TimeRoleMeta["precision"]): string;
};

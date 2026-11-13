import * as React from "react";
export type CloseStatus = "not-started" | "in-progress" | "waiting" | "at-risk" | "blocked" | "review" | "complete" | "reopened" | "waived";
export interface CloseDay { date: string; label?: string }
export interface CloseTask {
  id: string | number;
  /** Calendar-view placement; must match a `days[].date`. */
  date?: string;
  title: string;
  /** Exact deadline, not a relative phrase — "Aug 3 18:00 UTC". */
  due?: string;
  owner?: string;
  /** Groups the owner view when several owners share a function. */
  team?: string;
  status?: CloseStatus;
  /** Ids of tasks this one requires; drives the dependency view's layering. */
  dependsOn?: (string | number)[];
  /** Why it is blocked, in words. */
  blocker?: string;
  /** Evidence completeness, e.g. "4 of 5 sources". */
  evidence?: React.ReactNode;
  /** Sign-off requirement, e.g. "Controller". */
  signoff?: React.ReactNode;
  completedAt?: string;
  [key: string]: unknown;
}
export interface CloseCalendarProps extends Omit<React.HTMLAttributes<HTMLElement>, "onSelect" | "title"> {
  days: CloseDay[];
  tasks: CloseTask[];
  /** Day carrying the current-time Datum. */
  currentDate?: string;
  currentTime?: string;
  title?: React.ReactNode;
  period?: React.ReactNode;
  selectedId?: string | number | null;
  onSelect?: (task: CloseTask) => void;
  /** Controlled view. Omit for uncontrolled with `defaultView`. */
  view?: "calendar" | "dependency" | "owner";
  defaultView?: "calendar" | "dependency" | "owner";
  onViewChange?: (view: "calendar" | "dependency" | "owner") => void;
}
export declare function CloseCalendar(props: CloseCalendarProps): React.JSX.Element;

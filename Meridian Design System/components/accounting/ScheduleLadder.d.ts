import * as React from "react";
import type { DataConditionKey, VarianceState } from "./VarianceCell";

export type ScheduleNotation="factor"|"rate"|"percent"|"bp"|"currency"|"accounting"|"quantity"|"number";
export type ScheduleRowState=VarianceState|"undelivered";

/** A property requirement: a key, all-of list (nested lists read as any-of), or an explicit any/all object. */
export type ScheduleRequirement=string|ScheduleRequirement[]|{any?:ScheduleRequirement[];all?:ScheduleRequirement[]}|((properties:Record<string,unknown>)=>boolean);

export interface ScheduleLadderRow{
  id?:string;
  /** Period identifier — the row's stable name, e.g. "2026-09" or "Reset 4". */
  period:string;
  /** Secondary period detail shown under the period. */
  label?:string;
  /** Payable / effective date. ISO or display text; ISO lets past-versus-future derive from `asOf`. */
  date?:string;
  dateNote?:string;
  /** Modelled from the security's terms. */
  expected?:number|null;
  expectedNote?:string;
  /** Received from the source. Absent means undelivered, never agreement. */
  actual?:number|null;
  /** Date the actual is due — shown on undelivered rows. */
  actualDue?:string;
  /** Force projected/undelivered treatment. Derived from `date` versus `asOf` when omitted. */
  projected?:boolean;
  /** Highlights the current period. */
  current?:boolean;
  tolerance?:number;
  tolerancePct?:number;
  nearFactor?:number;
  condition?:DataConditionKey|string;
  conditionDetail?:string;
  comparisonLabel?:string;
  note?:React.ReactNode;
  notComparable?:boolean;
  state?:VarianceState;
  [key:string]:unknown;
}

export interface ScheduleLadderProps extends Omit<React.HTMLAttributes<HTMLElement>,"onSelect">{
  title?:string;
  /** Schedule kind shown as a tag, e.g. "Amortization factor" or "Rate reset". */
  kind?:string;
  /** The security — CUSIP, ticker, or pool number. */
  object?:string;
  /** Cut date separating delivered from projected rows. */
  asOf?:string;
  /** The security's properties. Applicability is decided from these, never from an asset-class label. */
  properties?:Record<string,unknown>;
  /** Property requirement that makes this schedule applicable. Omit to always render. */
  requires?:ScheduleRequirement;
  notApplicableReason?:string;
  /** Default true — an inapplicable schedule renders nothing. False renders a stated absence line. */
  hideWhenNotApplicable?:boolean;
  expectedLabel?:string;
  /** Names where expected comes from — the indenture, prospectus, or your projection. */
  expectedSource?:string;
  /** Footer statement of the modelling basis. */
  basis?:string;
  actualLabel?:string;
  /** Names the delivering source — factor file, agent notice, index fixing. */
  actualSource?:string;
  rows:ScheduleLadderRow[];
  notation?:ScheduleNotation;
  digits?:number;
  currency?:string;
  unit?:string;
  tolerance?:number;
  tolerancePct?:number;
  nearFactor?:number;
  periodLabel?:string;
  dateLabel?:string;
  /** Next-event strip above the ladder. */
  showNext?:boolean;
  showTally?:boolean;
  note?:React.ReactNode;
  /** Wording on undelivered rows. Default "Not yet delivered". */
  pendingText?:string;
  selectedId?:string|null;
  onSelectRow?:(row:ScheduleLadderRow)=>void;
  onOpenTrace?:(row:ScheduleLadderRow)=>void;
}
export declare function ScheduleLadder(props:ScheduleLadderProps):React.JSX.Element|null;

export interface ScheduleLadderSetProps extends React.HTMLAttributes<HTMLElement>{
  properties?:Record<string,unknown>;
  /** Declared schedules, in reading order. Each carries its own `requires`. */
  schedules:Array<Omit<ScheduleLadderProps,"properties">&{id?:string}>;
  emptyText?:string;
  /** True renders inapplicable schedules as stated absence lines instead of hiding them. */
  showNotApplicable?:boolean;
}
export declare function ScheduleLadderSet(props:ScheduleLadderSetProps):React.JSX.Element;

export declare function scheduleApplies(properties:Record<string,unknown>|undefined,requires?:ScheduleRequirement):boolean;

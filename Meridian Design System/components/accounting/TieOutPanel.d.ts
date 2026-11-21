import * as React from "react";
export type TieOutType="one-to-one"|"many-to-one"|"one-to-many"|"rollforward"|"quantity-and-value"|"count-and-amount"|"multi-basis";
/**
 * A tie-out whose inputs are not all usable reads `indeterminate`: a source with no value, or one
 * carrying a `missing` / `pending` / `stale` / `na` / `exception` / `restricted` condition, is NOT treated as zero.
 * The residual renders as —, acceptance and resolution are unavailable, and a caller-declared
 * `status` cannot outrank the gap.
 */
export interface TieOutSource{label:string;value?:number|null;source?:string;condition?:string;count?:number;}
export interface TieOutItem{id?:string;label:string;value:number;condition?:string;evidence?:React.ReactNode;explained?:boolean;}
export interface TieOutSecondary{label:string;actual:number;expected:number;tolerance?:number;notation?:string;unit?:string;digits?:number;comparisonLabel?:string;}
export interface TieOutPanelProps extends Omit<React.HTMLAttributes<HTMLElement>, "title">{title?:React.ReactNode;context?:React.ReactNode;type?:TieOutType;sourceA:TieOutSource;sourceB:TieOutSource;items?:TieOutItem[];adjustments?:TieOutItem[];secondary?:TieOutSecondary;tolerance?:number;currency?:string;notation?:"currency"|"number";unit?:string;digits?:number;defaultExpanded?:boolean;expanded?:boolean;onToggleDetail?:(open:boolean)=>void;reviewer?:string;reviewedAt?:string;evidence?:React.ReactNode;evidenceLabel?:string;requireReason?:boolean;acceptLabel?:string;onInvestigate?:()=>void;onAddItem?:()=>void;onLinkEvidence?:()=>void;onAccept?:(reason:string)=>void;onEscalate?:()=>void;onReopen?:()=>void;onResolve?:()=>void;onExport?:()=>void;status?:"tied"|"tolerance"|"exception"|"indeterminate";}
export declare function TieOutPanel(props:TieOutPanelProps):React.JSX.Element;

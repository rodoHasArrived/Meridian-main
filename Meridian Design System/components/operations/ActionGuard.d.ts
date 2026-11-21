import * as React from "react";
/** `pending` = the check cannot be evaluated yet (no draft input); never blocks, never reads as satisfied. */
export type GuardState="pending"|"satisfied"|"warning"|"blocking";
export interface GuardRemediation{label:string;actionId:string;requiredRole?:string;}
export interface ActionGuardValue{id:string;state:GuardState;label:string;reason?:string;policyRef?:string;evidenceRefs?:string[];remediation?:GuardRemediation;}
export interface GuardSummary{total:number;satisfied:number;warning:number;blocking:number;pending:number;blocked:boolean;firstBlocking:ActionGuardValue|null;sentence:string;}
export declare function summarizeGuards(guards?:ActionGuardValue[]):GuardSummary;
export interface ActionGuardListProps extends React.HTMLAttributes<HTMLElement>{guards?:ActionGuardValue[];label?:string;showSummary?:boolean;sort?:boolean;onRemediate?:(remediation:GuardRemediation,guard:ActionGuardValue)=>void;}
export declare function ActionGuardList(props:ActionGuardListProps):React.JSX.Element;
export interface ActionAvailabilityProps extends React.HTMLAttributes<HTMLDivElement>{action:React.ReactNode;guards?:ActionGuardValue[];available?:boolean;reason?:React.ReactNode;requiredRole?:string;onRemediate?:(remediation:GuardRemediation,guard:ActionGuardValue|null)=>void;}
export declare function ActionAvailability(props:ActionAvailabilityProps):React.JSX.Element;
export declare const ActionGuard:{states:Record<GuardState,{label:string;glyph:string;rank:number}>;summarize:typeof summarizeGuards;List:(props:ActionGuardListProps)=>React.JSX.Element;Availability:(props:ActionAvailabilityProps)=>React.JSX.Element;};

import * as React from "react";
export type AuthorityStage="source"|"calculated"|"override"|"approved"|"posted"|"reported";
export type AuthorityCondition="confirmed"|"provisional"|"estimated"|"stale"|"overridden"|"missing"|"pending"|"na"|"exception"|"reconciled"|"restricted";
export interface AuthorityCandidate<T=number>{stage:AuthorityStage;value:T;version?:string|number;effectiveAt?:string;sourceRef?:string;actorId?:string;condition?:AuthorityCondition|string;}
export interface ValueAuthorityValue<T=number>{candidates:AuthorityCandidate<T>[];authoritativeStage?:AuthorityStage;rationale?:string;policyRef?:string;}
export interface ResolvedValueAuthority<T=number> extends ValueAuthorityValue<T>{authoritativeStage:AuthorityStage;authoritative:AuthorityCandidate<T>|null;}
export declare function resolveAuthority<T=number>(authority?:ValueAuthorityValue<T>|null):ResolvedValueAuthority<T>|null;
export interface AuthorityChipProps extends React.HTMLAttributes<HTMLSpanElement>{stage:AuthorityStage;authoritative?:boolean;version?:string|number;}
export declare function AuthorityChip(props:AuthorityChipProps):React.JSX.Element;
export interface AuthorityStackProps extends React.HTMLAttributes<HTMLElement>{authority:ValueAuthorityValue;label?:string;unit?:string;format?:(value:any)=>string;showMeta?:boolean;}
export declare function AuthorityStack(props:AuthorityStackProps):React.JSX.Element|null;
export interface AuthorityPanelProps extends React.HTMLAttributes<HTMLDivElement>{authority:ValueAuthorityValue;unit?:string;format?:(value:any)=>string;caption?:React.ReactNode;}
export declare function AuthorityPanel(props:AuthorityPanelProps):React.JSX.Element|null;
export declare const ValueAuthority:{stages:AuthorityStage[];stageMeta:Record<AuthorityStage,{label:string;glyph:string;note:string}>;resolve:typeof resolveAuthority;Stack:(props:AuthorityStackProps)=>React.JSX.Element|null;Chip:(props:AuthorityChipProps)=>React.JSX.Element;Panel:(props:AuthorityPanelProps)=>React.JSX.Element|null;};

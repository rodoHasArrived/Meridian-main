import type{ActionGuardValue,GuardSummary}from"./ActionGuard";
import type{ObjectRef}from"../core/ObjectModel";
/**
 * 4.2 — `FinancialObjectType` and `FinancialObjectRef` are REMOVED, not deprecated. They were a
 * second object-identity union that omitted `strategy`, so the seventeenth object existed for
 * `ObjectHeader` and not for the decision system that has to approve it. Every reference to an
 * object now goes through the canonical `ObjectRef` from `core/ObjectModel`, whose vocabulary is
 * `MeridianObjectType`. Migration: docs/UPGRADING.md › 4.2. Enforced by conformance rule C-15.
 */
export type ReversibilityClass="reversible"|"compensatable"|"supersedable"|"irreversible";
export interface DecisionStep{id:string;label:string;requires?:string[];guards?:ActionGuardValue[];invalidates?:string[];optional?:boolean;stableAcrossVersions?:boolean;onEnter?:()=>void|Promise<void>;}
export interface DecisionTransition{from:string;to:string;at:string;actorId?:string;reason?:string;}
export interface DecisionFlowValue{object?:ObjectRef;snapshotId?:string;steps:DecisionStep[];stepId:string;step:DecisionStep|null;index:number;completed:Record<string,{at:string;actorId?:string}>;guards:Record<string,ActionGuardValue[]>;stepGuards:(id:string)=>ActionGuardValue[];summary:GuardSummary;transitions:DecisionTransition[];versionChanged:{from:string|number|undefined;to:string|number|undefined}|null;acknowledgeVersionChange:()=>void;unmet:(id:string)=>string[];goTo:(id:string,opts?:{force?:boolean;reason?:string})=>Promise<boolean>;complete:(id?:string,detail?:Record<string,unknown>)=>Promise<boolean>;validate:(id:string)=>Promise<ActionGuardValue[]>;isBlocked:boolean;}
export interface DecisionFlowProviderProps{object?:ObjectRef;snapshotId?:string;steps:DecisionStep[];initialStep?:string;persistedKey?:string;urlParam?:string;actorId?:string;onValidate?:(stepId:string)=>Promise<ActionGuardValue[]>|ActionGuardValue[];onTransition?:(transition:DecisionTransition)=>void;children?:React.ReactNode;}
export declare function DecisionFlowProvider(props:DecisionFlowProviderProps):JSX.Element;
export declare function useDecisionFlow():DecisionFlowValue;
export interface DecisionFlowRailProps extends React.HTMLAttributes<HTMLDivElement>{label?:string;showGuardCounts?:boolean;}
export declare function DecisionFlowRail(props:DecisionFlowRailProps):JSX.Element;
export interface DecisionFlowStageProps extends React.HTMLAttributes<HTMLElement>{title?:React.ReactNode;showVersionNotice?:boolean;children?:React.ReactNode;}
export declare function DecisionFlowStage(props:DecisionFlowStageProps):JSX.Element;
export interface DecisionConsequenceBarProps extends React.HTMLAttributes<HTMLDivElement>{primaryLabel?:React.ReactNode;onPrimary?:(detail:{object?:ObjectRef;snapshotId?:string;stepId:string;guards:ActionGuardValue[]})=>void;secondaryLabel?:React.ReactNode;onSecondary?:()=>void;reversibility?:ReversibilityClass;guards?:ActionGuardValue[];requireCompletion?:boolean;}
export declare function DecisionConsequenceBar(props:DecisionConsequenceBarProps):JSX.Element;
export declare const DecisionFlow:{reversibility:Record<ReversibilityClass,{label:string;note:string}>;use:typeof useDecisionFlow;useDecisionFlow:typeof useDecisionFlow;Provider:(props:DecisionFlowProviderProps)=>JSX.Element;Rail:(props:DecisionFlowRailProps)=>JSX.Element;Stage:(props:DecisionFlowStageProps)=>JSX.Element;ConsequenceBar:(props:DecisionConsequenceBarProps)=>JSX.Element;};

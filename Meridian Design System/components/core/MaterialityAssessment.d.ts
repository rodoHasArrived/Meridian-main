import * as React from "react";
export type MaterialityClassification="immaterial"|"operational"|"financial"|"reporting";
export interface MaterialityQualitativeFactor{id:string;label:string;present:boolean;rationale?:string;}
export interface MaterialityAssessmentValue{classification:MaterialityClassification;amount?:number;percentage?:number;quantitative?:{threshold?:number;benchmark?:string;ratio?:number};qualitativeFactors?:MaterialityQualitativeFactor[];policyId:string;policyVersion?:string;evaluatedAt?:string;evaluatedBy?:string;}
export interface MaterialityBadgeProps extends React.HTMLAttributes<HTMLElement>{assessment?:MaterialityAssessmentValue|null;classification?:MaterialityClassification;showAmount?:boolean;currency?:string;onOpen?:()=>void;}
export declare function MaterialityBadge(props:MaterialityBadgeProps):React.JSX.Element;
export interface MaterialityAssessmentProps extends React.HTMLAttributes<HTMLElement>{assessment?:MaterialityAssessmentValue|null;currency?:string;title?:string;}
export declare function MaterialityAssessment(props:MaterialityAssessmentProps):React.JSX.Element|null;

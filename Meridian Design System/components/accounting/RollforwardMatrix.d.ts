import * as React from "react";
export interface RollforwardColumn{id:string;label:string;}export interface RollforwardRow{id?:string;label:string;values:Record<string,number|React.ReactNode>;operator?:string;total?:boolean;check?:boolean;tolerance?:number;}
export interface RollforwardMatrixProps extends React.HTMLAttributes<HTMLDivElement>{columns:RollforwardColumn[];rows:RollforwardRow[];valueFormat?:(value:any)=>React.ReactNode;}
export declare function RollforwardMatrix(props:RollforwardMatrixProps):React.JSX.Element;

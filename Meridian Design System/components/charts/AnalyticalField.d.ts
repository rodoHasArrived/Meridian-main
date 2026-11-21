import * as React from "react";
export interface AnalyticalFieldItem { id?:string|number; label?:string; value?:React.ReactNode; condition?:string; state?:string; }
export interface AnalyticalFieldProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect">{items:AnalyticalFieldItem[];columns?:number;selectedId?:string|number|null;onSelect?:(item:AnalyticalFieldItem,index:number)=>void;showLegend?:boolean;summary?:React.ReactNode;}
export declare function AnalyticalField(props:AnalyticalFieldProps):React.JSX.Element;

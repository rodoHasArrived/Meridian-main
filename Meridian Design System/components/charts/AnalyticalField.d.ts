export interface AnalyticalFieldItem { id?:string|number; label?:string; value?:React.ReactNode; condition?:string; state?:string; }
export interface AnalyticalFieldProps extends React.HTMLAttributes<HTMLDivElement>{items:AnalyticalFieldItem[];columns?:number;selectedId?:string|number|null;onSelect?:(item:AnalyticalFieldItem,index:number)=>void;showLegend?:boolean;summary?:React.ReactNode;}
export declare function AnalyticalField(props:AnalyticalFieldProps):JSX.Element;

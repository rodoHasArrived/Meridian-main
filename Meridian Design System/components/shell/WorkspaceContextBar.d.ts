import type React from "react";
export interface WorkspaceContext { entity?: {type?: string; label?: string} | string; book?: string; basis?: string; period?: string; asOf?: string; currency?: string; scenario?: string; condition?: string; locked?: boolean; [key:string]: unknown; }
export declare function WorkspaceContextProvider(props: {value?: WorkspaceContext; defaultValue?: WorkspaceContext; onChange?: (value:WorkspaceContext)=>void; children?:React.ReactNode}): React.JSX.Element;
export declare function useWorkspaceContext(): {context:WorkspaceContext; update(patch:Partial<WorkspaceContext>|((current:WorkspaceContext)=>Partial<WorkspaceContext>)):void; reset():void};
/** Bundle carrier. The compiler exposes only capitalized exports, so consumers of
 *  `_ds_bundle.js` read the hook through this object. */
export declare const WorkspaceHooks: { useWorkspaceContext: typeof useWorkspaceContext };
export interface WorkspaceContextBarProps extends React.HTMLAttributes<HTMLElement> {
  value?:WorkspaceContext; condition?:string;
  onEntityClick?:()=>void; onBookClick?:()=>void; onPeriodClick?:()=>void;
  onAsOfClick?:()=>void; onCurrencyClick?:()=>void; onScenarioClick?:()=>void;
  actions?:React.ReactNode;
  /** `"auto"` (default) measures the bar and moves the lowest-priority facets into a
   *  `More +N` disclosure when they stop fitting; `"never"` keeps the 3.1.0 scroll
   *  behaviour; `"always"` collapses to `minVisible` regardless of width.
   *  Drop order is scenario → currency → book → as-of → period; entity, data
   *  condition, and a locked period always survive. */
  collapse?: "auto" | "never" | "always";
  /** Facets that never collapse, in priority order. Default 2. */
  minVisible?: number;
  /** Label on the overflow disclosure. Default "More". */
  overflowLabel?: React.ReactNode;
}
export declare function WorkspaceContextBar(props:WorkspaceContextBarProps):React.JSX.Element;

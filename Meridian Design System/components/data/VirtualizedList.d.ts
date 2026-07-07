import React from "react";

export interface VirtualizedListProps<T = any> {
  /** Full data set — only the visible slice is rendered */
  items: T[];
  /** Fixed row height in px (required for scroll math). @default 34 */
  rowHeight?: number;
  /** Viewport height in px. @default 360 */
  height?: number;
  /** Extra rows rendered above/below the viewport. @default 6 */
  overscan?: number;
  /** Render a single row. Receives (item, index) */
  renderRow?: (item: T, index: number) => React.ReactNode;
  className?: string;
  style?: React.CSSProperties;
}

export declare function VirtualizedList<T = any>(props: VirtualizedListProps<T>): JSX.Element;

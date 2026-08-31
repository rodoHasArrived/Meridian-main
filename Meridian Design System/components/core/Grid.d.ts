import React from 'react';

export interface GridProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Number of columns. @default 2 */
  cols?: number;
  /** Grid template rows (CSS value). @default "auto" */
  rows?: string;
  /** Gap between items: xs | sm | md | lg | xl | 2xl or CSS value. @default "md" */
  gap?: 'xs' | 'sm' | 'md' | 'lg' | 'xl' | '2xl' | string;
  /** Grid items */
  children?: React.ReactNode;
}

export declare function Grid(props: GridProps): JSX.Element;

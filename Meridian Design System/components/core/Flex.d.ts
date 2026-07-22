import React from 'react';

export interface FlexProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Stack vertically (column direction). @default false */
  vertical?: boolean;
  /** Align items: flex-start | center | flex-end | stretch. @default "stretch" */
  align?: React.CSSProperties['alignItems'];
  /** Justify content: flex-start | center | flex-end | space-between | space-around. @default "flex-start" */
  justify?: React.CSSProperties['justifyContent'];
  /** Gap between items: xs | sm | md | lg | xl | 2xl or CSS value. @default "md" */
  gap?: 'xs' | 'sm' | 'md' | 'lg' | 'xl' | '2xl' | string;
  /** Allow wrapping. @default false */
  wrap?: boolean;
  /** Flex items */
  children?: React.ReactNode;
}

export declare function Flex(props: FlexProps): JSX.Element;

import React from 'react';

export interface StackProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Stack direction: vertical | row. @default "vertical" */
  direction?: 'vertical' | 'column' | 'row' | 'horizontal';
  /** Spacing between items: xs | sm | md | lg | xl | 2xl or CSS value. @default "md" */
  spacing?: 'xs' | 'sm' | 'md' | 'lg' | 'xl' | '2xl' | string;
  /** Align items: flex-start | center | flex-end | stretch. @default "stretch" */
  align?: React.CSSProperties['alignItems'];
  /** Justify content: flex-start | center | flex-end | space-between | space-around. @default "flex-start" */
  justify?: React.CSSProperties['justifyContent'];
  /** Stack items */
  children?: React.ReactNode;
}

export declare function Stack(props: StackProps): JSX.Element;

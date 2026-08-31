import React from "react";

export interface AccordionItem {
  /** Section header */
  title: React.ReactNode;
  /** Optional right-aligned badge (count, status) */
  badge?: React.ReactNode;
  /** Body content, shown when open */
  content: React.ReactNode;
}

export interface AccordionProps {
  /** Sections */
  items: AccordionItem[];
  /** Allow multiple sections open at once. @default false */
  multi?: boolean;
  /** Indices open on first render */
  defaultOpen?: number[];
}

export declare function Accordion(props: AccordionProps): JSX.Element;

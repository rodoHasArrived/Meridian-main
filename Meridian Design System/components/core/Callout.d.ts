import React from "react";

export interface CalloutProps {
  /** Semantic tone. @default "info" */
  tone?: "info" | "success" | "warning" | "danger";
  /** Bold title line */
  title?: React.ReactNode;
  /** Override the default tone glyph */
  icon?: React.ReactNode;
  /** Body prose */
  children?: React.ReactNode;
}

export declare function Callout(props: CalloutProps): JSX.Element;

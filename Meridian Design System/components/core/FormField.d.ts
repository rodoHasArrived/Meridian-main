import React from 'react';

export interface FormFieldProps {
  /** Stable control id; an explicit id on the child takes precedence. */
  id?: string;
  /** Field label (rendered as small-caps) */
  label?: string;
  /** Help text below the field */
  hint?: string;
  /** Error message (overrides hint, styled in red) */
  error?: string;
  /** Mark field as required (adds red *) */
  required?: boolean;
  /** Single control element that forwards id and ARIA props to its focusable input. */
  children?: React.ReactNode;
  className?: string;
  style?: React.CSSProperties;
}

export declare function FormField(props: FormFieldProps): JSX.Element;

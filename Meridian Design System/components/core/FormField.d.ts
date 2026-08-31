import React from 'react';

export interface FormFieldProps {
  /** Field label (rendered as small-caps) */
  label?: string;
  /** Help text below the field */
  hint?: string;
  /** Error message (overrides hint, styled in red) */
  error?: string;
  /** Mark field as required (adds red *) */
  required?: boolean;
  /** Input element (Input, TextArea, Select, etc.) */
  children?: React.ReactNode;
  className?: string;
  style?: React.CSSProperties;
}

export declare function FormField(props: FormFieldProps): JSX.Element;

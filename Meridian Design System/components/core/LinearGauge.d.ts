import React from 'react';

export interface LinearGaugeProps {
  /** Current value (0-100 or 0-max). @default 0 */
  value?: number;
  /** Maximum value for the gauge. @default 100 */
  max?: number;
  /** Label displayed above the bar */
  label?: string;
  /** Show numeric percentage value. @default true */
  showValue?: boolean;
  /** Bar color (CSS color or var). @default "var(--accent)" */
  color?: string;
  /** Bar height in pixels. @default 8 */
  height?: number;
  className?: string;
  style?: React.CSSProperties;
}

export declare function LinearGauge(props: LinearGaugeProps): JSX.Element;

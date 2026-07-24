import React from 'react';

export interface GaugeProps {
  /** Current value (0-100 or 0-max). @default 0 */
  value?: number;
  /** Maximum value for the gauge. @default 100 */
  max?: number;
  /** Label displayed below the gauge */
  label?: string;
  /** SVG diameter in pixels. @default 120 */
  size?: number;
  /** Arc color (CSS color or var). @default "var(--accent)" */
  color?: string;
  /** Arc stroke width in pixels. @default 8 */
  thickness?: number;
  /** Show numeric value in center. @default true */
  showValue?: boolean;
  className?: string;
  style?: React.CSSProperties;
}

export declare function Gauge(props: GaugeProps): JSX.Element;

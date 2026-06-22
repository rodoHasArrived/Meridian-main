import React from "react";
export interface SparklineProps { points?: number[]; width?: number; height?: number; variant?: "line"|"area"|"bar"; color?: string; strokeWidth?: number; baseline?: number | null; }
export function Sparkline(props: SparklineProps): React.ReactElement | null;

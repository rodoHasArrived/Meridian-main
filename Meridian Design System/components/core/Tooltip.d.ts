import React from "react";
export interface TooltipProps { content: React.ReactNode; children: React.ReactNode; placement?: "above"|"below"|"left"|"right"; delay?: number; }
export function Tooltip(props: TooltipProps): React.ReactElement;

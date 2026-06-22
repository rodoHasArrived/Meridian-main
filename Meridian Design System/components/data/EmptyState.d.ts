import React from "react";
export interface EmptyStateProps { icon?: "table"|"search"|"chart"|"docs"|"inbox"; title?: string; detail?: string; action?: string; onAction?: () => void; compact?: boolean; }
export function EmptyState(props: EmptyStateProps): React.ReactElement;

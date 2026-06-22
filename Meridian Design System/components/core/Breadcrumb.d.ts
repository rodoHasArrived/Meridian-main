import React from "react";
export interface BreadcrumbItem { label: string; onClick?: () => void; }
export interface BreadcrumbProps { items?: BreadcrumbItem[]; separator?: string; }
export function Breadcrumb(props: BreadcrumbProps): React.ReactElement;

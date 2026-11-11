export type CapsuleState = "ready" | "loading" | "empty" | "stale" | "partial" | "offline" | "error";
export interface CapsuleProps extends React.HTMLAttributes<HTMLElement> {
  title: React.ReactNode; context?: React.ReactNode; state?: CapsuleState;
  size?: "xs" | "sm" | "md" | "lg"; height?: number | string; selected?: boolean;
  collapsible?: boolean; defaultCollapsed?: boolean; storageKey?: string;
  movable?: boolean; resizable?: boolean; tools?: React.ReactNode; footer?: React.ReactNode;
}
export declare function Capsule(props: CapsuleProps): JSX.Element;
export declare function CapsuleHeader(props: any): JSX.Element;
export declare function CapsuleBody(props: React.HTMLAttributes<HTMLDivElement>): JSX.Element;
export declare function CapsuleFooter(props: React.HTMLAttributes<HTMLElement>): JSX.Element;

import { Badge } from "@/components/ui/badge";
import { StatusBanner } from "@/components/ui/status-banner";

export interface ReportingCommandStatus {
  id: string;
  label: string;
  state: "running" | "success" | "error";
  message: string;
  details: string[];
}

export function ReportingScheduleField({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2">
      <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-mono text-xs text-foreground">{value}</dd>
    </div>
  );
}

export function ReportingCommandStatusView({ status }: { status: ReportingCommandStatus }) {
  return (
    <StatusBanner
      role="status"
      aria-label={`${status.label} status`}
      tone={status.state === "success" ? "success" : status.state === "error" ? "warning" : "info"}
      title={status.message}
      detail={status.details.length > 0 ? (
        <ul className="mt-2 space-y-1 text-xs">
          {status.details.map((detail) => (
            <li key={detail}>{detail}</li>
          ))}
        </ul>
      ) : null}
    />
  );
}

export function ReportingBackendReference({
  link
}: {
  link: {
    method: string;
    label: string;
    href: string;
    interactionLabel: string;
  };
}) {
  return (
    <>
      <Badge variant="outline">{link.interactionLabel}</Badge>
      <span className="min-w-0">
        <span className="block font-semibold text-foreground">{link.label}</span>
        <span className="block text-[11px] text-muted-foreground">Meridian service reference retained for diagnostics.</span>
      </span>
    </>
  );
}

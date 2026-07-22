import { Badge } from "@/components/ui/badge";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";

export interface ReportingCommandStatus {
  id: string;
  label: string;
  state: "running" | "success" | "error";
  message: string;
  details: string[];
  technicalDetails?: {
    label: string;
    description?: string;
    items: string[];
  };
}

export function ReportingScheduleField({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
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
      detail={status.details.length > 0 || status.technicalDetails ? (
        <div className="mt-2 space-y-2">
          {status.details.length > 0 ? (
            <ul className="space-y-1 text-xs">
              {status.details.map((detail) => (
                <li key={detail}>{detail}</li>
              ))}
            </ul>
          ) : null}
          {status.technicalDetails ? (
            <TechnicalDetails
              label={status.technicalDetails.label}
              description={status.technicalDetails.description}
            >
              <ul className="space-y-1 font-mono text-xs text-muted-foreground">
                {status.technicalDetails.items.map((item) => (
                  <li key={item} className="break-all">{item}</li>
                ))}
              </ul>
            </TechnicalDetails>
          ) : null}
        </div>
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
        <span className="block text-xs text-muted-foreground">Meridian service reference retained for diagnostics.</span>
      </span>
    </>
  );
}

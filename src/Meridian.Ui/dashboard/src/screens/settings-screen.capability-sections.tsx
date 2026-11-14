// Runtime feature capability and backend capability coverage sections of the Settings screen.
import { SeverityBadge } from "@/components/operations";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Toggle } from "@/components/ui/checkbox";
import { usesTrackedCaps } from "@/design-system/label-case";
import { cn } from "@/lib/utils";
import { type SettingsMutationConfirmation } from "@/screens/settings-mutation-confirm-dialog";
import { ExternalLink, MonitorCheck } from "lucide-react";
import { diagnosticToneClass } from "./settings-provider-integration";
import type { SettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import type { Dispatch, SetStateAction } from "react";
import { SettingsChip, capabilityTone } from "@/screens/settings-screen.section-primitives";
import { toSeverity } from "@/design-system/status-styles";

interface SettingsRuntimeCapabilitiesSectionProps {
  vm: SettingsScreenViewModel;
  onFeatureCapabilityToggle: ((capabilityKey: string, isEnabled: boolean) => Promise<void> | void) | undefined;
  setMutationConfirmation: Dispatch<SetStateAction<SettingsMutationConfirmation | null>>;
}

/** Runtime feature capability toggles. */
export function SettingsRuntimeCapabilitiesSection({ vm, onFeatureCapabilityToggle, setMutationConfirmation }: SettingsRuntimeCapabilitiesSectionProps) {
  return (
    <Card id="runtime-feature-capabilities" className="panel-surface scroll-mt-6">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <div className="eyebrow-label">Runtime controls</div>
            <CardTitle className="flex items-center gap-2 text-base">
              <MonitorCheck className="h-4 w-4 text-primary" />
              {vm.runtimeCapabilitySection.title}
            </CardTitle>
            <CardDescription className="mt-2">{vm.runtimeCapabilitySection.summary}</CardDescription>
          </div>
          <SeverityBadge
            status={toSeverity(vm.runtimeCapabilitySection.statusVariant)}
            label={vm.runtimeCapabilitySection.statusLabel}
          />
        </div>
      </CardHeader>
      <CardContent>
        {vm.runtimeCapabilitySection.toggles.length === 0 ? (
          <p className="text-sm text-muted-foreground">{vm.runtimeCapabilitySection.description}</p>
        ) : (
          <div className="grid gap-3 md:grid-cols-2" role="list" aria-label={vm.runtimeCapabilitySection.listLabel}>
            {vm.runtimeCapabilitySection.toggles.map((capability) => (
              <div
                key={capability.capabilityKey}
                role="listitem"
                className={cn("rounded-lg border px-4 py-4", diagnosticToneClass[capability.statusVariant === "success" ? "success" : "warning"])}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="font-mono text-[10px] text-muted-foreground">{capability.capabilityKey}</div>
                    <h3 className="mt-2 text-sm font-semibold text-foreground">{capability.displayName}</h3>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{capability.description}</p>
                  </div>
                  <SeverityBadge
                    status={toSeverity(capability.statusVariant)}
                    label={capability.statusLabel}
                    className="shrink-0"
                  />
                </div>
                <div className="mt-3 flex flex-wrap gap-2">
                  <SettingsChip label="Default" value={capability.defaultLabel} />
                  <SettingsChip label="Config" value={capability.overrideLabel} />
                </div>
                <div className="mt-4 grid gap-1">
                  <Toggle
                     checked={capability.isEnabled}
                     disabled={!capability.canToggle || !onFeatureCapabilityToggle}
                     onCheckedChange={(checked) => {
                       setMutationConfirmation({
                         id: `feature-capability-${capability.capabilityKey}`,
                         title: `${checked ? "Enable" : "Disable"} ${capability.displayName}?`,
                         description: `${checked ? "Enable" : "Disable"} this browser workstation capability override. Required policy remains server-owned and cannot be changed here.`,
                         confirmLabel: checked ? "Confirm enable" : "Confirm disable",
                         confirmAriaLabel: `${checked ? "Confirm enable" : "Confirm disable"} ${capability.displayName}`,
                         destructive: !checked,
                         run: () => onFeatureCapabilityToggle?.(capability.capabilityKey, checked)
                       });
                     }}
                    aria-label={capability.ariaLabel}
                    label={capability.canToggle ? "Allow this browser workstation capability" : "Required capability"}
                  />
                  {capability.disabledReason ? (
                    <p className="text-xs leading-5 text-muted-foreground">{capability.disabledReason}</p>
                  ) : null}
                </div>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

interface SettingsBackendCapabilitySectionProps {
  vm: SettingsScreenViewModel;
}

/** Backend capability coverage list. */
export function SettingsBackendCapabilitySection({ vm }: SettingsBackendCapabilitySectionProps) {
  return (
    <Card id="backend-capability-coverage" className="panel-surface scroll-mt-6">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <div className="eyebrow-label">Service reachability</div>
            <CardTitle className="flex items-center gap-2 text-base">
              <ExternalLink className="h-4 w-4 text-primary" />
              Capability coverage
            </CardTitle>
            <CardDescription className="mt-2">{vm.backendCapabilitySummary}</CardDescription>
          </div>
          <SeverityBadge
            status={toSeverity(vm.backendCapabilityStatusVariant)}
            label={vm.backendCapabilityStatusLabel}
          />
        </div>
      </CardHeader>
      <CardContent>
        <div className="grid gap-3 xl:grid-cols-2" role="list" aria-label={vm.backendCapabilityListLabel}>
          {vm.backendCapabilityGroups.map((group) => (
            <div
              key={group.id}
              role="listitem"
              className={cn("rounded-lg border px-4 py-4", diagnosticToneClass[capabilityTone(group.statusVariant)])}
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="eyebrow-label">{group.workspaceLabel} · {group.route}</div>
                  <h3 className="mt-2 text-sm font-semibold text-foreground">{group.title}</h3>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{group.description}</p>
                </div>
                <SeverityBadge
                  status={toSeverity(group.statusVariant)}
                  label={group.statusLabel}
                  className="shrink-0"
                />
              </div>
              <div className="mt-3 flex flex-wrap gap-2">
                <SettingsChip label="Mapped" value={group.endpointCountLabel} />
                <SettingsChip label="Loaded" value={group.loadedCountLabel} />
              </div>
              <p className="mt-3 text-xs leading-5 text-foreground/75">{group.statusDetail}</p>
              <div className="mt-4 grid gap-2 sm:grid-cols-2">
                {group.endpoints.map((endpoint) => endpoint.isBrowserNavigable ? (
                  <a
                    key={endpoint.id}
                    href={endpoint.href}
                    target="_blank"
                    rel="noreferrer"
                    aria-label={endpoint.ariaLabel}
                    className="flex min-w-0 items-start gap-2 rounded-md border border-border/60 bg-background/45 px-3 py-2 text-xs transition-colors hover:bg-secondary/45 focus:outline-hidden focus:ring-2 focus:ring-primary/40"
                  >
                    <EndpointReference endpoint={endpoint} />
                  </a>
                ) : (
                  <div
                    key={endpoint.id}
                    role="group"
                    aria-label={endpoint.ariaLabel}
                    className="flex min-w-0 items-start gap-2 rounded-md border border-border/60 bg-secondary/20 px-3 py-2 text-xs"
                  >
                    <EndpointReference endpoint={endpoint} />
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

function EndpointReference({
  endpoint
}: {
  endpoint: {
    method: string;
    label: string;
    href: string;
    interactionLabel: string;
  };
}) {
  return (
    <>
      <Badge variant="outline" className="shrink-0">{endpoint.method}</Badge>
      <span className="min-w-0">
        <span className="block font-semibold text-foreground">{endpoint.label}</span>
        <span className="mt-1 block break-all font-mono text-[10px] leading-4 text-muted-foreground">
          {endpoint.href}
        </span>
        <span className={cn("mt-1 inline-flex rounded-sm border border-border/60 px-1.5 py-0.5 text-[10px] text-muted-foreground", usesTrackedCaps(endpoint.interactionLabel) && "uppercase")}>
          {endpoint.interactionLabel}
        </span>
      </span>
    </>
  );
}

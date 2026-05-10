import { AlertTriangle, Download, ExternalLink, FileText, ListChecks, Network, RefreshCcw, ShieldCheck } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import {
  mapStatusTone,
  useEvidenceWorkbenchViewModel,
  type EvidencePacketActionTone,
  type EvidencePacketActionViewModel,
  type EvidenceStatusTone
} from "@/screens/evidence-workbench-screen.view-model";
import type { EvidenceNode } from "@/types";

const badgeVariant: Record<EvidenceStatusTone, "success" | "warning" | "danger" | "outline"> = {
  success: "success",
  warning: "warning",
  danger: "danger",
  muted: "outline"
};

const actionBadgeVariant: Record<EvidencePacketActionTone, "success" | "warning" | "danger" | "outline"> = {
  primary: "outline",
  success: "success",
  warning: "warning",
  danger: "danger",
  muted: "outline"
};

export function EvidenceWorkbenchScreen() {
  const location = useLocation();
  const vm = useEvidenceWorkbenchViewModel(location.search);

  if (vm.loading) {
    return (
      <Card className="panel-surface" role="status" aria-busy="true" aria-live="polite">
        <CardHeader>
          <CardTitle>Loading evidence</CardTitle>
          <CardDescription>{vm.loadingLabel}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-6">
      <section
        role="region"
        aria-label="Evidence workbench context"
        className="panel-surface-strong flex flex-wrap items-start justify-between gap-4 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Evidence workbench</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            {vm.title}
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">{vm.subtitle}</p>
          {vm.error ? (
            <p role="alert" className="mt-2 inline-flex items-center gap-2 text-sm text-danger">
              <AlertTriangle className="h-4 w-4" aria-hidden="true" />
              {vm.error}
            </p>
          ) : null}
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <Badge variant={badgeVariant[vm.statusTone]} dot>{vm.statusLabel}</Badge>
          <Badge variant="outline">{vm.scoreLabel}</Badge>
          <Badge variant="outline">{vm.generatedLabel}</Badge>
          {vm.sourceWorkflowHref && vm.sourceWorkflowLabel && vm.sourceWorkflowAriaLabel ? (
            <Button asChild variant="outline" size="sm">
              <Link to={vm.sourceWorkflowHref} aria-label={vm.sourceWorkflowAriaLabel}>
                <ExternalLink className="h-4 w-4" aria-hidden="true" />
                {vm.sourceWorkflowLabel}
              </Link>
            </Button>
          ) : null}
        </div>
      </section>

      {!vm.hasSelection ? (
        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Network className="h-5 w-5 text-primary" aria-hidden="true" />
                  Evidence subjects
                </CardTitle>
                <CardDescription>Select a subject to inspect completeness, stale evidence, and lineage.</CardDescription>
              </div>
              <Badge variant="outline">{vm.subjectsSummaryLabel}</Badge>
            </div>
          </CardHeader>
          <CardContent>
            {vm.hasSubjects ? (
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" role="list" aria-label={vm.subjectsRegionLabel}>
                {vm.subjects.map((subject) => (
                  <div key={`${subject.subjectKind}:${subject.subjectId}`} role="listitem">
                    <Link
                      to={vm.openSubjectHref(subject)}
                      className="block rounded-md border border-border/70 bg-secondary/25 px-4 py-3 transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                    >
                      <span className="block font-semibold text-foreground">{subject.label}</span>
                      <span className="mt-1 block font-mono text-xs text-muted-foreground">
                        {subject.subjectKind}/{subject.subjectId}
                      </span>
                      <span className="mt-3 inline-flex">
                        <Badge variant="outline">{subject.workspace}</Badge>
                      </span>
                    </Link>
                  </div>
                ))}
              </div>
            ) : (
              <div
                role="status"
                className="rounded-md border border-dashed border-border/80 bg-secondary/20 px-4 py-4 text-sm text-muted-foreground"
              >
                <div className="font-semibold text-foreground">{vm.subjectEmptyTitle}</div>
                <p className="mt-1 max-w-2xl leading-6">{vm.subjectEmptyDetail}</p>
                <Button asChild variant="outline" size="sm" className="mt-3">
                  <Link to={vm.subjectEmptyActionHref} aria-label={vm.subjectEmptyActionAriaLabel}>
                    {vm.subjectEmptyActionLabel}
                  </Link>
                </Button>
              </div>
            )}
          </CardContent>
        </Card>
      ) : null}

      {vm.hasPacket && vm.packet ? (
        <>
          <section className="grid gap-4 lg:grid-cols-[0.85fr_1.15fr]">
            <Card className="panel-surface">
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <ShieldCheck className="h-5 w-5 text-primary" aria-hidden="true" />
                  Completeness
                </CardTitle>
                <CardDescription>
                  Validation reads the current packet graph and does not mutate source workflows.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="grid gap-3 sm:grid-cols-2">
                  <EvidenceMetric label="Required" value={String(vm.packet.completeness.requiredIds.length)} />
                  <EvidenceMetric label="Ready" value={String(vm.packet.completeness.readyIds.length)} />
                  <EvidenceMetric label="Missing" value={String(vm.missingEvidence.length)} tone={vm.missingEvidence.length ? "danger" : "success"} />
                  <EvidenceMetric label="Stale" value={String(vm.staleEvidence.length)} tone={vm.staleEvidence.length ? "warning" : "success"} />
                </div>
                <div className="flex flex-wrap gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    onClick={vm.validatePacket}
                    busy={vm.validateCommand.busy}
                    busyLabel={vm.validateCommand.busyLabel}
                    disabled={vm.validateCommand.disabled}
                    disabledReason={vm.validateCommand.disabledReason}
                    aria-label={vm.validateCommand.ariaLabel}
                  >
                    <RefreshCcw className="h-4 w-4" aria-hidden="true" />
                    {vm.validateCommand.label}
                  </Button>
                  <Button
                    type="button"
                    onClick={vm.exportManifest}
                    busy={vm.exportCommand.busy}
                    busyLabel={vm.exportCommand.busyLabel}
                    disabled={vm.exportCommand.disabled}
                    disabledReason={vm.exportCommand.disabledReason}
                    aria-label={vm.exportCommand.ariaLabel}
                  >
                    <Download className="h-4 w-4" aria-hidden="true" />
                    {vm.exportCommand.label}
                  </Button>
                </div>
                {vm.validationResult ? (
                  <p role="status" className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                    Validation returned {vm.validationResult.score}% completeness with {vm.validationResult.blockingWorkItemIds.length} blocking item(s).
                  </p>
                ) : null}
                {vm.exportResult ? (
                  <div role="status" className="rounded-md border border-primary/30 bg-primary/10 px-3 py-2 text-sm leading-6 text-primary">
                    <div className="font-semibold">Manifest retained</div>
                    <div className="break-all font-mono text-xs">{vm.exportResult.manifestPath}</div>
                    <div className="mt-1 text-xs">
                      {vm.exportResult.evidenceCount} node(s), {vm.exportResult.warningCount} warning(s)
                    </div>
                  </div>
                ) : null}
              </CardContent>
            </Card>

            <Card className="panel-surface">
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <Network className="h-5 w-5 text-primary" aria-hidden="true" />
                  Lineage
                </CardTitle>
                <CardDescription>Graph edges show how evidence nodes support this workflow subject.</CardDescription>
              </CardHeader>
              <CardContent className="overflow-x-auto">
                {vm.packet.edges.length > 0 ? (
                  <table className="min-w-full text-left text-sm">
                    <thead className="text-xs uppercase tracking-[0.14em] text-muted-foreground">
                      <tr>
                        <th className="px-2 py-2">From</th>
                        <th className="px-2 py-2">Relationship</th>
                        <th className="px-2 py-2">To</th>
                        <th className="px-2 py-2">Reason</th>
                      </tr>
                    </thead>
                    <tbody>
                      {vm.packet.edges.map((edge) => (
                        <tr key={`${edge.fromId}-${edge.relationship}-${edge.toId}`} className="border-t border-border/60">
                          <td className="max-w-[16rem] break-all px-2 py-2 font-mono text-xs">{edge.fromId}</td>
                          <td className="px-2 py-2"><Badge variant="outline">{edge.relationship}</Badge></td>
                          <td className="max-w-[16rem] break-all px-2 py-2 font-mono text-xs">{edge.toId}</td>
                          <td className="px-2 py-2 text-muted-foreground">{edge.reason}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                ) : (
                  <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                    No graph edges are available for this packet.
                  </p>
                )}
              </CardContent>
            </Card>
          </section>

          <section className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
            <div className="space-y-4">
              {vm.nodeGroups.map((group) => (
                <Card key={group.id} className="panel-surface">
                  <CardHeader>
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div>
                        <CardTitle className="text-base">{group.label}</CardTitle>
                        <CardDescription>
                          {group.readyCount} ready, {group.reviewCount} needing review.
                        </CardDescription>
                      </div>
                      <Badge variant="outline">{group.nodes.length} node(s)</Badge>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-3">
                    {group.nodes.map((node) => <EvidenceNodeRow key={node.evidenceId} node={node} />)}
                  </CardContent>
                </Card>
              ))}
            </div>

            <aside className="space-y-4">
              {vm.hasPacketActions ? (
                <EvidenceActionPanel
                  actions={vm.packetActions}
                  label={vm.packetActionsLabel}
                  summary={vm.packetActionsSummaryLabel}
                  onValidate={vm.validatePacket}
                  onExport={vm.exportManifest}
                />
              ) : null}
              <EvidenceList title="Missing evidence" items={vm.missingEvidence} tone="danger" />
              <EvidenceList title="Stale evidence" items={vm.staleEvidence} tone="warning" />
              <EvidenceList title="Related work items" items={vm.relatedWorkItemIds} tone="muted" />
              <EvidenceList title="Warnings" items={vm.warnings} tone="warning" />
            </aside>
          </section>
        </>
      ) : vm.hasSelection && !vm.error ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>No evidence packet</CardTitle>
            <CardDescription>The selected evidence subject did not return a packet.</CardDescription>
          </CardHeader>
        </Card>
      ) : null}
    </div>
  );
}

function EvidenceMetric({ label, value, tone = "muted" }: { label: string; value: string; tone?: EvidenceStatusTone }) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2">
      <div className="eyebrow-label">{label}</div>
      <div className={cn("mt-1 font-mono text-lg font-semibold", metricToneClass[tone])}>{value}</div>
    </div>
  );
}

function EvidenceNodeRow({ node }: { node: EvidenceNode }) {
  const tone = mapStatusTone(node.status);
  return (
    <div className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="font-semibold text-foreground">{node.kind}</div>
          <div className="mt-1 break-all font-mono text-xs text-muted-foreground">{node.evidenceId}</div>
        </div>
        <Badge variant={badgeVariant[tone]}>{node.status}</Badge>
      </div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{node.summary}</p>
      <div className="mt-3 flex flex-wrap gap-2">
        <Badge variant="outline">{node.sourceSystem}</Badge>
        <Badge variant={node.freshness.isStale ? "warning" : "success"}>
          {node.freshness.isStale ? "Stale" : "Fresh"}
        </Badge>
        {node.artifactRefs.length > 0 ? <Badge variant="outline">{node.artifactRefs.length} artifact(s)</Badge> : null}
      </div>
      {node.artifactRefs.length > 0 ? (
        <div className="mt-3 grid gap-2">
          {node.artifactRefs.map((artifact) => (
            <div key={artifact.artifactId} className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2 text-xs">
              <div className="flex items-center gap-2 font-semibold text-foreground">
                <FileText className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                {artifact.kind}
              </div>
              <div className="mt-1 break-all font-mono text-muted-foreground">
                {artifact.path ?? artifact.route ?? artifact.artifactId}
              </div>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function EvidenceActionPanel({
  actions,
  label,
  summary,
  onValidate,
  onExport
}: {
  actions: EvidencePacketActionViewModel[];
  label: string;
  summary: string;
  onValidate: () => Promise<void>;
  onExport: () => Promise<void>;
}) {
  return (
    <Card className="panel-surface" role="region" aria-label={label}>
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <ListChecks className="h-4 w-4 text-primary" aria-hidden="true" />
            {label}
          </CardTitle>
          <Badge variant="outline">{summary}</Badge>
        </div>
      </CardHeader>
      <CardContent>
        <ul className="space-y-3">
          {actions.map((action) => (
            <li key={action.id} className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <div className="font-semibold text-foreground">{action.label}</div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{action.detail}</p>
                </div>
                <Badge variant={actionBadgeVariant[action.tone]}>{action.targetLabel}</Badge>
              </div>
              <div className="mt-3">
                {action.control === "validate" ? (
                  <Button
                    type="button"
                    variant={buttonVariantForAction(action.tone)}
                    size="sm"
                    onClick={() => void onValidate()}
                    busy={action.busy}
                    busyLabel={action.busyLabel}
                    disabled={action.disabled}
                    disabledReason={action.disabledReason}
                    aria-label={action.ariaLabel}
                  >
                    <RefreshCcw className="h-4 w-4" aria-hidden="true" />
                    {action.commandLabel}
                  </Button>
                ) : action.control === "export" ? (
                  <Button
                    type="button"
                    variant={buttonVariantForAction(action.tone)}
                    size="sm"
                    onClick={() => void onExport()}
                    busy={action.busy}
                    busyLabel={action.busyLabel}
                    disabled={action.disabled}
                    disabledReason={action.disabledReason}
                    aria-label={action.ariaLabel}
                  >
                    <Download className="h-4 w-4" aria-hidden="true" />
                    {action.commandLabel}
                  </Button>
                ) : (
                  <Button asChild variant={buttonVariantForAction(action.tone)} size="sm">
                    <Link to={action.href} aria-label={action.ariaLabel}>
                      <ExternalLink className="h-4 w-4" aria-hidden="true" />
                      {action.commandLabel}
                    </Link>
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}

function EvidenceList({
  title,
  items,
  tone
}: {
  title: string;
  items: string[];
  tone: EvidenceStatusTone;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle className="text-base">{title}</CardTitle>
          <Badge variant={badgeVariant[tone]}>{items.length}</Badge>
        </div>
      </CardHeader>
      <CardContent>
        {items.length > 0 ? (
          <ul className="space-y-2 text-sm">
            {items.map((item) => (
              <li key={item} className="break-all rounded-md border border-border/70 bg-secondary/25 px-3 py-2 font-mono text-xs">
                {item}
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground">None.</p>
        )}
      </CardContent>
    </Card>
  );
}

const metricToneClass: Record<EvidenceStatusTone, string> = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-foreground"
};

function buttonVariantForAction(tone: EvidencePacketActionTone) {
  if (tone === "primary" || tone === "success") {
    return "default";
  }
  if (tone === "danger") {
    return "destructive";
  }
  return "outline";
}

import { AlertTriangle, Download, ExternalLink, FileText, ListChecks, Network, RefreshCcw, ShieldCheck } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import {
  useEvidenceLineageSelectionViewModel,
  useEvidenceNodeSelectionViewModel,
  useEvidenceWorkbenchViewModel,
  type EvidenceLineageDetailViewModel,
  type EvidenceLineagePanelViewModel,
  type EvidenceLineageRowViewModel,
  type EvidenceAssurancePanelViewModel,
  type EvidenceProofChainPanelViewModel,
  type EvidenceSlaAssessmentRowViewModel,
  type EvidenceNodeDetailViewModel,
  type EvidenceNodeGroupViewModel,
  type EvidenceNodeRowViewModel,
  type EvidencePacketActionTone,
  type EvidencePacketActionViewModel,
  type EvidenceStatusTone
} from "@/screens/evidence-workbench-screen.view-model";

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
            <div role="alert" className="mt-3 rounded-md border border-danger/40 bg-danger/10 px-3 py-3 text-sm text-danger">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="inline-flex items-center gap-2 font-medium">
                    <AlertTriangle className="h-4 w-4" aria-hidden="true" />
                    {vm.error.summary}
                  </p>
                  {vm.error.details.length > 0 ? (
                    <ul className="mt-2 space-y-1 text-xs leading-5 text-danger/90">
                      {vm.error.details.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={vm.reloadEvidence}
                  busy={vm.reloadCommand.busy}
                  busyLabel={vm.reloadCommand.busyLabel}
                  disabled={vm.reloadCommand.disabled}
                  disabledReason={vm.reloadCommand.disabledReason}
                  aria-label={vm.reloadCommand.ariaLabel}
                >
                  <RefreshCcw className="h-4 w-4" aria-hidden="true" />
                  {vm.reloadCommand.label}
                </Button>
              </div>
            </div>
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

      {vm.showSubjectPicker ? (
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
            <div className="space-y-4">
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
                  {vm.exportResultDetail ? (
                    <div role="status" className="rounded-md border border-primary/30 bg-primary/10 px-3 py-2 text-sm leading-6 text-primary">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div className="min-w-0">
                          <div className="font-semibold">{vm.exportResultDetail.title}</div>
                          <div className="mt-1 break-all font-mono text-xs">{vm.exportResultDetail.manifestPath}</div>
                          <div className="mt-1 text-xs">{vm.exportResultDetail.summaryLabel}</div>
                          {vm.exportResultDetail.vaultIdLabel || vm.exportResultDetail.storageKindLabel ? (
                            <div className="mt-2 flex flex-wrap gap-2">
                              {vm.exportResultDetail.vaultIdLabel ? (
                                <Badge variant="outline">{vm.exportResultDetail.vaultIdLabel}</Badge>
                              ) : null}
                              {vm.exportResultDetail.storageKindLabel ? (
                                <Badge variant="outline">{vm.exportResultDetail.storageKindLabel}</Badge>
                              ) : null}
                            </div>
                          ) : null}
                        </div>
                        {vm.exportResultDetail.routeHref && vm.exportResultDetail.routeLabel && vm.exportResultDetail.routeAriaLabel ? (
                          <Button asChild variant="outline" size="sm">
                            <a
                              href={vm.exportResultDetail.routeHref}
                              target="_blank"
                              rel="noreferrer"
                              aria-label={vm.exportResultDetail.routeAriaLabel}
                            >
                              <ExternalLink className="h-4 w-4" aria-hidden="true" />
                              {vm.exportResultDetail.routeLabel}
                            </a>
                          </Button>
                        ) : null}
                      </div>
                      {vm.exportResultDetail.artifactRows.length > 0 ? (
                        <ul className="mt-3 grid gap-2" aria-label="Retained vault artifacts">
                          {vm.exportResultDetail.artifactRows.map((artifact) => (
                            <li
                              key={artifact.id}
                              aria-label={artifact.ariaLabel}
                              className="rounded-sm border border-primary/30 bg-background/40 px-2.5 py-2 text-xs"
                            >
                              <div className="flex flex-wrap items-center gap-2 font-semibold">
                                <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                                <span>{artifact.kind}</span>
                                <Badge variant="success">{artifact.sizeLabel}</Badge>
                              </div>
                              <div className="mt-1 break-all font-mono">{artifact.relativePath}</div>
                              <div className="mt-2 grid gap-1 font-mono text-[0.7rem] sm:grid-cols-2">
                                <span className="break-all">{artifact.hashLabel}</span>
                                <span className="break-all">{artifact.canonicalSubjectLabel}</span>
                                <span className="break-all">{artifact.sourceLabel}</span>
                                <span>{artifact.retainedLabel}</span>
                              </div>
                            </li>
                          ))}
                        </ul>
                      ) : null}
                      {vm.exportResultDetail.supportRequestRows.length > 0 ? (
                        <ul className="mt-3 grid gap-2" aria-label="Evidence vault support requests">
                          {vm.exportResultDetail.supportRequestRows.map((request) => (
                            <li
                              key={request.id}
                              aria-label={request.ariaLabel}
                              className="rounded-sm border border-primary/30 bg-background/40 px-2.5 py-2 text-xs"
                            >
                              <div className="flex flex-wrap items-center gap-2 font-semibold">
                                <ListChecks className="h-3.5 w-3.5" aria-hidden="true" />
                                <span>{request.requestKindLabel}</span>
                                <Badge variant={badgeVariant[request.severityTone]}>{request.severityLabel}</Badge>
                                <Badge variant="outline">{request.statusLabel}</Badge>
                              </div>
                              <div className="mt-1 break-all font-mono">{request.evidenceLabel}</div>
                              <p className="mt-1 text-xs leading-5 text-primary/90">{request.summary}</p>
                              <div className="mt-2 grid gap-1 font-mono text-[0.7rem] sm:grid-cols-2">
                                <span className="break-all">{request.evidenceKindLabel}</span>
                                <span className="break-all">{request.sourceLabel}</span>
                                <span className="break-all">{request.workItemLabel}</span>
                                <span className="break-all">{request.blockedOutputLabel}</span>
                              </div>
                            </li>
                          ))}
                        </ul>
                      ) : null}
                    </div>
                  ) : null}
                </CardContent>
              </Card>
              <EvidenceProofChainPanel panel={vm.proofChainPanel} />
            </div>

            <Card className="panel-surface">
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <Network className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.lineagePanel.title}
                </CardTitle>
                <CardDescription>{vm.lineagePanel.description}</CardDescription>
              </CardHeader>
              <CardContent>
                <EvidenceLineagePanel panel={vm.lineagePanel} />
              </CardContent>
            </Card>
          </section>

          <section className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
            <div className="space-y-4">
              {vm.nodeGroups.map((group) => (
                <EvidenceNodeGroupPanel key={group.id} group={group} />
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
              <EvidenceAssurancePanel panel={vm.assurancePanel} />
              <EvidenceList title="Orphan evidence" items={vm.orphanEvidence} tone={vm.assurancePanel.orphanTone} />
              <EvidenceList title="SLA breaches" items={vm.slaBreaches} tone={vm.slaBreaches.length ? "warning" : "success"} />
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

function EvidenceLineagePanel({ panel }: { panel: EvidenceLineagePanelViewModel }) {
  const selection = useEvidenceLineageSelectionViewModel(panel);

  if (!panel.hasRows) {
    return (
      <div
        role={panel.emptyRole}
        aria-live={panel.emptyAriaLive}
        className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
      >
        <div className="font-semibold">{panel.emptyTitle}</div>
        <p className="mt-1 text-warning/90">{panel.emptyDetail}</p>
      </div>
    );
  }

  return (
    <div className="grid gap-3 xl:grid-cols-[minmax(0,1.15fr)_minmax(16rem,0.85fr)]">
      <DenseDataTable
        columns={lineageColumns}
        rows={panel.rows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => row.ariaLabel}
        getRowSelectAriaLabel={(row) => row.selectAriaLabel}
        getRowAriaControls={() => panel.detailPanelId}
        getRowAriaExpanded={(row) => row.id === selection.selectedRowId}
        onRowSelect={(row) => selection.selectRow(row.id)}
        selectedRowId={selection.selectedRowId}
        emptyText={panel.emptyTitle}
        ariaLabel={panel.tableLabel}
        caption={panel.summaryLabel}
      />
      {selection.selectedDetail ? (
        <EvidenceLineageDetailPanel detail={selection.selectedDetail} id={panel.detailPanelId} />
      ) : null}
    </div>
  );
}

const lineageColumns: DenseDataTableColumn<EvidenceLineageRowViewModel>[] = [
  {
    id: "from",
    label: "From",
    className: "max-w-[16rem] break-all font-mono text-xs",
    render: (row) => row.fromId
  },
  {
    id: "relationship",
    label: "Relationship",
    render: (row) => <Badge variant="outline">{row.relationshipLabel}</Badge>
  },
  {
    id: "to",
    label: "To",
    className: "max-w-[16rem] break-all font-mono text-xs",
    render: (row) => row.toId
  },
  {
    id: "reason",
    label: "Reason",
    className: "text-muted-foreground",
    render: (row) => row.reason
  }
];

function EvidenceLineageDetailPanel({ detail, id }: { detail: EvidenceLineageDetailViewModel; id: string }) {
  return (
    <aside id={id} className="row-detail-panel h-fit min-w-0" role="region" aria-label={detail.ariaLabel}>
      <div className="head">{detail.eyebrow}</div>
      <div className="body space-y-3">
        <div className="min-w-0">
          <h3 className="font-semibold text-foreground">{detail.title}</h3>
          <p className="mt-1 break-all font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">{detail.description}</p>
        </div>
        <dl className="grid gap-2">
          {detail.fields.map((field) => (
            <div key={field.label} className="rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-2">
              <dt className="eyebrow-label">{field.label}</dt>
              <dd className="mt-1 break-all font-mono text-xs text-foreground">{field.value}</dd>
            </div>
          ))}
        </dl>
      </div>
    </aside>
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

function EvidenceProofChainPanel({ panel }: { panel: EvidenceProofChainPanelViewModel }) {
  return (
    <Card className="panel-surface" role="region" aria-label={panel.title}>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <Network className="h-4 w-4 text-primary" aria-hidden="true" />
              {panel.title}
            </CardTitle>
            <CardDescription>{panel.summaryLabel}</CardDescription>
          </div>
          <Badge variant={badgeVariant[panel.statusTone]}>{panel.statusLabel}</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <EvidenceMetric label="Layer coverage" value={panel.coverageLabel} tone={panel.statusTone} />
        {panel.hasLayers ? (
          <ul className="grid gap-2" aria-label="Operational evidence proof-chain layers">
            {panel.rows.map((row) => (
              <li
                key={row.id}
                aria-label={row.ariaLabel}
                className="rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-2 text-xs"
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-semibold text-foreground">{row.label}</span>
                  <span className="flex flex-wrap gap-2">
                    <Badge variant={badgeVariant[row.statusTone]}>{row.statusLabel}</Badge>
                    <Badge variant="outline">{row.coverageLabel}</Badge>
                  </span>
                </div>
                <p className="mt-1 leading-5 text-muted-foreground">{row.summary}</p>
                <div className="mt-2 flex flex-wrap gap-2">
                  <Badge variant="success">{row.readyLabel}</Badge>
                  <Badge variant={row.reviewLabel.startsWith("0 ") ? "outline" : "warning"}>{row.reviewLabel}</Badge>
                  <Badge variant={row.missingLabel.startsWith("0 ") ? "outline" : "danger"}>{row.missingLabel}</Badge>
                </div>
                <div className="mt-2 break-words font-mono text-[0.7rem] text-muted-foreground">{row.kindsLabel}</div>
              </li>
            ))}
          </ul>
        ) : (
          <p className="rounded-sm border border-dashed border-border/70 bg-secondary/20 px-2.5 py-2 text-sm text-muted-foreground">
            No proof-chain layers returned.
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function EvidenceNodeGroupPanel({ group }: { group: EvidenceNodeGroupViewModel }) {
  const selection = useEvidenceNodeSelectionViewModel(group);

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle className="text-base">{group.label}</CardTitle>
            <CardDescription>{group.summaryLabel}</CardDescription>
          </div>
          <div className="flex flex-wrap justify-end gap-2">
            <Badge variant="success">{group.readyCount} ready</Badge>
            <Badge variant={group.reviewCount ? "warning" : "outline"}>{group.reviewCount} review</Badge>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        {group.hasRows ? (
          <div className="grid gap-3 2xl:grid-cols-[minmax(0,1.2fr)_minmax(18rem,0.8fr)]">
            <DenseDataTable
              columns={nodeColumns}
              rows={group.rows}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowSelectAriaLabel={(row) => row.selectAriaLabel}
              getRowAriaControls={() => group.detailPanelId}
              getRowAriaExpanded={(row) => row.id === selection.selectedRowId}
              onRowSelect={(row) => selection.selectRow(row.id)}
              selectedRowId={selection.selectedRowId}
              emptyText={group.emptyTitle}
              ariaLabel={group.tableLabel}
              caption={group.summaryLabel}
            />
            {selection.selectedDetail ? (
              <EvidenceNodeDetailPanel detail={selection.selectedDetail} id={group.detailPanelId} />
            ) : null}
          </div>
        ) : (
          <div
            role="status"
            aria-live="polite"
            className="rounded-md border border-dashed border-border/80 bg-secondary/20 px-4 py-4 text-sm text-muted-foreground"
          >
            <div className="font-semibold text-foreground">{group.emptyTitle}</div>
            <p className="mt-1 max-w-2xl leading-6">{group.emptyDetail}</p>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

const nodeColumns: DenseDataTableColumn<EvidenceNodeRowViewModel>[] = [
  {
    id: "node",
    label: "Node",
    className: "min-w-[14rem]",
    render: (row) => (
      <div className="min-w-0">
        <div className="font-semibold text-foreground">{row.kindLabel}</div>
        <div className="mt-1 break-all font-mono text-[0.7rem] text-muted-foreground">{row.evidenceId}</div>
      </div>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <Badge variant={badgeVariant[row.statusTone]}>{row.statusLabel}</Badge>
  },
  {
    id: "source",
    label: "Source",
    className: "max-w-[13rem] break-words text-muted-foreground",
    render: (row) => row.sourceSystem
  },
  {
    id: "freshness",
    label: "Freshness",
    className: "min-w-[9rem]",
    render: (row) => <Badge variant={badgeVariant[row.freshnessTone]}>{row.freshnessLabel}</Badge>
  },
  {
    id: "artifacts",
    label: "Artifacts",
    render: (row) => row.artifactCountLabel
  },
  {
    id: "work-items",
    label: "Work items",
    render: (row) => row.workItemCountLabel
  }
];

function EvidenceNodeDetailPanel({ detail, id }: { detail: EvidenceNodeDetailViewModel; id: string }) {
  return (
    <aside
      id={id}
      className="row-detail-panel h-fit min-w-0"
      role="region"
      aria-label={detail.ariaLabel}
      aria-live="polite"
    >
      <div className="head flex flex-wrap items-center justify-between gap-2">
        <span>{detail.eyebrow}</span>
        <span className="flex flex-wrap gap-2">
          <Badge variant={badgeVariant[detail.statusTone]}>{detail.statusLabel}</Badge>
          <Badge variant={badgeVariant[detail.freshnessTone]}>{detail.freshnessLabel}</Badge>
        </span>
      </div>
      <div className="body space-y-4">
        <div className="min-w-0">
          <h3 className="font-semibold text-foreground">{detail.title}</h3>
          <p className="mt-1 break-all font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">{detail.description}</p>
        </div>
        <dl className="grid gap-2 sm:grid-cols-2">
          {detail.fields.map((field) => (
            <div key={field.label} className="rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-2">
              <dt className="eyebrow-label">{field.label}</dt>
              <dd className="mt-1 break-all font-mono text-xs text-foreground">{field.value}</dd>
            </div>
          ))}
        </dl>
        <section aria-label="Selected evidence artifacts" className="space-y-2">
          <div className="eyebrow-label">Artifacts</div>
          {detail.artifactRows.length > 0 ? (
            <ul className="grid gap-2">
              {detail.artifactRows.map((artifact) => (
                <li
                  key={artifact.id}
                  aria-label={artifact.ariaLabel}
                  className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2 text-xs"
                >
                  <div className="flex flex-wrap items-center gap-2 font-semibold text-foreground">
                    <FileText className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                    <span>{artifact.kind}</span>
                    <Badge variant={artifact.retainedLabel === "Retained" ? "success" : "outline"}>{artifact.retainedLabel}</Badge>
                  </div>
                  <div className="mt-1 break-all font-mono text-muted-foreground">{artifact.target}</div>
                  <div className="mt-2 grid gap-1 font-mono text-[0.7rem] text-muted-foreground sm:grid-cols-2">
                    <span>{artifact.generatedLabel}</span>
                    <span className="break-all">{artifact.hashLabel}</span>
                  </div>
                  {artifact.canonicalSubjectLabel ? (
                    <div className="mt-1 break-all font-mono text-[0.7rem] text-muted-foreground">
                      {artifact.canonicalSubjectLabel}
                    </div>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : (
            <p className="rounded-sm border border-dashed border-border/70 bg-secondary/20 px-2.5 py-2 text-sm text-muted-foreground">
              {detail.artifactEmptyText}
            </p>
          )}
        </section>
        <section aria-label="Selected evidence work items" className="space-y-2">
          <div className="eyebrow-label">Work items</div>
          {detail.workItemRows.length > 0 ? (
            <ul className="grid gap-2">
              {detail.workItemRows.map((item) => (
                <li
                  key={item.id}
                  aria-label={item.ariaLabel}
                  className="break-all rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-2 font-mono text-xs"
                >
                  {item.label}
                </li>
              ))}
            </ul>
          ) : (
            <p className="rounded-sm border border-dashed border-border/70 bg-secondary/20 px-2.5 py-2 text-sm text-muted-foreground">
              {detail.workItemEmptyText}
            </p>
          )}
        </section>
      </div>
    </aside>
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

function EvidenceAssurancePanel({ panel }: { panel: EvidenceAssurancePanelViewModel }) {
  return (
    <Card className="panel-surface" role="region" aria-label={panel.title}>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <ShieldCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              {panel.title}
            </CardTitle>
            <CardDescription>{panel.summaryLabel}</CardDescription>
          </div>
          <Badge variant={badgeVariant[panel.statusTone]}>{panel.scoreLabel}</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-2 sm:grid-cols-2">
          <EvidenceMetric label="Status" value={panel.statusLabel} tone={panel.statusTone} />
          <EvidenceMetric label="No orphan rule" value={panel.orphanSummaryLabel} tone={panel.orphanTone} />
        </div>
        <div className="flex flex-wrap gap-2">
          <Badge variant={badgeVariant[panel.orphanTone]}>{panel.noOrphanRuleLabel}</Badge>
          <Badge variant="outline">{panel.validationIssueLabel}</Badge>
        </div>
        {panel.componentRows.length > 0 ? (
          <ul className="space-y-2" aria-label="Assurance score components">
            {panel.componentRows.map((component) => (
              <li
                key={component.id}
                aria-label={component.ariaLabel}
                className="rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-2 text-xs"
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-semibold text-foreground">{component.label}</span>
                  <span className="flex flex-wrap gap-2">
                    <Badge variant={badgeVariant[component.statusTone]}>{component.statusLabel}</Badge>
                    <Badge variant="outline">{component.scoreLabel}</Badge>
                  </span>
                </div>
                <p className="mt-1 leading-5 text-muted-foreground">{component.detail}</p>
              </li>
            ))}
          </ul>
        ) : (
          <p className="rounded-sm border border-dashed border-border/70 bg-secondary/20 px-2.5 py-2 text-sm text-muted-foreground">
            Assurance components are not available for this packet.
          </p>
        )}
        <EvidenceSlaRows rows={panel.breachedSlaRows.length > 0 ? panel.breachedSlaRows : panel.slaRows.slice(0, 3)} />
      </CardContent>
    </Card>
  );
}

function EvidenceSlaRows({ rows }: { rows: EvidenceSlaAssessmentRowViewModel[] }) {
  if (rows.length === 0) {
    return (
      <p className="rounded-sm border border-dashed border-border/70 bg-secondary/20 px-2.5 py-2 text-sm text-muted-foreground">
        No evidence SLA assessments returned.
      </p>
    );
  }

  return (
    <ul className="space-y-2" aria-label="Evidence SLA assessments">
      {rows.map((row) => (
        <li
          key={row.id}
          aria-label={row.ariaLabel}
          className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2 text-xs"
        >
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="font-semibold text-foreground">{row.policyLabel}</span>
            <Badge variant={badgeVariant[row.tone]}>{row.breached ? "Breached" : "Fresh"}</Badge>
          </div>
          <div className="mt-1 break-all font-mono text-[0.7rem] text-muted-foreground">{row.evidenceId}</div>
          <div className="mt-2 flex flex-wrap gap-2">
            <Badge variant="outline">{row.evidenceKindLabel}</Badge>
            <Badge variant="outline">{row.ageLabel}</Badge>
            <Badge variant="outline">{row.freshnessLabel}</Badge>
            <Badge variant="outline">{row.severityLabel}</Badge>
          </div>
          <p className="mt-2 leading-5 text-muted-foreground">{row.message}</p>
        </li>
      ))}
    </ul>
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

import { BookCheck, Network, RefreshCcw, ShieldCheck } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { accountingToolingBadgeVariant, accountingToolingBorderClass, cashFlowTextClass } from "@/screens/accounting-screen.styles";
import type { AccountingConfigurationViewModel, AccountingRulesStudioPromotionReadinessViewModel } from "@/screens/accounting-screen.view-model";
import { cn } from "@/lib/utils";

export function AccountingConfigurationPanel({ view }: { view: AccountingConfigurationViewModel }) {
  return (
    <section className="workspace-section-band" aria-labelledby="accounting-configure-heading">
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Configure</p>
          <h3 id="accounting-configure-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={view.statusTone === "success" ? "success" : view.statusTone === "danger" ? "danger" : view.statusTone === "warning" ? "warning" : "outline"} dot>
            {view.statusLabel}
          </Badge>
          <Button
            size="sm"
            variant="outline"
            disabled={view.loading}
            disabledReason={view.loading ? "Configuration refresh is already in progress." : null}
            onClick={() => void view.refresh()}
          >
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Refresh
          </Button>
          <Button
            size="sm"
            disabled={!view.canActivate}
            disabledReason={view.activateDisabledReason}
            busy={view.activateBusy}
            busyLabel={view.activateButtonLabel}
            onClick={() => void view.activate()}
          >
            {view.activateButtonLabel}
          </Button>
        </div>
      </div>

      {view.errorText ? (
        <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
          <div className="font-semibold">{view.errorText}</div>
          {view.errorDetails.length > 0 ? (
            <ul className="mt-2 list-disc pl-4">
              {view.errorDetails.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
        {view.metricRows.map((metric) => (
          <div key={metric.id} className="panel-surface px-4 py-3">
            <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{metric.label}</div>
            <div className={cn("mt-2 font-mono text-xl font-semibold", cashFlowTextClass(metric.tone))}>{metric.value}</div>
            <p className="mt-2 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
          </div>
        ))}
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        {view.setupReadinessRows.map((row) => (
          <div key={row.id} className="rounded-md border border-border/70 bg-secondary/20 px-4 py-3">
            <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{row.label}</div>
            <div className={cn("mt-2 text-sm font-semibold", cashFlowTextClass(row.tone))}>{row.value}</div>
            <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.detail}</p>
          </div>
        ))}
      </div>
      <div className="flex flex-wrap items-center gap-3 rounded-md border border-border/70 bg-background px-4 py-3">
        <Button
          size="sm"
          variant="secondary"
          disabled={!view.canCreateLedgerBook}
          disabledReason={view.createLedgerBookDisabledReason}
          busy={view.createLedgerBookBusy}
          busyLabel={view.createLedgerBookButtonLabel}
          onClick={() => void view.createLedgerBookFromSetupCandidate()}
        >
          {view.createLedgerBookButtonLabel}
        </Button>
        {view.createLedgerBookStatusText ? (
          <p className="text-xs leading-5 text-muted-foreground">{view.createLedgerBookStatusText}</p>
        ) : null}
      </div>

      <Card className="panel-surface" aria-labelledby="accounting-ledger-books-heading">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle id="accounting-ledger-books-heading">Ledger book administration</CardTitle>
              <CardDescription>{view.ledgerBookSummaryLabel}</CardDescription>
            </div>
            <Badge variant={view.ledgerBookRows.some((book) => book.statusLabel === "Selected") ? "success" : view.ledgerBookRows.length > 0 ? "warning" : "outline"}>
              {view.ledgerBookRows.length} book{view.ledgerBookRows.length === 1 ? "" : "s"}
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          {view.ledgerBookEmptyText ? (
            <p className="text-sm text-muted-foreground">{view.ledgerBookEmptyText}</p>
          ) : (
            <div className="grid gap-3 lg:grid-cols-2" role="region" aria-label="Accounting ledger book catalog">
              {view.ledgerBookRows.map((book) => (
                <div key={book.id} className={cn("rounded-md border px-4 py-3", accountingToolingBorderClass(book.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{book.title}</div>
                      <div className="mt-1 font-mono text-[11px] uppercase tracking-[0.08em] text-muted-foreground">{book.id}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(book.tone)}>{book.statusLabel}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{book.description}</p>
                  <dl className="mt-3 grid gap-2 text-xs md:grid-cols-2">
                    <div>
                      <dt className="font-semibold text-muted-foreground">Basis</dt>
                      <dd className="mt-1 text-foreground">{book.subtitle}</dd>
                    </div>
                    <div>
                      <dt className="font-semibold text-muted-foreground">Policy</dt>
                      <dd className="mt-1 text-foreground">{book.policyLabel}</dd>
                    </div>
                    <div>
                      <dt className="font-semibold text-muted-foreground">Currency</dt>
                      <dd className="mt-1 text-foreground">{book.currencyLabel}</dd>
                    </div>
                    <div>
                      <dt className="font-semibold text-muted-foreground">Scope</dt>
                      <dd className="mt-1 break-words text-foreground">{book.scopeLabel}</dd>
                    </div>
                    <div>
                      <dt className="font-semibold text-muted-foreground">Updated</dt>
                      <dd className="mt-1 text-foreground">{book.updatedLabel}</dd>
                    </div>
                  </dl>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Card className="panel-surface" aria-labelledby="accounting-chart-account-editor-heading">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle id="accounting-chart-account-editor-heading">Chart account setup</CardTitle>
              <CardDescription>Author ledger-book chart nodes through the shared accounting configuration API.</CardDescription>
            </div>
            <Button
              size="sm"
              disabled={!view.chartAccountEditor.canSave}
              disabledReason={view.chartAccountEditor.saveDisabledReason}
              busy={view.chartAccountEditor.saveBusy}
              busyLabel={view.chartAccountEditor.saveButtonLabel}
              onClick={() => void view.chartAccountEditor.save()}
            >
              {view.chartAccountEditor.saveButtonLabel}
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            <FormRow label="Node id" labelFor="accounting-chart-node-id">
              <Input
                id="accounting-chart-node-id"
                value={view.chartAccountEditor.nodeIdValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ nodeId: event.currentTarget.value })}
              />
            </FormRow>
            <FormRow label="Account path" labelFor="accounting-chart-path">
              <Input
                id="accounting-chart-path"
                value={view.chartAccountEditor.pathValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ path: event.currentTarget.value })}
              />
            </FormRow>
            <FormRow label="Account name" labelFor="accounting-chart-account-name">
              <Input
                id="accounting-chart-account-name"
                value={view.chartAccountEditor.accountNameValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ accountName: event.currentTarget.value })}
              />
            </FormRow>
            <FormRow label="Account type" labelFor="accounting-chart-account-type">
              <Input
                id="accounting-chart-account-type"
                value={view.chartAccountEditor.accountTypeValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ accountType: event.currentTarget.value })}
              />
            </FormRow>
          </div>
          <div className="grid gap-3 md:grid-cols-3">
            <FormRow label="Parent path" labelFor="accounting-chart-parent-path">
              <Input
                id="accounting-chart-parent-path"
                value={view.chartAccountEditor.parentPathValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ parentPath: event.currentTarget.value })}
              />
            </FormRow>
            <FormRow label="Financial account id" labelFor="accounting-chart-financial-account-id">
              <Input
                id="accounting-chart-financial-account-id"
                value={view.chartAccountEditor.financialAccountIdValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ financialAccountId: event.currentTarget.value })}
              />
            </FormRow>
            <FormRow label="Retained evidence" labelFor="accounting-chart-evidence">
              <Input
                id="accounting-chart-evidence"
                value={view.chartAccountEditor.evidenceValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ evidenceText: event.currentTarget.value })}
              />
            </FormRow>
          </div>
          {view.chartAccountEditor.statusText ? (
            <p className="text-sm text-muted-foreground">{view.chartAccountEditor.statusText}</p>
          ) : null}
        </CardContent>
      </Card>

      <Card className="panel-surface" aria-labelledby="accounting-production-readiness-heading">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle id="accounting-production-readiness-heading" className="flex items-center gap-2">
                <ShieldCheck className="h-5 w-5 text-primary" aria-hidden="true" />
                {view.productionReadiness.title}
              </CardTitle>
              <CardDescription>{view.productionReadiness.scopeLabel}</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={accountingToolingBadgeVariant(view.productionReadiness.components.length === 0 ? "default" : view.productionReadiness.components.some((component) => component.tone === "danger") ? "danger" : view.productionReadiness.components.some((component) => component.tone === "warning") ? "warning" : "success")} dot>
                {view.productionReadiness.statusLabel}
              </Badge>
              <Badge variant="outline">{view.productionReadiness.scoreLabel}</Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {view.productionReadiness.errorText ? (
            <div role="alert" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
              <div className="font-semibold">{view.productionReadiness.errorText}</div>
              {view.productionReadiness.errorDetails.length > 0 ? (
                <ul className="mt-2 list-disc pl-4">
                  {view.productionReadiness.errorDetails.map((detail) => <li key={detail}>{detail}</li>)}
                </ul>
              ) : null}
            </div>
          ) : null}

          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Readiness</div>
              <div className={cn("mt-2 text-sm font-semibold", view.productionReadiness.blockerIssues.some((issue) => issue.tone === "danger") ? "text-danger" : view.productionReadiness.blockerIssues.length > 0 ? "text-warning" : "text-success")}>{view.productionReadiness.issueSummaryLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">{view.productionReadiness.generatedAtLabel}</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Ledger books</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.ledgerBookRolloutLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">Rollout evidence remains service-owned.</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">External GL</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.externalGlLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">Import, mapping, reconciliation, and guarded export posture.</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Dimensions</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.dimensionalReportingLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">{view.productionReadiness.dimensionalReportingEvidenceLabel}</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Control plane</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.components.length} component checks</div>
              <p className="mt-1 text-xs text-muted-foreground">Rules, posting, JE lifecycle, dimensions, close, and admin readiness.</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Tenant admin</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.tenantAdministrationLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">{view.productionReadiness.tenantAdministrationEvidenceLabel}</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Migration evidence</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.migrationArtifactSummaryLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">Retained migration run artifacts from the shared Accounting System store.</p>
            </div>
          </div>

          {view.productionReadiness.productionGapRows.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5" role="region" aria-label="Accounting production gap checklist">
              {view.productionReadiness.productionGapRows.map((gap) => (
                <div key={gap.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(gap.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="font-semibold text-foreground">{gap.label}</div>
                      <div className="mt-1 font-mono text-[11px] text-muted-foreground">{gap.id}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(gap.tone)}>{gap.statusLabel}</Badge>
                  </div>
                  <div className="mt-2 text-[11px] text-muted-foreground">{gap.severityLabel} | {gap.areaLabel}</div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{gap.summary}</p>
                  <p className="mt-2 text-xs leading-5 text-foreground">{gap.requiredAction}</p>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{gap.issueDetailLabel}</p>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{gap.blockingIssueLabel}</div>
                  <div className="mt-1 font-mono text-[11px] text-muted-foreground">{gap.routeLabel}</div>
                </div>
              ))}
            </div>
          ) : null}

          {view.productionReadiness.tenantAdministrationControls.length > 0 ? (
            <div className="grid gap-2 md:grid-cols-4" role="region" aria-label="Accounting tenant administration readiness controls">
              {view.productionReadiness.tenantAdministrationControls.map((control) => (
                <div key={control.id} className={cn("rounded-md border px-3 py-2", accountingToolingBorderClass(control.tone))}>
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-sm font-semibold text-foreground">{control.label}</span>
                    <Badge variant={accountingToolingBadgeVariant(control.tone)}>{control.statusLabel}</Badge>
                  </div>
                </div>
              ))}
            </div>
          ) : null}

          <div className="rounded-lg border border-border/70 bg-background/35 p-3" role="region" aria-label="Accounting production certification profile editor">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="font-semibold text-foreground">{view.productionCertificationProfile.title}</div>
                <div className="mt-1 font-mono text-xs text-muted-foreground">{view.productionCertificationProfile.scopeLabel}</div>
                <div className="mt-1 text-xs text-muted-foreground">{view.productionCertificationProfile.updatedLabel}</div>
              </div>
              <Button
                size="sm"
                disabled={!view.productionCertificationProfile.canSave}
                disabledReason={view.productionCertificationProfile.saveDisabledReason}
                busy={view.productionCertificationProfile.saveBusy}
                busyLabel={view.productionCertificationProfile.saveButtonLabel}
                onClick={() => void view.productionCertificationProfile.save()}
              >
                {view.productionCertificationProfile.saveButtonLabel}
              </Button>
            </div>
            <div className="mt-3 grid gap-2 md:grid-cols-4">
              {view.productionCertificationProfile.controls.map((control) => (
                <label key={control.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm">
                  <span className="flex items-center gap-2 font-semibold text-foreground">
                    <input
                      type="checkbox"
                      checked={control.checked}
                      onChange={(event) => view.productionCertificationProfile.updateControl(control.id, event.currentTarget.checked)}
                    />
                    {control.label}
                  </span>
                  <span className="mt-1 block text-xs text-muted-foreground">{control.description}</span>
                </label>
              ))}
            </div>
            <FormRow label="Retained certification evidence" labelFor="accounting-production-certification-evidence">
              <Input
                id="accounting-production-certification-evidence"
                value={view.productionCertificationProfile.evidenceValue}
                onChange={(event) => view.productionCertificationProfile.updateEvidence(event.currentTarget.value)}
                placeholder="evidence://accounting/production-certification"
              />
            </FormRow>
            {view.productionCertificationProfile.statusText ? (
              <p className={cn("text-sm", view.productionCertificationProfile.errorText ? "text-danger" : "text-muted-foreground")}>
                {view.productionCertificationProfile.statusText}
              </p>
            ) : null}
          </div>

          <div className="rounded-lg border border-border/70 bg-background/35 p-3" role="region" aria-label="Accounting tenant administration setup editor">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="font-semibold text-foreground">{view.tenantAdministrationProfile.title}</div>
                <div className="mt-1 font-mono text-xs text-muted-foreground">{view.tenantAdministrationProfile.scopeLabel}</div>
                <div className="mt-1 text-xs text-muted-foreground">{view.tenantAdministrationProfile.updatedLabel}</div>
              </div>
              <div className="flex flex-wrap gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={!view.tenantAdministrationProfile.canRetainSandboxProof}
                  disabledReason={view.tenantAdministrationProfile.sandboxDisabledReason}
                  busy={view.tenantAdministrationProfile.sandboxBusy}
                  busyLabel={view.tenantAdministrationProfile.sandboxButtonLabel}
                  onClick={() => void view.tenantAdministrationProfile.retainSandboxProof()}
                >
                  {view.tenantAdministrationProfile.sandboxButtonLabel}
                </Button>
                <Button
                  size="sm"
                  disabled={!view.tenantAdministrationProfile.canSave}
                  disabledReason={view.tenantAdministrationProfile.saveDisabledReason}
                  busy={view.tenantAdministrationProfile.saveBusy}
                  busyLabel={view.tenantAdministrationProfile.saveButtonLabel}
                  onClick={() => void view.tenantAdministrationProfile.save()}
                >
                  {view.tenantAdministrationProfile.saveButtonLabel}
                </Button>
              </div>
            </div>
            <div className="mt-3 grid gap-2 md:grid-cols-5">
              {view.tenantAdministrationProfile.controls.map((control) => (
                <label key={control.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm">
                  <span className="flex items-center gap-2 font-semibold text-foreground">
                    <input
                      type="checkbox"
                      checked={control.checked}
                      onChange={(event) => view.tenantAdministrationProfile.updateControl(control.id, event.currentTarget.checked)}
                    />
                    {control.label}
                  </span>
                  <span className="mt-1 block text-xs text-muted-foreground">{control.description}</span>
                </label>
              ))}
            </div>
            <div className="mt-3 grid gap-3 rounded-md border border-border/70 bg-background/45 px-3 py-3" role="region" aria-label="Accounting approval queue setup editor">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Approval queue setup</div>
              <div className="grid gap-3 md:grid-cols-3">
                <FormRow label="Queue id" labelFor="accounting-approval-queue-id">
                  <Input
                    id="accounting-approval-queue-id"
                    value={view.tenantAdministrationProfile.approvalQueueSetup.queueIdValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ queueId: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Display name" labelFor="accounting-approval-queue-display-name">
                  <Input
                    id="accounting-approval-queue-display-name"
                    value={view.tenantAdministrationProfile.approvalQueueSetup.displayNameValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ displayName: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Workflow kind" labelFor="accounting-approval-queue-workflow-kind">
                  <Input
                    id="accounting-approval-queue-workflow-kind"
                    value={view.tenantAdministrationProfile.approvalQueueSetup.workflowKindValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ workflowKind: event.currentTarget.value })}
                  />
                </FormRow>
              </div>
              <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_8rem_minmax(0,1.5fr)]">
                <FormRow label="Approval role" labelFor="accounting-approval-queue-role">
                  <Input
                    id="accounting-approval-queue-role"
                    value={view.tenantAdministrationProfile.approvalQueueSetup.requiredApprovalRoleValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ requiredApprovalRole: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Count" labelFor="accounting-approval-queue-count">
                  <Input
                    id="accounting-approval-queue-count"
                    type="number"
                    min={1}
                    value={view.tenantAdministrationProfile.approvalQueueSetup.requiredApprovalCountValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ requiredApprovalCount: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Evidence requirement" labelFor="accounting-approval-queue-evidence">
                  <Input
                    id="accounting-approval-queue-evidence"
                    value={view.tenantAdministrationProfile.approvalQueueSetup.evidenceRequirementValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ evidenceRequirement: event.currentTarget.value })}
                  />
                </FormRow>
              </div>
              <FormRow label="Segregation policy" labelFor="accounting-approval-queue-segregation">
                <Input
                  id="accounting-approval-queue-segregation"
                  value={view.tenantAdministrationProfile.approvalQueueSetup.segregationPolicyValue}
                  onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ segregationPolicy: event.currentTarget.value })}
                />
              </FormRow>
            </div>
            <div className="mt-3 grid gap-3 rounded-md border border-border/70 bg-background/45 px-3 py-3" role="region" aria-label="Accounting dimension mapping setup editor">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Dimension mapping setup</div>
              <div className="grid gap-3 md:grid-cols-3">
                <FormRow label="Mapping id" labelFor="accounting-dimension-mapping-id">
                  <Input
                    id="accounting-dimension-mapping-id"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.mappingIdValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ mappingId: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Display name" labelFor="accounting-dimension-mapping-display-name">
                  <Input
                    id="accounting-dimension-mapping-display-name"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.displayNameValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ displayName: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Provider id" labelFor="accounting-dimension-mapping-provider-id">
                  <Input
                    id="accounting-dimension-mapping-provider-id"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.providerIdValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ providerId: event.currentTarget.value })}
                  />
                </FormRow>
              </div>
              <div className="grid gap-3 lg:grid-cols-2">
                <FormRow label="Meridian dimensions" labelFor="accounting-dimension-mapping-meridian-dimensions">
                  <textarea
                    id="accounting-dimension-mapping-meridian-dimensions"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.meridianDimensionsValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ meridianDimensionsText: event.currentTarget.value })}
                    className="min-h-28 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                    placeholder="fundId=fund-alpha&#10;bookId=book-primary&#10;costCenterId=fund-accounting"
                  />
                </FormRow>
                <FormRow label="Provider dimensions" labelFor="accounting-dimension-mapping-provider-dimensions">
                  <textarea
                    id="accounting-dimension-mapping-provider-dimensions"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.providerDimensionsValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ providerDimensionsText: event.currentTarget.value })}
                    className="min-h-28 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                    placeholder="Class=fund-alpha&#10;Book=book-primary&#10;Department=fund-accounting"
                  />
                </FormRow>
              </div>
              <FormRow label="Evidence requirement" labelFor="accounting-dimension-mapping-evidence">
                <Input
                  id="accounting-dimension-mapping-evidence"
                  value={view.tenantAdministrationProfile.dimensionMappingSetup.evidenceRequirementValue}
                  onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ evidenceRequirement: event.currentTarget.value })}
                />
              </FormRow>
            </div>
            <FormRow label="Retained setup evidence" labelFor="accounting-tenant-admin-evidence">
              <Input
                id="accounting-tenant-admin-evidence"
                value={view.tenantAdministrationProfile.evidenceValue}
                onChange={(event) => view.tenantAdministrationProfile.updateEvidence(event.currentTarget.value)}
                placeholder="evidence://tenant-admin/setup"
              />
            </FormRow>
            {view.tenantAdministrationProfile.statusText ? (
              <p className={cn("text-sm", view.tenantAdministrationProfile.errorText ? "text-danger" : "text-muted-foreground")}>
                {view.tenantAdministrationProfile.statusText}
              </p>
            ) : null}
            {view.tenantAdministrationProfile.sandboxStatusText ? (
              <p className={cn("text-sm", view.tenantAdministrationProfile.sandboxStatusText.includes("failed") ? "text-danger" : "text-muted-foreground")}>
                {view.tenantAdministrationProfile.sandboxStatusText}
              </p>
            ) : null}
          </div>

          <div className="rounded-lg border border-border/70 bg-background/35 p-3" role="region" aria-label="Accounting external GL provider mapping editor">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="font-semibold text-foreground">{view.externalGlMappingProfile.title}</div>
                <div className="mt-1 font-mono text-xs text-muted-foreground">{view.externalGlMappingProfile.scopeLabel}</div>
              </div>
              <Button
                size="sm"
                disabled={!view.externalGlMappingProfile.canSave}
                disabledReason={view.externalGlMappingProfile.saveDisabledReason}
                busy={view.externalGlMappingProfile.saveBusy}
                busyLabel={view.externalGlMappingProfile.saveButtonLabel}
                onClick={() => void view.externalGlMappingProfile.save()}
              >
                {view.externalGlMappingProfile.saveButtonLabel}
              </Button>
            </div>
            <div className="mt-3 grid gap-3 md:grid-cols-3">
              <FormRow label="Provider" labelFor="accounting-external-gl-mapping-provider">
                <Input
                  id="accounting-external-gl-mapping-provider"
                  value={view.externalGlMappingProfile.providerIdValue}
                  onChange={(event) => view.externalGlMappingProfile.updateProviderId(event.currentTarget.value)}
                />
              </FormRow>
              <FormRow label="Profile" labelFor="accounting-external-gl-mapping-profile">
                <Input
                  id="accounting-external-gl-mapping-profile"
                  value={view.externalGlMappingProfile.profileIdValue}
                  onChange={(event) => view.externalGlMappingProfile.updateProfileId(event.currentTarget.value)}
                />
              </FormRow>
              <FormRow label="Display name" labelFor="accounting-external-gl-mapping-display-name">
                <Input
                  id="accounting-external-gl-mapping-display-name"
                  value={view.externalGlMappingProfile.displayNameValue}
                  onChange={(event) => view.externalGlMappingProfile.updateDisplayName(event.currentTarget.value)}
                />
              </FormRow>
            </div>
            <div className="mt-3 grid gap-3 lg:grid-cols-2">
              <FormRow label="Account mappings" labelFor="accounting-external-gl-account-mappings">
                <textarea
                  id="accounting-external-gl-account-mappings"
                  value={view.externalGlMappingProfile.accountMappingsValue}
                  onChange={(event) => view.externalGlMappingProfile.updateAccountMappings(event.currentTarget.value)}
                  className="min-h-24 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                  placeholder="Meridian:Account=external-account-id"
                />
              </FormRow>
              <FormRow label="Retained mapping evidence" labelFor="accounting-external-gl-mapping-evidence">
                <textarea
                  id="accounting-external-gl-mapping-evidence"
                  value={view.externalGlMappingProfile.evidenceValue}
                  onChange={(event) => view.externalGlMappingProfile.updateEvidence(event.currentTarget.value)}
                  className="min-h-24 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                  placeholder="approval:external-gl-mapping:profile-id"
                />
              </FormRow>
            </div>
            <div className="mt-3 grid gap-3 lg:grid-cols-2">
              <FormRow label="Meridian dimensions" labelFor="accounting-external-gl-meridian-dimensions">
                <textarea
                  id="accounting-external-gl-meridian-dimensions"
                  value={view.externalGlMappingProfile.meridianDimensionsValue}
                  onChange={(event) => view.externalGlMappingProfile.updateMeridianDimensions(event.currentTarget.value)}
                  className="min-h-28 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                  placeholder="fundId=fund-alpha&#10;bookId=book-primary&#10;Provider=quickbooks-fixture"
                />
              </FormRow>
              <FormRow label="External dimensions" labelFor="accounting-external-gl-provider-dimensions">
                <textarea
                  id="accounting-external-gl-provider-dimensions"
                  value={view.externalGlMappingProfile.externalDimensionsValue}
                  onChange={(event) => view.externalGlMappingProfile.updateExternalDimensions(event.currentTarget.value)}
                  className="min-h-28 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                  placeholder="Class=fund-alpha&#10;Book=book-primary&#10;customerId=qbo-customer"
                />
              </FormRow>
            </div>
            <label className="mt-3 flex items-center gap-2 text-sm font-semibold text-foreground">
              <input
                type="checkbox"
                checked={view.externalGlMappingProfile.certified}
                onChange={(event) => view.externalGlMappingProfile.updateCertified(event.currentTarget.checked)}
              />
              Certify mapping profile for guarded export readiness
            </label>
            {view.externalGlMappingProfile.mappingRows.length > 0 ? (
              <div className="mt-3 grid gap-2 md:grid-cols-3" role="region" aria-label="Retained external GL mapping profiles">
                {view.externalGlMappingProfile.mappingRows.map((row) => (
                  <div key={row.id} className={cn("rounded-md border px-3 py-2", accountingToolingBorderClass(row.tone))}>
                    <div className="text-sm font-semibold text-foreground">{row.label}</div>
                    <div className="mt-1 text-xs text-muted-foreground">{row.statusLabel}</div>
                  </div>
                ))}
              </div>
            ) : null}
            {view.externalGlMappingProfile.statusText ? (
              <p className={cn("mt-3 text-sm", view.externalGlMappingProfile.errorText ? "text-danger" : "text-muted-foreground")}>
                {view.externalGlMappingProfile.statusText}
              </p>
            ) : null}
          </div>

          {view.productionReadiness.migrationPlanRows.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" role="region" aria-label="Accounting migration rollout plan">
              {view.productionReadiness.migrationPlanRows.map((item) => (
                <div key={item.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(item.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="font-semibold text-foreground">{item.title}</div>
                      <div className="mt-1 font-mono text-[11px] text-muted-foreground">{item.id}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(item.tone)}>{item.statusLabel}</Badge>
                  </div>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{item.certificationLabel} | {item.latestRunLabel}</div>
                  <div className="mt-1 text-[11px] text-muted-foreground">{item.scopeLabel}</div>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{item.metricsLabel} | {item.evidenceLabel}</div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{item.requiredAction}</p>
                  <div className="mt-2 text-[11px] text-muted-foreground">{item.blockingIssueLabel}</div>
                </div>
              ))}
            </div>
          ) : null}

          {view.productionReadiness.migrationArtifactRows.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" role="region" aria-label="Retained accounting migration run artifacts">
              {view.productionReadiness.migrationArtifactRows.map((artifact) => (
                <div key={artifact.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(artifact.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="font-semibold text-foreground">{artifact.kindLabel}</div>
                      <div className="mt-1 font-mono text-[11px] text-muted-foreground">{artifact.id}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(artifact.tone)}>{artifact.statusLabel}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{artifact.title}</p>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{artifact.recordCountLabel} | {artifact.issueCountLabel} | {artifact.evidenceLabel}</div>
                  <div className="mt-1 text-[11px] text-muted-foreground">{artifact.detail}</div>
                </div>
              ))}
            </div>
          ) : null}

          {view.productionReadiness.migrationWorkerPlanRows.length > 0 ? (
            <div className="space-y-2" role="region" aria-label="Retained accounting migration worker plans">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                {view.productionReadiness.migrationWorkerPlanSummaryLabel}
              </div>
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {view.productionReadiness.migrationWorkerPlanRows.map((plan) => (
                  <div key={plan.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(plan.tone))}>
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div>
                        <div className="font-semibold text-foreground">{plan.kindLabel}</div>
                        <div className="mt-1 font-mono text-[11px] text-muted-foreground">{plan.id}</div>
                      </div>
                      <Badge variant={accountingToolingBadgeVariant(plan.tone)}>{plan.tone === "success" ? "Reconciled" : "Mismatch"}</Badge>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{plan.title}</p>
                    <div className="mt-2 font-mono text-[11px] text-muted-foreground">{plan.countLabel} | {plan.evidenceLabel}</div>
                    <div className="mt-1 text-[11px] text-muted-foreground">{plan.scopeLabel}</div>
                    <div className="mt-1 text-[11px] text-muted-foreground">{plan.detail}</div>
                  </div>
                ))}
              </div>
            </div>
          ) : null}

          {view.productionReadiness.components.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4" aria-label="Accounting production readiness components">
              {view.productionReadiness.components.map((component) => (
                <div key={component.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(component.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="font-semibold text-foreground">{component.label}</div>
                    <Badge variant={accountingToolingBadgeVariant(component.tone)}>{component.statusLabel}</Badge>
                  </div>
                  <div className="mt-2 font-mono text-xs text-muted-foreground">{component.scoreLabel} | {component.issueCountLabel}</div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{component.summary}</p>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{component.evidenceLabel} | {component.routeLabel}</div>
                </div>
              ))}
            </div>
          ) : null}

          {view.productionReadiness.blockerIssues.length > 0 ? (
            <div className="space-y-2" aria-label="Accounting production readiness blockers">
              {view.productionReadiness.blockerIssues.map((issue) => (
                <div key={issue.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(issue.tone))}>
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-semibold text-foreground">{issue.label}</span>
                    <Badge variant={accountingToolingBadgeVariant(issue.tone)}>{issue.tone === "danger" ? "Blocker" : "Review"}</Badge>
                  </div>
                  <p className="mt-1 text-muted-foreground">{issue.message}</p>
                  <p className="mt-1 text-xs text-muted-foreground">{issue.suggestedAction} | {issue.evidenceLabel}</p>
                </div>
              ))}
            </div>
          ) : (
            <p role="status" className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">No production-readiness blockers returned by the shared accounting control-plane assessment.</p>
          )}
        </CardContent>
      </Card>

      <Card className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle className="flex items-center gap-2">
                <BookCheck className="h-5 w-5 text-primary" />
                Accounting Rules Studio
              </CardTitle>
              <CardDescription>Effective-dated posting rules, predicates, formulas, allocations, generated postings, versions, promotion approvals, and dry-run previews use the shared configuration API.</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                size="sm"
                disabled={!view.canDryRun}
                disabledReason={view.dryRunDisabledReason}
                busy={view.dryRunBusy}
                busyLabel={view.dryRunButtonLabel}
                onClick={() => void view.dryRunSelectedRule()}
              >
                {view.dryRunButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="secondary"
                disabled={!view.canBuildJournalCandidate}
                disabledReason={view.journalCandidateDisabledReason}
                busy={view.journalCandidateBusy}
                busyLabel={view.journalCandidateButtonLabel}
                onClick={() => void view.buildJournalCandidate()}
              >
                {view.journalCandidateButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyThreshold}
                disabledReason={view.applyThresholdDisabledReason}
                busy={view.applyThresholdBusy}
                busyLabel={view.applyThresholdButtonLabel}
                onClick={() => void view.applyDryRunAmountThreshold()}
              >
                {view.applyThresholdButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyEventPredicate}
                disabledReason={view.applyEventPredicateDisabledReason}
                busy={view.applyEventPredicateBusy}
                busyLabel={view.applyEventPredicateButtonLabel}
                onClick={() => void view.applyDryRunEventPredicate()}
              >
                {view.applyEventPredicateButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyEffectiveStart}
                disabledReason={view.applyEffectiveStartDisabledReason}
                busy={view.applyEffectiveStartBusy}
                busyLabel={view.applyEffectiveStartButtonLabel}
                onClick={() => void view.applyDryRunEffectiveStart()}
              >
                {view.applyEffectiveStartButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canCapturePostings}
                disabledReason={view.capturePostingsDisabledReason}
                busy={view.capturePostingsBusy}
                busyLabel={view.capturePostingsButtonLabel}
                onClick={() => void view.captureDryRunGeneratedPostings()}
              >
                {view.capturePostingsButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyFormula}
                disabledReason={view.applyFormulaDisabledReason}
                busy={view.applyFormulaBusy}
                busyLabel={view.applyFormulaButtonLabel}
                onClick={() => void view.applyDryRunFormulaAmount()}
              >
                {view.applyFormulaButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyAllocation}
                disabledReason={view.applyAllocationDisabledReason}
                busy={view.applyAllocationBusy}
                busyLabel={view.applyAllocationButtonLabel}
                onClick={() => void view.applyDryRunAllocationTargets()}
              >
                {view.applyAllocationButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyScope}
                disabledReason={view.applyScopeDisabledReason}
                busy={view.applyScopeBusy}
                busyLabel={view.applyScopeButtonLabel}
                onClick={() => void view.applyDryRunScope()}
              >
                {view.applyScopeButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canDuplicateRule}
                disabledReason={view.duplicateRuleDisabledReason}
                busy={view.duplicateRuleBusy}
                busyLabel={view.duplicateRuleButtonLabel}
                onClick={() => void view.duplicateSelectedRule()}
              >
                {view.duplicateRuleButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canRaisePriority}
                disabledReason={view.raisePriorityDisabledReason}
                busy={view.raisePriorityBusy}
                busyLabel={view.raisePriorityButtonLabel}
                onClick={() => void view.raiseSelectedRulePriority()}
              >
                {view.raisePriorityButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canSaveDryRunAsRuleTest}
                disabledReason={view.saveDryRunAsRuleTestDisabledReason}
                busy={view.saveDryRunAsRuleTestBusy}
                busyLabel={view.saveDryRunAsRuleTestButtonLabel}
                onClick={() => void view.saveDryRunAsRuleTest()}
              >
                {view.saveDryRunAsRuleTestButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canArchiveRule}
                disabledReason={view.archiveRuleDisabledReason}
                busy={view.archiveRuleBusy}
                busyLabel={view.archiveRuleButtonLabel}
                onClick={() => void view.archiveSelectedRule()}
              >
                {view.archiveRuleButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canRunRuleTests}
                disabledReason={view.ruleTestDisabledReason}
                busy={view.ruleTestBusy}
                busyLabel={view.ruleTestButtonLabel}
                onClick={() => void view.runRuleTests()}
              >
                {view.ruleTestButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="secondary"
                disabled={!view.canApproveRulePromotion}
                disabledReason={view.approveRulePromotionDisabledReason}
                busy={view.approveRulePromotionBusy}
                busyLabel={view.approveRulePromotionButtonLabel}
                onClick={() => void view.approveRulePromotion()}
              >
                {view.approveRulePromotionButtonLabel}
              </Button>
              {view.dryRunStatusText ? <span className="text-sm text-muted-foreground">{view.dryRunStatusText}</span> : null}
              {view.journalCandidateStatusText ? <span className="text-sm text-muted-foreground">{view.journalCandidateStatusText}</span> : null}
              {view.applyEventPredicateStatusText ? <span className="text-sm text-muted-foreground">{view.applyEventPredicateStatusText}</span> : null}
              {view.applyThresholdStatusText ? <span className="text-sm text-muted-foreground">{view.applyThresholdStatusText}</span> : null}
              {view.applyEffectiveStartStatusText ? <span className="text-sm text-muted-foreground">{view.applyEffectiveStartStatusText}</span> : null}
              {view.capturePostingsStatusText ? <span className="text-sm text-muted-foreground">{view.capturePostingsStatusText}</span> : null}
              {view.applyFormulaStatusText ? <span className="text-sm text-muted-foreground">{view.applyFormulaStatusText}</span> : null}
              {view.applyAllocationStatusText ? <span className="text-sm text-muted-foreground">{view.applyAllocationStatusText}</span> : null}
              {view.applyScopeStatusText ? <span className="text-sm text-muted-foreground">{view.applyScopeStatusText}</span> : null}
              {view.duplicateRuleStatusText ? <span className="text-sm text-muted-foreground">{view.duplicateRuleStatusText}</span> : null}
              {view.raisePriorityStatusText ? <span className="text-sm text-muted-foreground">{view.raisePriorityStatusText}</span> : null}
              {view.archiveRuleStatusText ? <span className="text-sm text-muted-foreground">{view.archiveRuleStatusText}</span> : null}
              {view.saveDryRunAsRuleTestStatusText ? <span className="text-sm text-muted-foreground">{view.saveDryRunAsRuleTestStatusText}</span> : null}
              {view.ruleTestStatusText ? <span className="text-sm text-muted-foreground">{view.ruleTestStatusText}</span> : null}
              {view.approveRulePromotionStatusText ? <span className="text-sm text-muted-foreground">{view.approveRulePromotionStatusText}</span> : null}
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {view.rules.length > 0 ? (
            <div className="grid gap-4 xl:grid-cols-[minmax(0,0.95fr)_minmax(0,1.05fr)]">
              <div className="space-y-2" role="list" aria-label="Accounting posting rules">
                {view.rules.map((rule) => (
                  <button
                    key={rule.id}
                    type="button"
                    role="listitem"
                    aria-label={rule.selectAriaLabel}
                    aria-pressed={rule.isSelected}
                    className={cn(
                      "w-full rounded-lg border px-3 py-3 text-left transition hover:bg-secondary/35",
                      rule.isSelected ? "border-primary/45 bg-primary/10" : "border-border/70 bg-secondary/20"
                    )}
                    onClick={() => view.selectRule(rule.id)}
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div className="min-w-0">
                        <div className="font-semibold text-foreground">{rule.title}</div>
                        <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{rule.subtitle}</div>
                      </div>
                      <Badge variant={rule.statusTone}>{rule.statusLabel}</Badge>
                    </div>
                    <div className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-3">
                      <span className="font-mono">{rule.eventLabel}</span>
                      <span className="font-mono">{rule.effectiveLabel}</span>
                      <span className="font-mono">{rule.priorityLabel}</span>
                    </div>
                  </button>
                ))}
              </div>

              <div className="rounded-lg border border-border/70 bg-background/35 p-3">
                {view.selectedRule ? (
                  <div className="space-y-4">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div>
                        <div className="font-semibold text-foreground">{view.selectedRule.title}</div>
                        <div className="mt-1 font-mono text-xs text-muted-foreground">{view.selectedRule.eventLabel} | {view.selectedRule.effectiveLabel} | {view.selectedRule.priorityLabel}</div>
                      </div>
                      <Badge variant={view.selectedRule.promotionTone}>{view.selectedRule.promotionLabel}</Badge>
                    </div>
                    <div className="rounded-lg border border-border/70 bg-secondary/10 p-3" role="region" aria-label="Posting rule promotion readiness">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Promotion readiness</div>
                        <Badge variant={accountingToolingBadgeVariant(hasPromotionReadinessBlocker(view.selectedRule.promotionReadiness) ? "warning" : "success")}>
                          {view.selectedRule.promotionReadiness.find((item) => item.id === "activation-gate")?.value ?? "Review"}
                        </Badge>
                      </div>
                      <div className="mt-3 grid gap-2 md:grid-cols-2">
                        {view.selectedRule.promotionReadiness.map((item) => (
                          <div key={item.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(item.tone))}>
                            <div className="flex flex-wrap items-center justify-between gap-2">
                              <span className="font-semibold text-foreground">{item.label}</span>
                              <Badge variant={accountingToolingBadgeVariant(item.tone)}>{item.value}</Badge>
                            </div>
                            <div className="mt-1 text-xs text-muted-foreground">{item.detail}</div>
                          </div>
                        ))}
                      </div>
                    </div>
                    <RulesStudioList title="Scope" rows={view.selectedRule.scopeLabels.length > 0 ? view.selectedRule.scopeLabels : ["No dimensional scope configured."]} />
                    <RulesStudioList title="Predicates" rows={view.selectedRule.conditionRows} />
                    <RulesStudioList title="Formulas" rows={view.selectedRule.formulaRows} />
                    <RulesStudioList title="Allocations" rows={view.selectedRule.allocationRows} />
                    <RulesStudioList title="Generated postings" rows={view.selectedRule.generatedPostingRows} />
                    <RulesStudioList title="Version history" rows={view.selectedRule.versionRows} />
                  </div>
                ) : (
                  <p className="text-sm text-muted-foreground">Select a posting rule to inspect its effective dating, predicates, formula logic, allocation behavior, generated postings, and promotion evidence.</p>
                )}
              </div>
            </div>
          ) : (
            <p role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">{view.emptyText}</p>
          )}

          {view.dryRunPreview ? (
            <div className="rounded-lg border border-primary/25 bg-primary/5 p-3" role="region" aria-label="Accounting rule dry-run preview">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <div className="font-semibold text-foreground">{view.dryRunPreview.title}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{view.dryRunPreview.selectedRuleLabel}</div>
                </div>
                <Badge variant={view.dryRunPreview.balanceLabel.startsWith("Balanced") ? "success" : "warning"}>{view.dryRunPreview.balanceLabel}</Badge>
              </div>
              <div className="mt-3 grid gap-3 xl:grid-cols-3">
                <RulesStudioList title="Rule resolution" rows={view.dryRunPreview.matchRows.length > 0 ? view.dryRunPreview.matchRows : ["No rule matches were returned."]} />
                <RulesStudioList title="Generated journal lines" rows={view.dryRunPreview.generatedLineRows.length > 0 ? view.dryRunPreview.generatedLineRows : ["No generated journal lines were returned."]} />
                <RulesStudioList title="Generated posting metadata" rows={view.dryRunPreview.generatedPostingRows} />
              </div>
              {view.dryRunPreview.validationRows.length > 0 ? (
                <div className="mt-3 space-y-2">
                  {view.dryRunPreview.validationRows.map((issue) => (
                    <div key={issue.id} className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                      <div className="font-semibold">{issue.label}</div>
                      <div className="mt-1">{issue.message}</div>
                    </div>
                  ))}
                </div>
              ) : null}
            </div>
          ) : null}

          {view.journalCandidatePreview ? (
            <div className="rounded-lg border border-border/70 bg-secondary/15 p-3" role="region" aria-label="Accounting rule journal draft candidate">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <div className="font-semibold text-foreground">{view.journalCandidatePreview.title}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{view.journalCandidatePreview.selectedRuleLabel}</div>
                </div>
                <Badge variant={view.journalCandidatePreview.balanceLabel.startsWith("Balanced") ? "success" : "warning"}>{view.journalCandidatePreview.balanceLabel}</Badge>
              </div>
              <div className="mt-3 grid gap-3 xl:grid-cols-3">
                <RulesStudioList title="Draft command" rows={[view.journalCandidatePreview.commandLabel, view.journalCandidatePreview.approvalLabel, view.journalCandidatePreview.evidenceLabel]} />
                <RulesStudioList title="Candidate posting lines" rows={view.journalCandidatePreview.generatedLineRows} />
                <RulesStudioList
                  title="Candidate issues"
                  rows={view.journalCandidatePreview.issueRows.length > 0
                    ? view.journalCandidatePreview.issueRows.map((issue) => `${issue.label}: ${issue.message}`)
                    : ["No blocking candidate issues returned."]}
                />
              </div>
            </div>
          ) : null}

          <div className="rounded-lg border border-border/70 bg-background p-3" role="region" aria-label="Saved accounting rule test cases">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <div className="font-semibold text-foreground">Saved regression cases</div>
                <div className="mt-1 text-xs text-muted-foreground">Persisted dry-run assertions used for rule promotion checks.</div>
              </div>
              <Badge variant={view.ruleTestCases.length > 0 ? "success" : "warning"}>{view.ruleTestCases.length} saved</Badge>
            </div>
            <div className="mt-3 grid gap-2 md:grid-cols-2">
              {view.ruleTestCases.length > 0 ? view.ruleTestCases.map((testCase) => (
                <div key={testCase.id} className="rounded-md border border-border/70 bg-secondary/10 px-3 py-2 text-sm">
                  <div className="font-semibold text-foreground">{testCase.title}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{testCase.subtitle}</div>
                  <div className="mt-1 text-xs text-muted-foreground">{testCase.assertionLabel}</div>
                  <Badge variant={testCase.evidenceTone} className="mt-2">{testCase.evidenceLabel}</Badge>
                </div>
              )) : (
                <p className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning md:col-span-2">
                  No saved rule test cases. Running tests will generate temporary cases from active posting rules.
                </p>
              )}
            </div>
          </div>

          {view.ruleTestSuite ? (
            <div className="rounded-lg border border-border/70 bg-secondary/15 p-3" role="region" aria-label="Accounting rule test cases">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <div className="font-semibold text-foreground">{view.ruleTestSuite.title}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{view.ruleTestSuite.executedLabel}</div>
                </div>
                <Badge variant={view.ruleTestSuite.statusTone}>{view.ruleTestSuite.summaryLabel}</Badge>
              </div>
              <div className="mt-3 grid gap-3 xl:grid-cols-2">
                <RulesStudioList title="Regression cases" rows={view.ruleTestSuite.resultRows} />
                <RulesStudioList
                  title="Assertion issues"
                  rows={view.ruleTestSuite.validationRows.length > 0
                    ? view.ruleTestSuite.validationRows.map((issue) => `${issue.label}: ${issue.message}`)
                    : ["No rule-test assertion issues returned."]}
                />
              </div>
            </div>
          ) : null}
        </CardContent>
      </Card>

      <div className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Network className="h-5 w-5 text-primary" />
              Journal templates and preview
            </CardTitle>
            <CardDescription>Preview uses accounting configuration services and does not persist journal entries.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <Button
                size="sm"
                disabled={!view.canPreview}
                disabledReason={view.previewDisabledReason}
                busy={view.previewBusy}
                busyLabel={view.previewButtonLabel}
                onClick={() => void view.previewFirstTemplate()}
              >
                {view.previewButtonLabel}
              </Button>
              {view.previewStatusText ? <span className="text-sm text-muted-foreground">{view.previewStatusText}</span> : null}
            </div>

            <div className="space-y-2">
              {view.templates.length > 0 ? view.templates.map((template) => (
                <div key={template.id} className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-2">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{template.title}</div>
                      <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{template.subtitle}</div>
                    </div>
                    <Badge variant={template.statusLabel === "Balanced" ? "success" : template.statusLabel === "Archived" ? "outline" : "warning"}>
                      {template.statusLabel}
                    </Badge>
                  </div>
                  <div className="mt-2 flex flex-wrap gap-2 text-xs text-muted-foreground">
                    <span>{template.lineCountLabel}</span>
                    <span>{template.balanceLabel}</span>
                  </div>
                </div>
              )) : (
                <p role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">{view.emptyText}</p>
              )}
            </div>

            {view.preview ? (
              <div className="rounded-lg border border-border/70 bg-background/35 p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <div className="font-semibold text-foreground">{view.preview.title}</div>
                    <div className="mt-1 font-mono text-xs text-muted-foreground">{view.preview.balanceLabel}</div>
                  </div>
                  <Badge variant={view.preview.statusLabel.startsWith("Balanced") ? "success" : "warning"}>{view.preview.statusLabel}</Badge>
                </div>
                <div className="mt-3 space-y-2">
                  {view.preview.lineRows.map((line) => (
                    <div key={line.id} className="grid gap-2 rounded border border-border/60 px-2 py-2 text-xs sm:grid-cols-[1fr_auto_auto]">
                      <span className="min-w-0 break-words font-mono text-foreground">{line.account}</span>
                      <span className="font-mono text-muted-foreground">{line.side}</span>
                      <span className="font-mono text-foreground">{line.amount}</span>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Validation and audit trail</CardTitle>
            <CardDescription>Configuration readiness and append-only mutation evidence stay visible before activation.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              {view.validationIssues.length > 0 ? view.validationIssues.map((issue) => (
                <div key={issue.id} className={cn(
                  "rounded-lg border px-3 py-2 text-sm",
                  issue.tone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : "",
                  issue.tone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : "",
                  issue.tone === "default" ? "border-border/70 bg-secondary/25 text-muted-foreground" : ""
                )}>
                  <div className="font-semibold">{issue.label}</div>
                  <div className="mt-1">{issue.message}</div>
                  <div className="mt-1 font-mono text-xs">{issue.detail}</div>
                </div>
              )) : (
                <div className="rounded-lg border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                  No critical configuration validation issues.
                </div>
              )}
            </div>

            <div className="space-y-2">
              <div className="eyebrow-label">Recent audit events</div>
              {view.auditTrail.length > 0 ? view.auditTrail.map((event) => (
                <div key={event.id} className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-2 text-sm">
                  <div className="font-semibold text-foreground">{event.title}</div>
                  <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{event.subtitle}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{event.hashLabel}</div>
                </div>
              )) : (
                <p className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">{view.emptyText}</p>
              )}
            </div>
          </CardContent>
        </Card>
      </div>
    </section>
  );
}

function RulesStudioList({ title, rows }: { title: string; rows: string[] }) {
  return (
    <div>
      <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{title}</div>
      <div className="mt-2 space-y-1.5">
        {rows.map((row, index) => (
          <div key={`${title}-${index}-${row}`} className="rounded border border-border/60 bg-secondary/20 px-2 py-1.5 text-xs leading-5 text-muted-foreground">
            {row}
          </div>
        ))}
      </div>
    </div>
  );
}

function hasPromotionReadinessBlocker(items: AccountingRulesStudioPromotionReadinessViewModel[]): boolean {
  return items.some((item) => item.tone === "danger" || (item.id === "activation-gate" && item.tone === "warning"));
}


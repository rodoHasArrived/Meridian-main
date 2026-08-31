import { ArrowRight, Play, Plus, RefreshCw, Trash2 } from "lucide-react";
import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import type { ApiErrorDisplay } from "@/lib/api-errors";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import {
  useStatementFetchPanelViewModel,
  type StatementFetchDraftField,
  type StatementFetchPanelServices
} from "@/screens/statement-fetch-panel.view-model";
import { StatementImportPreviewDetails } from "@/screens/statement-import-preview";
import type { StatementConnectorDescriptor, StatementMappingProfile } from "@/types";

export interface StatementFetchPanelProps {
  connectors: StatementConnectorDescriptor[];
  profiles: StatementMappingProfile[];
  services?: Partial<StatementFetchPanelServices>;
}

export function StatementFetchPanel({ connectors, profiles, services }: StatementFetchPanelProps) {
  const viewModel = useStatementFetchPanelViewModel({ connectors, profiles, services });

  if (viewModel.remoteConnectors.length === 0) {
    return (
      <StatusBanner
        tone="warning"
        title="No remote statement connector is available"
        detail="Configure a fetch-capable broker connection, such as Alpaca, or use the file upload path. Credentials stay in the existing provider vault."
      />
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle>Remote statement preview and schedule</CardTitle>
          <CardDescription>
            Fetch broker or custodian activity through the existing provider connection, inspect the canonical mapping, then save a recurring schedule or run it now. No credentials are stored in this workflow.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-5">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <FetchField label="Fetch connector" field="connectorId" error={viewModel.draftErrors.connectorId}>
              <Select
                id="statement-fetch-connector-id"
                value={viewModel.draft.connectorId}
                aria-invalid={Boolean(viewModel.draftErrors.connectorId)}
                aria-describedby={viewModel.draftErrors.connectorId ? "statement-fetch-connector-id-error" : undefined}
                onChange={(event) => viewModel.updateDraft("connectorId", event.target.value)}
              >
                {viewModel.remoteConnectors.map((connector) => (
                  <option key={connector.connectorId} value={connector.connectorId}>{connector.displayName}</option>
                ))}
              </Select>
            </FetchField>
            <FetchField label="External account" field="externalAccountId" error={viewModel.draftErrors.externalAccountId}>
              <Input
                id="statement-fetch-external-account-id"
                value={viewModel.draft.externalAccountId}
                aria-invalid={Boolean(viewModel.draftErrors.externalAccountId)}
                aria-describedby={viewModel.draftErrors.externalAccountId ? "statement-fetch-external-account-id-error" : undefined}
                onChange={(event) => viewModel.updateDraft("externalAccountId", event.target.value)}
                placeholder="PA3ALPACA01"
              />
            </FetchField>
            <FetchField label="Mapping profile" field="mappingProfileId">
              <Select
                id="statement-fetch-mapping-profile-id"
                value={viewModel.draft.mappingProfileId}
                onChange={(event) => viewModel.updateDraft("mappingProfileId", event.target.value)}
              >
                <option value="">Connector default</option>
                {viewModel.profiles.map((profile) => (
                  <option key={profile.profileId} value={profile.profileId}>
                    {profile.displayName}{profile.isBuiltIn ? " (built-in)" : ""}
                  </option>
                ))}
              </Select>
            </FetchField>
            <FetchField label="Fetch / ledger period start" field="sinceDate" error={viewModel.draftErrors.sinceDate}>
              <Input
                id="statement-fetch-since-date"
                type="date"
                value={viewModel.draft.sinceDate}
                aria-invalid={Boolean(viewModel.draftErrors.sinceDate)}
                aria-describedby={viewModel.draftErrors.sinceDate ? "statement-fetch-since-date-error" : undefined}
                onChange={(event) => viewModel.updateDraft("sinceDate", event.target.value)}
              />
            </FetchField>
            <FetchField label="Preview datasets" field="datasets">
              <Select
                id="statement-fetch-datasets"
                value={viewModel.draft.datasets}
                onChange={(event) => viewModel.updateDraft("datasets", event.target.value as "activity" | "positions" | "all")}
              >
                <option value="all">Activity and positions</option>
                <option value="activity">Account activity</option>
                <option value="positions">Current positions</option>
              </Select>
            </FetchField>
          </div>

          {viewModel.previewError ? <FetchError title="Remote statement preview failed" error={viewModel.previewError} /> : null}
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              busy={viewModel.previewBusy}
              busyLabel="Fetching preview…"
              disabled={!viewModel.canPreview}
              disabledReason={viewModel.previewDisabledReason ?? undefined}
              onClick={() => void viewModel.previewFetch()}
            >
              Preview remote statement
            </Button>
            <span className="text-xs text-muted-foreground">
              Preview is read-only. Only a saved schedule run commits records into reconciliation.
            </span>
          </div>

          <div className="border-t border-border pt-5">
            <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
              <div>
                <h3 className="text-sm font-semibold">Schedule configuration</h3>
                <p className="text-xs text-muted-foreground">Leave schedule id blank to create a new schedule.</p>
              </div>
              <Button type="button" size="sm" variant="outline" onClick={viewModel.newSchedule}>
                <Plus className="size-3.5" aria-hidden="true" />
                New schedule
              </Button>
            </div>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <FetchField label="Schedule id" field="scheduleId">
                <Input
                  id="statement-fetch-schedule-id"
                  value={viewModel.draft.scheduleId}
                  onChange={(event) => viewModel.updateDraft("scheduleId", event.target.value)}
                  placeholder="Generated when blank"
                />
              </FetchField>
              <FetchField label="Fund account" field="fundAccountId" error={viewModel.draftErrors.fundAccountId}>
                <Input
                  id="statement-fetch-fund-account-id"
                  value={viewModel.draft.fundAccountId}
                  aria-invalid={Boolean(viewModel.draftErrors.fundAccountId)}
                  aria-describedby={viewModel.draftErrors.fundAccountId ? "statement-fetch-fund-account-id-error" : undefined}
                  onChange={(event) => viewModel.updateDraft("fundAccountId", event.target.value)}
                  placeholder="FUND-ALPHA-BROKERAGE"
                />
              </FetchField>
              <FetchField label="Source institution" field="sourceInstitution" error={viewModel.draftErrors.sourceInstitution}>
                <Input
                  id="statement-fetch-source-institution"
                  value={viewModel.draft.sourceInstitution}
                  aria-invalid={Boolean(viewModel.draftErrors.sourceInstitution)}
                  aria-describedby={viewModel.draftErrors.sourceInstitution ? "statement-fetch-source-institution-error" : undefined}
                  onChange={(event) => viewModel.updateDraft("sourceInstitution", event.target.value)}
                  placeholder="Alpaca"
                />
              </FetchField>
              <FetchField label="Statement source" field="sourceKind">
                <Select
                  id="statement-fetch-source-kind"
                  value={viewModel.draft.sourceKind}
                  onChange={(event) => viewModel.updateDraft("sourceKind", event.target.value as "broker" | "custodian")}
                >
                  <option value="broker">Broker</option>
                  <option value="custodian">Custodian</option>
                </Select>
              </FetchField>
              <FetchField label="Tolerance profile" field="toleranceProfileId">
                <Input
                  id="statement-fetch-tolerance-profile-id"
                  value={viewModel.draft.toleranceProfileId}
                  onChange={(event) => viewModel.updateDraft("toleranceProfileId", event.target.value)}
                />
              </FetchField>
              <FetchField label="Ledger period end" field="periodEnd" error={viewModel.draftErrors.periodEnd}>
                <Input
                  id="statement-fetch-period-end"
                  type="date"
                  value={viewModel.draft.periodEnd}
                  aria-invalid={Boolean(viewModel.draftErrors.periodEnd)}
                  aria-describedby={viewModel.draftErrors.periodEnd ? "statement-fetch-period-end-error" : undefined}
                  onChange={(event) => viewModel.updateDraft("periodEnd", event.target.value)}
                />
              </FetchField>
              <FetchField label="Cadence (hours)" field="cadenceHours" error={viewModel.draftErrors.cadenceHours}>
                <Input
                  id="statement-fetch-cadence-hours"
                  type="number"
                  min="1"
                  step="1"
                  value={viewModel.draft.cadenceHours}
                  aria-invalid={Boolean(viewModel.draftErrors.cadenceHours)}
                  aria-describedby={viewModel.draftErrors.cadenceHours ? "statement-fetch-cadence-hours-error" : undefined}
                  onChange={(event) => viewModel.updateDraft("cadenceHours", event.target.value)}
                />
              </FetchField>
              <div className="flex items-end pb-2">
                <Checkbox
                  id="statement-fetch-enabled"
                  checked={viewModel.draft.enabled}
                  onCheckedChange={(checked) => viewModel.updateDraft("enabled", checked)}
                  label="Enable automatic fetches"
                />
              </div>
            </div>
          </div>

          {viewModel.saveError ? <FetchError title="Schedule save failed" error={viewModel.saveError} /> : null}
          {viewModel.saveMessage ? <StatusBanner tone="success" title="Statement fetch schedule saved" detail={viewModel.saveMessage} /> : null}
          <div>
            <Button
              type="button"
              busy={viewModel.saveBusy}
              busyLabel="Saving schedule…"
              disabled={!viewModel.canSave}
              disabledReason={viewModel.saveDisabledReason ?? undefined}
              onClick={() => void viewModel.saveSchedule()}
            >
              Save fetch schedule
            </Button>
          </div>
        </CardContent>
      </Card>

      {viewModel.preview ? (
        <StatementImportPreviewDetails
          preview={viewModel.preview}
          selectedKind={viewModel.selectedKind}
          onSelectKind={viewModel.selectKind}
        />
      ) : null}

      <StatementFetchSchedulesTable viewModel={viewModel} />
      <StatementFetchRunResult viewModel={viewModel} />
    </div>
  );
}

function FetchField({
  children,
  error,
  field,
  label
}: {
  children: ReactNode;
  error?: string;
  field: StatementFetchDraftField;
  label: string;
}) {
  const id = `statement-fetch-${toKebabCase(field)}`;
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {error ? <p id={`${id}-error`} className="text-xs text-danger" role="alert">{error}</p> : null}
    </div>
  );
}

function StatementFetchSchedulesTable({
  viewModel
}: {
  viewModel: ReturnType<typeof useStatementFetchPanelViewModel>;
}) {
  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle>Saved statement fetch schedules</CardTitle>
            <CardDescription>Run a schedule now, edit its configuration, or remove it.</CardDescription>
          </div>
          <Button
            type="button"
            size="sm"
            variant="outline"
            busy={viewModel.loading}
            busyLabel="Refreshing…"
            onClick={() => void viewModel.refreshSchedules()}
          >
            <RefreshCw className="size-3.5" aria-hidden="true" />
            Refresh
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        {viewModel.loadError ? <FetchError title="Schedule list failed" error={viewModel.loadError} /> : null}
        {viewModel.runError ? <FetchError title="Scheduled statement run failed" error={viewModel.runError} /> : null}
        {viewModel.deleteError ? <FetchError title="Schedule delete failed" error={viewModel.deleteError} /> : null}
        {!viewModel.loading && viewModel.schedules.length === 0 ? (
          <StatusBanner
            tone="info"
            title="No schedules configured"
            detail="Complete the fetch and reconciliation fields above, then save the first statement schedule."
          />
        ) : null}
        {viewModel.schedules.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-xs" aria-label="Statement fetch schedules">
              <caption className="sr-only">Persisted remote statement fetch schedules and run posture</caption>
              <thead>
                <tr className="border-b border-border text-left font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                  <th scope="col" className="px-2 py-2">Schedule</th>
                  <th scope="col" className="px-2 py-2">Account</th>
                  <th scope="col" className="px-2 py-2">Cadence</th>
                  <th scope="col" className="px-2 py-2">Last result</th>
                  <th scope="col" className="px-2 py-2">Next due</th>
                  <th scope="col" className="px-2 py-2 text-right">Actions</th>
                </tr>
              </thead>
              <tbody>
                {viewModel.schedules.map((schedule) => (
                  <tr key={schedule.scheduleId} className="border-b border-border/60 align-top">
                    <td className="px-2 py-2">
                      <div className="font-mono font-semibold">{schedule.scheduleId}</div>
                      <div className="mt-1 flex flex-wrap gap-1.5">
                        <Badge variant={schedule.enabled ? "success" : "outline"}>{schedule.enabled ? "Enabled" : "Paused"}</Badge>
                        <Badge variant="paper">{schedule.connectorId}</Badge>
                        <Badge variant="outline" className="capitalize">{schedule.sourceKind}</Badge>
                      </div>
                    </td>
                    <td className="px-2 py-2">
                      <div>{schedule.sourceInstitution}</div>
                      <div className="font-mono text-muted-foreground">{schedule.externalAccountId}</div>
                      <div className="font-mono text-muted-foreground">{schedule.fundAccountId}</div>
                    </td>
                    <td className="px-2 py-2 font-mono">Every {schedule.cadenceHours}h</td>
                    <td className="max-w-64 px-2 py-2">
                      <div>{schedule.lastRunStatus ?? "Not run"}</div>
                      <div className="font-mono text-[10px] text-muted-foreground">{formatTimestamp(schedule.lastRunAtUtc)}</div>
                    </td>
                    <td className="px-2 py-2 font-mono">{formatTimestamp(schedule.nextDueAtUtc)}</td>
                    <td className="px-2 py-2">
                      <div className="flex flex-wrap justify-end gap-1.5">
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          aria-label={`Edit schedule ${schedule.scheduleId}`}
                          onClick={() => viewModel.editSchedule(schedule)}
                        >
                          Edit
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          aria-label={`Run schedule ${schedule.scheduleId}`}
                          busy={viewModel.runBusyId === schedule.scheduleId}
                          busyLabel="Running…"
                          onClick={() => void viewModel.runSchedule(schedule.scheduleId)}
                        >
                          <Play className="size-3.5" aria-hidden="true" />
                          Run now
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          variant={viewModel.pendingDeleteScheduleId === schedule.scheduleId ? "destructive" : "ghost"}
                          aria-label={viewModel.pendingDeleteScheduleId === schedule.scheduleId
                            ? `Confirm delete schedule ${schedule.scheduleId}. This permanently removes the fetch schedule.`
                            : `Delete schedule ${schedule.scheduleId}`}
                          aria-describedby={viewModel.pendingDeleteScheduleId === schedule.scheduleId
                            ? `statement-fetch-delete-${schedule.scheduleId}-status`
                            : undefined}
                          busy={viewModel.deleteBusyId === schedule.scheduleId}
                          busyLabel="Deleting…"
                          onClick={() => void viewModel.deleteSchedule(schedule.scheduleId)}
                        >
                          <Trash2 className="size-3.5" aria-hidden="true" />
                          {viewModel.pendingDeleteScheduleId === schedule.scheduleId ? "Confirm delete" : "Delete"}
                        </Button>
                        {viewModel.pendingDeleteScheduleId === schedule.scheduleId ? (
                          <p
                            id={`statement-fetch-delete-${schedule.scheduleId}-status`}
                            role="status"
                            aria-live="polite"
                            className="basis-full text-right text-[11px] leading-4 text-warning"
                          >
                            Delete confirmation pending for {schedule.scheduleId}. Confirm delete permanently removes this schedule.
                          </p>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function StatementFetchRunResult({
  viewModel
}: {
  viewModel: ReturnType<typeof useStatementFetchPanelViewModel>;
}) {
  const result = viewModel.runResult;
  if (!result) {
    return null;
  }

  const reconciliationRoute = result.reconciliationRoute ?? WORKSTATION_ROUTE_CATALOG.accountingReconciliationMatch;
  const evidenceRoute = result.evidenceWorkbenchRoute ?? WORKSTATION_ROUTE_CATALOG.reportingEvidence;
  return (
    <Card>
      <CardHeader>
        <CardTitle>{result.duplicate ? "Statement already imported" : "Scheduled statement imported"}</CardTitle>
        <CardDescription>
          Run {result.runId} {result.duplicate ? "matched retained evidence" : `committed ${result.recordCount} records`} with {result.breakCount} breaks and {result.caseCount} cases. {result.nextAction}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-wrap gap-2">
        <Button asChild size="sm" variant="outline">
          <Link to={evidenceRoute}>
            Open Evidence Vault
            <ArrowRight className="size-3.5" aria-hidden="true" />
          </Link>
        </Button>
        <Button asChild size="sm">
          <Link to={reconciliationRoute}>
            Open reconciliation queue
            <ArrowRight className="size-3.5" aria-hidden="true" />
          </Link>
        </Button>
      </CardContent>
    </Card>
  );
}

function FetchError({ error, title }: { error: ApiErrorDisplay; title: string }) {
  const detail = [error.summary, ...error.details].filter(Boolean).join(" ");
  return <StatusBanner tone="danger" title={title} detail={detail} />;
}

function formatTimestamp(value: string | null): string {
  if (!value) {
    return "—";
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function toKebabCase(value: string): string {
  return value.replace(/[A-Z]/g, (character) => `-${character.toLowerCase()}`);
}

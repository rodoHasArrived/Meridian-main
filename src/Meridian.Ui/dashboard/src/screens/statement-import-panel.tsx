import { ArrowRight, Plus, Trash2 } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { FileUpload } from "@/components/ui/file-upload";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PanelSurface } from "@/components/ui/panel-surface";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import {
  STATEMENT_FORMAT_DRIFT_ISSUE_CODE,
  statementColumnConfidenceBadgeVariant,
  statementIssueBadgeVariant,
  useStatementImportPanelViewModel,
  type StatementImportPanelViewModel,
  type UseStatementImportPanelOptions
} from "@/screens/statement-import-panel.view-model";
import type { ApiErrorDisplay } from "@/lib/api-errors";

export function StatementImportPanel(options: UseStatementImportPanelOptions = {}) {
  const viewModel = useStatementImportPanelViewModel(options);

  return (
    <section className="flex flex-col gap-4" aria-label="Import statement">
      <PanelSurface className="p-4">
        <h1 className="text-base font-semibold text-foreground">Import statement</h1>
        <p className="mt-1 text-xs text-muted-foreground">
          Upload a broker or custodian statement, review the detected column mappings and record kinds, then commit
          the import into the reconciliation queue.
        </p>
      </PanelSurface>

      <h2 className="sr-only">Statement import workflow</h2>

      {viewModel.loadError ? (
        <ErrorBanner title="Statement connector library failed to load" error={viewModel.loadError} />
      ) : null}

      <SourceSection viewModel={viewModel} />
      {viewModel.previewError ? (
        <ErrorBanner title="Statement preview failed" error={viewModel.previewError} />
      ) : null}
      {viewModel.preview ? <PreviewSection viewModel={viewModel} /> : null}
      <ProfileEditorSection viewModel={viewModel} />
      <CommitSection viewModel={viewModel} />
    </section>
  );
}

function ErrorBanner({ title, error }: { title: string; error: ApiErrorDisplay }) {
  return (
    <StatusBanner
      tone="danger"
      title={title}
      detail={[error.summary, ...error.details].join(" ")}
    />
  );
}

function SourceSection({ viewModel }: { viewModel: StatementImportPanelViewModel }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Statement source</CardTitle>
        <CardDescription>
          Drop a statement file, then optionally pin a connector and mapping profile. Changing the mapping profile
          re-runs the preview automatically.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <FileUpload
          label="Statement file"
          multiple={false}
          accept={viewModel.fileAccept}
          disabled={viewModel.loading}
          onFilesSelected={(files) => viewModel.selectFile(files[0] ?? null)}
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="statement-import-connector">Connector</Label>
            <Select
              id="statement-import-connector"
              value={viewModel.selectedConnectorId}
              disabled={viewModel.loading}
              onChange={(event) => viewModel.selectConnector(event.target.value)}
            >
              <option value="">Auto-detect connector</option>
              {viewModel.connectors.map((connector) => (
                <option key={connector.connectorId} value={connector.connectorId}>
                  {connector.displayName}
                </option>
              ))}
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="statement-import-profile">Mapping profile</Label>
            <Select
              id="statement-import-profile"
              value={viewModel.selectedProfileId}
              disabled={viewModel.loading}
              onChange={(event) => viewModel.selectProfile(event.target.value)}
            >
              <option value="">Auto-select profile</option>
              {viewModel.profiles.map((profile) => (
                <option key={profile.profileId} value={profile.profileId}>
                  {profile.displayName}
                  {profile.isBuiltIn ? " (built-in)" : ""}
                </option>
              ))}
            </Select>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={!viewModel.selectedProfileId}
            disabledReason="Select a mapping profile first."
            onClick={() => viewModel.editProfile(viewModel.selectedProfileId)}
          >
            Edit mapping profile
          </Button>
          {viewModel.previewBusy ? (
            <span role="status" className="text-xs text-muted-foreground">Previewing statement…</span>
          ) : null}
        </div>
        {viewModel.preview && viewModel.preview.profileSuggestions.length > 0 ? (
          <div className="flex flex-col gap-1.5">
            <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
              Suggested profiles
            </span>
            <ul className="flex flex-wrap gap-2" aria-label="Suggested mapping profiles">
              {viewModel.preview.profileSuggestions.map((suggestion) => (
                <li key={suggestion.profileId}>
                  <button
                    type="button"
                    className={cn(
                      "inline-flex items-center gap-1.5 rounded-[2px] border border-primary/40 bg-primary/10 px-2.5 py-1",
                      "font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-primary transition-colors",
                      "hover:bg-primary/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    )}
                    aria-label={`Apply suggested profile ${suggestion.displayName}, score ${Math.round(suggestion.score * 100)} percent`}
                    onClick={() => viewModel.applyProfileSuggestion(suggestion.profileId)}
                  >
                    {suggestion.displayName}
                    <span aria-hidden="true">{Math.round(suggestion.score * 100)}%</span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function PreviewSection({ viewModel }: { viewModel: StatementImportPanelViewModel }) {
  const preview = viewModel.preview;
  if (!preview) {
    return null;
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Preview: {preview.fileName}</CardTitle>
        <CardDescription>
          {preview.connectorDisplayName} parsed {preview.recordCount} records.{" "}
          {preview.nextAction}
        </CardDescription>
        <div className="mt-1">
          <Badge variant={preview.status === "ReadyToImport" ? "success" : "warning"}>
            {preview.status}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <ColumnMappingTable viewModel={viewModel} />
        <KindSummarySection viewModel={viewModel} />
        <IssueList viewModel={viewModel} />
      </CardContent>
    </Card>
  );
}

function ColumnMappingTable({ viewModel }: { viewModel: StatementImportPanelViewModel }) {
  const mappings = viewModel.preview?.columnMappings ?? [];
  if (mappings.length === 0) {
    return null;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-xs" aria-label="Statement column mappings">
        <caption className="sr-only">Detected statement columns mapped onto canonical Meridian fields</caption>
        <thead>
          <tr className="border-b border-border text-left font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
            <th scope="col" className="px-2 py-1.5">Source column</th>
            <th scope="col" className="px-2 py-1.5">Canonical field</th>
            <th scope="col" className="px-2 py-1.5">Confidence</th>
          </tr>
        </thead>
        <tbody>
          {mappings.map((mapping) => (
            <tr key={mapping.sourceColumn} className="border-b border-border/60">
              <td className="px-2 py-1.5 font-mono">{mapping.sourceColumn}</td>
              <td className="px-2 py-1.5 font-mono">{mapping.canonicalField ?? "—"}</td>
              <td className="px-2 py-1.5">
                <Badge
                  variant={statementColumnConfidenceBadgeVariant(mapping.confidence)}
                  title={`${mapping.rationale} (score ${mapping.score.toFixed(2)})`}
                >
                  {mapping.confidence}
                </Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function KindSummarySection({ viewModel }: { viewModel: StatementImportPanelViewModel }) {
  if (viewModel.kindSummaries.length === 0) {
    return null;
  }

  const selected = viewModel.selectedKindSummary;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2" role="group" aria-label="Record kinds">
        {viewModel.kindSummaries.map((summary) => (
          <button
            key={summary.kind}
            type="button"
            aria-pressed={summary.kind === viewModel.selectedKind}
            className={cn(
              "inline-flex items-center gap-1.5 rounded-[2px] border px-2.5 py-1 font-mono text-[10px] font-semibold uppercase tracking-[0.14em]",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
              summary.kind === viewModel.selectedKind
                ? "border-primary bg-primary/15 text-primary"
                : "border-border bg-secondary/35 text-muted-foreground hover:border-[#ADB8C4]"
            )}
            onClick={() => viewModel.selectKind(summary.kind)}
          >
            {summary.kind}
            <span aria-hidden="true">{summary.recordCount}</span>
            <span className="sr-only">{summary.recordCount} records</span>
          </button>
        ))}
      </div>
      {selected && selected.sampleRecords.length > 0 ? (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-xs" aria-label={`Sample ${selected.kind} records`}>
            <caption className="sr-only">Sample records parsed for the {selected.kind} kind</caption>
            <thead>
              <tr className="border-b border-border text-left font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                <th scope="col" className="px-2 py-1.5">Account</th>
                <th scope="col" className="px-2 py-1.5">Symbol</th>
                <th scope="col" className="px-2 py-1.5">Activity</th>
                <th scope="col" className="px-2 py-1.5">Trade date</th>
                <th scope="col" className="px-2 py-1.5 text-right">Quantity</th>
                <th scope="col" className="px-2 py-1.5 text-right">Price</th>
                <th scope="col" className="px-2 py-1.5 text-right">Cash</th>
              </tr>
            </thead>
            <tbody>
              {selected.sampleRecords.map((record, index) => (
                <tr key={`${record.externalTransactionId ?? record.symbol}-${index}`} className="border-b border-border/60">
                  <td className="px-2 py-1.5 font-mono">{record.account}</td>
                  <td className="px-2 py-1.5 font-mono">{record.symbol}</td>
                  <td className="px-2 py-1.5">{record.activityType}</td>
                  <td className="px-2 py-1.5 font-mono">{record.tradeDate}</td>
                  <td className="px-2 py-1.5 text-right font-mono">{record.quantity}</td>
                  <td className="px-2 py-1.5 text-right font-mono">{record.price}</td>
                  <td className="px-2 py-1.5 text-right font-mono">{record.cashAmount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </div>
  );
}

function IssueList({ viewModel }: { viewModel: StatementImportPanelViewModel }) {
  if (viewModel.issues.length === 0) {
    return null;
  }

  return (
    <ul className="flex flex-col gap-1.5" aria-label="Statement import issues">
      {viewModel.issues.map((issue, index) => (
        <li
          key={`${issue.code}-${issue.rowNumber ?? "file"}-${index}`}
          className="flex flex-wrap items-center gap-2 rounded-[2px] border border-border/60 bg-card px-2 py-1.5 text-xs"
        >
          <Badge variant={statementIssueBadgeVariant(issue.severity)}>{issue.severity}</Badge>
          <span className="font-mono text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
            {issue.code}
            {issue.rowNumber !== null ? ` · row ${issue.rowNumber}` : ""}
            {issue.field ? ` · ${issue.field}` : ""}
          </span>
          <span className="min-w-0 flex-1">
            {issue.message}
            {issue.code === STATEMENT_FORMAT_DRIFT_ISSUE_CODE
              ? " Review the mapping profile before committing — the statement layout drifted from the accepted fingerprint."
              : ""}
          </span>
        </li>
      ))}
    </ul>
  );
}

function ProfileEditorSection({ viewModel }: { viewModel: StatementImportPanelViewModel }) {
  const draft = viewModel.profileDraft;
  if (!draft) {
    return null;
  }

  const readOnly = draft.isBuiltIn;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Mapping profile editor</CardTitle>
        <CardDescription>
          {readOnly
            ? `${draft.displayName} is a built-in profile. Clone it to make an editable copy.`
            : "Adjust field mappings, aliases, and activity codes, then save to re-run the preview."}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {viewModel.profileSaveError ? (
          <ErrorBanner title="Saving the mapping profile failed" error={viewModel.profileSaveError} />
        ) : null}
        {viewModel.profileDraftError ? (
          <StatusBanner tone="warning" title="Profile draft needs attention" detail={viewModel.profileDraftError} />
        ) : null}
        {viewModel.profileSaveMessage ? (
          <StatusBanner tone="success" title="Mapping profile saved" detail={viewModel.profileSaveMessage} />
        ) : null}

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="statement-profile-id">Profile id</Label>
            <Input
              id="statement-profile-id"
              value={draft.profileId}
              disabled={readOnly || !draft.isClone}
              onChange={(event) => viewModel.updateProfileDraft({ profileId: event.target.value })}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="statement-profile-display-name">Display name</Label>
            <Input
              id="statement-profile-display-name"
              value={draft.displayName}
              disabled={readOnly}
              onChange={(event) => viewModel.updateProfileDraft({ displayName: event.target.value })}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="statement-profile-delimiter">CSV delimiter</Label>
            <Input
              id="statement-profile-delimiter"
              value={draft.csvDelimiter}
              maxLength={1}
              disabled={readOnly}
              onChange={(event) => viewModel.updateProfileDraft({ csvDelimiter: event.target.value })}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="statement-profile-date-formats">Date formats (comma separated)</Label>
            <Input
              id="statement-profile-date-formats"
              value={draft.dateFormatsText}
              disabled={readOnly}
              onChange={(event) => viewModel.updateProfileDraft({ dateFormatsText: event.target.value })}
            />
          </div>
        </div>

        <fieldset className="flex flex-col gap-2 border-0 p-0">
          <legend className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
            Field mappings
          </legend>
          {draft.fields.map((field, index) => (
            <div key={index} className="grid items-end gap-2 sm:grid-cols-[1fr_1fr_1fr_auto_auto]">
              <div className="flex flex-col gap-1">
                <Label htmlFor={`statement-profile-field-canonical-${index}`}>Canonical field</Label>
                <Input
                  id={`statement-profile-field-canonical-${index}`}
                  value={field.canonicalField}
                  disabled={readOnly}
                  onChange={(event) => viewModel.updateProfileDraftField(index, { canonicalField: event.target.value })}
                />
              </div>
              <div className="flex flex-col gap-1">
                <Label htmlFor={`statement-profile-field-source-${index}`}>Source column</Label>
                <Input
                  id={`statement-profile-field-source-${index}`}
                  value={field.sourceColumn}
                  disabled={readOnly}
                  onChange={(event) => viewModel.updateProfileDraftField(index, { sourceColumn: event.target.value })}
                />
              </div>
              <div className="flex flex-col gap-1">
                <Label htmlFor={`statement-profile-field-aliases-${index}`}>Aliases (comma separated)</Label>
                <Input
                  id={`statement-profile-field-aliases-${index}`}
                  value={field.aliasesText}
                  disabled={readOnly}
                  onChange={(event) => viewModel.updateProfileDraftField(index, { aliasesText: event.target.value })}
                />
              </div>
              <Checkbox
                label="Required"
                checked={field.required}
                disabled={readOnly}
                onCheckedChange={(checked) => viewModel.updateProfileDraftField(index, { required: checked })}
              />
              <Button
                type="button"
                size="icon"
                variant="ghost"
                aria-label={`Remove field mapping ${field.canonicalField || index + 1}`}
                disabled={readOnly}
                onClick={() => viewModel.removeProfileDraftField(index)}
              >
                <Trash2 className="h-4 w-4" aria-hidden="true" />
              </Button>
            </div>
          ))}
          <div>
            <Button type="button" size="sm" variant="outline" disabled={readOnly} onClick={viewModel.addProfileDraftField}>
              <Plus className="h-3.5 w-3.5" aria-hidden="true" />
              Add field mapping
            </Button>
          </div>
        </fieldset>

        <fieldset className="flex flex-col gap-2 border-0 p-0">
          <legend className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
            Activity codes
          </legend>
          {draft.activityCodes.map((code, index) => (
            <div key={index} className="grid items-end gap-2 sm:grid-cols-[1fr_1fr_auto]">
              <div className="flex flex-col gap-1">
                <Label htmlFor={`statement-profile-activity-source-${index}`}>Source code</Label>
                <Input
                  id={`statement-profile-activity-source-${index}`}
                  value={code.sourceCode}
                  disabled={readOnly}
                  onChange={(event) => viewModel.updateProfileDraftActivityCode(index, { sourceCode: event.target.value })}
                />
              </div>
              <div className="flex flex-col gap-1">
                <Label htmlFor={`statement-profile-activity-canonical-${index}`}>Canonical activity type</Label>
                <Input
                  id={`statement-profile-activity-canonical-${index}`}
                  value={code.canonicalActivityType}
                  disabled={readOnly}
                  onChange={(event) =>
                    viewModel.updateProfileDraftActivityCode(index, { canonicalActivityType: event.target.value })}
                />
              </div>
              <Button
                type="button"
                size="icon"
                variant="ghost"
                aria-label={`Remove activity code ${code.sourceCode || index + 1}`}
                disabled={readOnly}
                onClick={() => viewModel.removeProfileDraftActivityCode(index)}
              >
                <Trash2 className="h-4 w-4" aria-hidden="true" />
              </Button>
            </div>
          ))}
          <div>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={readOnly}
              onClick={viewModel.addProfileDraftActivityCode}
            >
              <Plus className="h-3.5 w-3.5" aria-hidden="true" />
              Add activity code
            </Button>
          </div>
        </fieldset>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            size="sm"
            disabled={readOnly}
            busy={viewModel.profileSaveBusy}
            busyLabel="Saving mapping profile…"
            disabledReason="Built-in profiles are read-only. Clone the profile to edit a copy."
            onClick={() => void viewModel.saveProfileDraft()}
          >
            Save profile
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={() => viewModel.cloneProfile(draft.isClone ? viewModel.selectedProfileId : draft.profileId)}
          >
            Clone profile
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={viewModel.closeProfileEditor}>
            Close editor
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function CommitSection({ viewModel }: { viewModel: StatementImportPanelViewModel }) {
  const errors = viewModel.commitFormErrors;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Commit import</CardTitle>
        <CardDescription>
          Attach the statement to a fund account and reporting period, then commit it into reconciliation.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {viewModel.commitError ? (
          <ErrorBanner title="Statement import commit failed" error={viewModel.commitError} />
        ) : null}
        {viewModel.hasBlockingIssues ? (
          <StatusBanner
            tone="warning"
            title="Preview reported blocking errors"
            detail="Resolve the error-severity issues above before committing; the server rejects imports with blocking parse errors."
          />
        ) : null}

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="statement-commit-source-kind">Source kind</Label>
            <Select
              id="statement-commit-source-kind"
              value={viewModel.commitForm.sourceKind}
              onChange={(event) => viewModel.updateCommitForm("sourceKind", event.target.value)}
            >
              <option value="broker">Broker</option>
              <option value="custodian">Custodian</option>
            </Select>
          </div>
          <CommitField
            id="statement-commit-source-institution"
            label="Source institution"
            value={viewModel.commitForm.sourceInstitution}
            error={errors.sourceInstitution}
            onChange={(value) => viewModel.updateCommitForm("sourceInstitution", value)}
          />
          <CommitField
            id="statement-commit-fund-account"
            label="Fund account"
            value={viewModel.commitForm.fundAccountId}
            error={errors.fundAccountId}
            onChange={(value) => viewModel.updateCommitForm("fundAccountId", value)}
          />
          <CommitField
            id="statement-commit-external-account"
            label="External account"
            value={viewModel.commitForm.externalAccountId}
            error={errors.externalAccountId}
            onChange={(value) => viewModel.updateCommitForm("externalAccountId", value)}
          />
          <CommitField
            id="statement-commit-period-start"
            label="Period start"
            type="date"
            value={viewModel.commitForm.periodStart}
            error={errors.periodStart}
            onChange={(value) => viewModel.updateCommitForm("periodStart", value)}
          />
          <CommitField
            id="statement-commit-period-end"
            label="Period end"
            type="date"
            value={viewModel.commitForm.periodEnd}
            error={errors.periodEnd}
            onChange={(value) => viewModel.updateCommitForm("periodEnd", value)}
          />
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            disabled={!viewModel.canCommit}
            disabledReason={viewModel.commitDisabledReason ?? errors.file ?? "Preview the statement before committing."}
            busy={viewModel.commitBusy}
            busyLabel="Committing statement import…"
            onClick={() => void viewModel.commit()}
          >
            Commit statement import
          </Button>
        </div>

        <CommitResultPanel viewModel={viewModel} />
      </CardContent>
    </Card>
  );
}

function CommitField({
  id,
  label,
  value,
  error,
  type = "text",
  onChange
}: {
  id: string;
  label: string;
  value: string;
  error?: string;
  type?: string;
  onChange: (value: string) => void;
}) {
  const errorId = `${id}-error`;
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        type={type}
        value={value}
        error={Boolean(error)}
        aria-describedby={error ? errorId : undefined}
        onChange={(event) => onChange(event.target.value)}
      />
      {error ? (
        <p id={errorId} className="text-xs text-danger" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}

function CommitResultPanel({ viewModel }: { viewModel: StatementImportPanelViewModel }) {
  const result = viewModel.commitResult;
  if (!result) {
    return null;
  }

  if (viewModel.commitOutcome === "duplicate") {
    return (
      <PanelSurface role="status" className="flex flex-col gap-3 p-4">
        <StatusBanner
          tone="warning"
          title="Duplicate statement detected"
          detail={`This statement was already imported as run ${result.runId}. No new records were committed. ${result.nextAction}`}
        />
        <CommitResultActions result={result} />
      </PanelSurface>
    );
  }

  return (
    <PanelSurface role="status" className="flex flex-col gap-3 p-4">
      <StatusBanner
        tone="success"
        title="Statement import committed"
        detail={`Run ${result.runId} committed ${result.recordCount} records with ${result.breakCount} breaks and ${result.caseCount} cases. ${result.nextAction}`}
      />
      <dl className="grid gap-2 text-xs sm:grid-cols-2">
        <div>
          <dt className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Retained source</dt>
          <dd className="font-mono">{result.retainedSourcePath}</dd>
        </div>
        <div>
          <dt className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Retained canonical</dt>
          <dd className="font-mono">{result.retainedCanonicalPath}</dd>
        </div>
        {result.evidenceVaultIdentity ? (
          <div>
            <dt className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Evidence vault</dt>
            <dd className="font-mono">{result.evidenceVaultIdentity.vaultId}</dd>
          </div>
        ) : null}
      </dl>
      {result.nextActions && result.nextActions.length > 0 ? (
        <ul className="flex flex-col gap-1 text-xs text-muted-foreground" aria-label="Statement import next actions">
          {result.nextActions.map((action) => (
            <li key={action}>{action}</li>
          ))}
        </ul>
      ) : null}
      <CommitCaseLinks result={result} />
      <CommitResultActions result={result} />
    </PanelSurface>
  );
}

function CommitCaseLinks({ result }: { result: StatementImportPanelViewModel["commitResult"] }) {
  const caseLinks = result ? resolveStatementCaseLinks(result) : [];
  if (!result || caseLinks.length === 0) {
    return null;
  }

  const visibleCaseLinks = caseLinks.slice(0, 5);
  const remainingCount = caseLinks.length - visibleCaseLinks.length;

  return (
    <div className="flex flex-col gap-2" aria-label="Statement import reconciliation cases">
      <span className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
        Reconciliation cases
      </span>
      <ul className="grid gap-2 sm:grid-cols-2">
        {visibleCaseLinks.map((caseLink) => (
          <li
            key={caseLink.caseId}
            className="flex min-w-0 flex-col gap-2 rounded-[2px] border border-border/70 bg-card/70 p-2 text-xs"
          >
            <div className="flex min-w-0 flex-wrap items-center gap-2">
              <span className="min-w-[10rem] flex-1 truncate font-medium text-foreground">{caseLink.label}</span>
              <Button asChild variant="outline" size="sm">
                <Link to={caseLink.route}>
                  <span className="max-w-[14rem] truncate font-mono text-[11px]">{caseLink.caseId}</span>
                  <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                </Link>
              </Button>
              <Badge variant="outline">{caseLink.status}</Badge>
              <Badge variant={statementCasePriorityBadgeVariant(caseLink.priority)}>{caseLink.priority}</Badge>
            </div>
            <p className="line-clamp-2 text-muted-foreground">{caseLink.reason}</p>
            {caseLink.suggestedNextAction ? (
              <p className="line-clamp-2 text-muted-foreground">
                Next: {caseLink.suggestedNextAction}
              </p>
            ) : null}
          </li>
        ))}
      </ul>
      {remainingCount > 0 ? (
        <span className="text-xs text-muted-foreground">
          {remainingCount} more case{remainingCount === 1 ? "" : "s"} are available from the reconciliation queue.
        </span>
      ) : null}
    </div>
  );
}

interface ResolvedStatementCaseLink {
  caseId: string;
  breakId: string | null;
  route: string;
  label: string;
  status: string;
  priority: string;
  reason: string;
  suggestedNextAction: string;
}

function resolveStatementCaseLinks(
  result: NonNullable<StatementImportPanelViewModel["commitResult"]>
): ResolvedStatementCaseLink[] {
  const structuredLinks = (result.reconciliationCaseLinks ?? [])
    .map((link) => ({
      caseId: link.caseId?.trim() ?? "",
      breakId: link.breakId?.trim() || null,
      route: link.route?.trim() ?? "",
      label: link.label?.trim() ?? "",
      status: link.status?.trim() ?? "",
      priority: link.priority?.trim() ?? "",
      reason: link.reason?.trim() ?? "",
      suggestedNextAction: link.suggestedNextAction?.trim() ?? ""
    }))
    .filter((link) => link.caseId);

  if (structuredLinks.length > 0) {
    return dedupeCaseLinks(structuredLinks.map((link, index) => ({
      ...link,
      route: link.route || resolveStatementCaseRoute(result, link.caseId, index),
      label: link.label || `Reconciliation case ${link.caseId}`,
      status: link.status || "Open",
      priority: link.priority || "Normal",
      reason: link.reason || "Statement import created a reconciliation case.",
      suggestedNextAction: link.suggestedNextAction || "Review the linked statement evidence before disposition."
    })));
  }

  return dedupeCaseLinks((result.caseIds ?? [])
    .map((caseId, index) => {
      const trimmedCaseId = caseId.trim();
      if (!trimmedCaseId) {
        return null;
      }

      const breakId = trimmedCaseId.toLowerCase().startsWith("case:")
        ? trimmedCaseId.slice("case:".length).trim() || null
        : null;
      return {
        caseId: trimmedCaseId,
        breakId,
        route: resolveStatementCaseRoute(result, trimmedCaseId, index),
        label: `Reconciliation case ${trimmedCaseId}`,
        status: "Open",
        priority: "Normal",
        reason: "Statement import created a reconciliation case.",
        suggestedNextAction: "Review the linked statement evidence before disposition."
      };
    })
    .filter((link): link is ResolvedStatementCaseLink => Boolean(link)));
}

function dedupeCaseLinks(links: ResolvedStatementCaseLink[]) {
  const seen = new Set<string>();
  const deduped: ResolvedStatementCaseLink[] = [];
  for (const link of links) {
    const key = link.caseId.toLowerCase();
    if (seen.has(key)) {
      continue;
    }

    seen.add(key);
    deduped.push(link);
  }

  return deduped;
}

function statementCasePriorityBadgeVariant(priority: string): "outline" | "warning" | "danger" | "paper" {
  switch (priority.toLowerCase()) {
    case "critical":
      return "danger";
    case "high":
      return "warning";
    case "low":
      return "paper";
    default:
      return "outline";
  }
}

function CommitResultActions({ result }: { result: StatementImportPanelViewModel["commitResult"] }) {
  if (!result) {
    return null;
  }

  const evidenceRoute = result.evidenceWorkbenchRoute ?? WORKSTATION_ROUTE_CATALOG.accountingEvidence;
  const reconciliationRoute = result.reconciliationRoute ?? WORKSTATION_ROUTE_CATALOG.accountingReconciliationMatch;

  return (
    <div className="flex flex-wrap gap-2">
      <Button asChild variant="outline" size="sm">
        <Link to={evidenceRoute}>
          <span>Open Evidence Vault</span>
          <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Link>
      </Button>
      <Button asChild variant="outline" size="sm">
        <Link to={reconciliationRoute}>
          <span>Open reconciliation queue</span>
          <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Link>
      </Button>
    </div>
  );
}

function resolveStatementCaseRoute(
  result: NonNullable<StatementImportPanelViewModel["commitResult"]>,
  caseId: string,
  index: number
) {
  const explicitRoute = result.reconciliationCaseRoutes?.[index];
  if (explicitRoute && explicitRoute.trim()) {
    return explicitRoute;
  }

  const baseRoute = result.reconciliationRoute ?? WORKSTATION_ROUTE_CATALOG.accountingReconciliationMatch;
  const separator = baseRoute.includes("?") ? "&" : "?";
  const caseParam = `caseId=${encodeURIComponent(caseId)}`;
  const breakId = caseId.toLowerCase().startsWith("case:") ? caseId.slice("case:".length).trim() : "";
  return breakId
    ? `${baseRoute}${separator}${caseParam}&breakId=${encodeURIComponent(breakId)}`
    : `${baseRoute}${separator}${caseParam}`;
}

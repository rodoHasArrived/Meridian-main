import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState
} from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { describeApiError } from "@/lib/api-errors";
import { redactReportingCredentialText, safeReportingHref } from "@/lib/reporting-link-safety";
import {
  approveGovernedReportingRestatement,
  approveGovernedReportingRun,
  getGovernedReportingRun,
  getGovernedReportingSeriesHistory,
  getSecureReportingAccessGrantHistory,
  getSecureReportingDeliveryHistory,
  getSecureReportingTransportCapabilities,
  issueSecureReportingAccessGrant,
  queueSecureReportingDelivery,
  releaseGovernedReportingRun,
  requestGovernedReportingRestatement,
  revokeSecureReportingAccessGrant,
  submitGovernedReportingRun,
  validateGovernedReportingRun
} from "@/lib/reporting-governance-api";
import { secureReportingArtifactDownloadPath } from "@/lib/reporting-governance-routes";
import {
  enforceClientPackageArtifactSelection, resolveClientPackageArtifactGate, type ClientPackageArtifactGate
} from "@/screens/report-run-governance-client-package";
import type {
  GovernedReportingRun,
  ReportingGovernanceRestatement,
  ReportingGovernanceSeriesHistory,
  SecureReportingAccessGrant,
  SecureReportingDelivery,
  SecureReportingDistributionCapabilityCatalog,
  SecureReportingIssuedAccessGrant
} from "@/types/reporting-governance";

type ResourcePhase = "loading" | "ready" | "unavailable";

interface ResourceState<T> {
  phase: ResourcePhase;
  data: T;
  detail: string | null;
}

interface GovernanceLoadState {
  phase: "loading" | "ready" | "error";
  run: GovernedReportingRun | null;
  error: string | null;
  series: ResourceState<ReportingGovernanceSeriesHistory | null>;
  deliveries: ResourceState<SecureReportingDelivery[]>;
  grants: ResourceState<SecureReportingAccessGrant[]>;
  transports: ResourceState<SecureReportingDistributionCapabilityCatalog>;
}

type MutationState =
  | { phase: "idle"; label: null; message: null }
  | { phase: "running"; label: string; message: string }
  | { phase: "success" | "error"; label: string; message: string };

type GovernedActionSubject = Pick<
  GovernedReportingRun | ReportingGovernanceRestatement,
  "version" | "actionAvailability"
>;

interface ActionDecision {
  allowed: boolean;
  reason: string;
}

const lifecycleSteps = ["Draft", "Validated", "InReview", "Approved", "Released"] as const;
const emptyMutation: MutationState = { phase: "idle", label: null, message: null };
const unavailableDistributionCapabilities: SecureReportingDistributionCapabilityCatalog = {
  canQueueDelivery: false,
  canIssueAccessGrant: false,
  canRevokeAccessGrant: false,
  actionDisabledReasonCode: "DISTRIBUTION_CAPABILITIES_UNAVAILABLE",
  transports: []
};

function loadingResource<T>(data: T): ResourceState<T> {
  return { phase: "loading", data, detail: null };
}

function initialLoadState(): GovernanceLoadState {
  return {
    phase: "loading",
    run: null,
    error: null,
    series: loadingResource(null),
    deliveries: loadingResource([]),
    grants: loadingResource([]),
    transports: loadingResource(unavailableDistributionCapabilities)
  };
}

export function ReportRunGovernanceScreen() {
  const [searchParams] = useSearchParams();
  const runId = searchParams.get("runId")?.trim() ?? "";
  const [refreshSequence, setRefreshSequence] = useState(0);
  const [loadState, setLoadState] = useState<GovernanceLoadState>(initialLoadState);
  const [mutation, setMutation] = useState<MutationState>(emptyMutation);
  const [approvalNote, setApprovalNote] = useState("");
  const [restatementReason, setRestatementReason] = useState("");
  const [distributionId, setDistributionId] = useState("");
  const [transportId, setTransportId] = useState("");
  const [recipientPrincipalId, setRecipientPrincipalId] = useState("");
  const [recipientPrincipalKind, setRecipientPrincipalKind] = useState<"User" | "Group" | "Company">("User");
  const [destination, setDestination] = useState("");
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [maxAttempts, setMaxAttempts] = useState("3");
  const [grantLifetimeSeconds, setGrantLifetimeSeconds] = useState("");
  const [grantMaxUses, setGrantMaxUses] = useState("");
  const [grantRevocationReason, setGrantRevocationReason] = useState("");
  const [selectedArtifactIds, setSelectedArtifactIds] = useState<string[] | null>(null);
  const [issuedGrant, setIssuedGrant] = useState<SecureReportingIssuedAccessGrant | null>(null);
  const mutationControllerRef = useRef<AbortController | null>(null);

  const refresh = useCallback(() => setRefreshSequence((current) => current + 1), []);

  useEffect(() => {
    setSelectedArtifactIds(null);
    setIssuedGrant(null);
    setMutation(emptyMutation);
  }, [runId]);

  useEffect(() => {
    if (!runId) {
      setLoadState({
        ...initialLoadState(),
        phase: "error",
        error: "A governed report run identifier is required. No report data was requested."
      });
      return;
    }

    const controller = new AbortController();
    setLoadState((current) => ({
      ...initialLoadState(),
      phase: "loading",
      run: current.run?.runId === runId ? current.run : null
    }));

    const options = { signal: controller.signal };
    const runPromise = getGovernedReportingRun(runId, options);
    Promise.allSettled([
      runPromise,
      runPromise.then((governedRun) => getGovernedReportingSeriesHistory(governedRun.seriesId, options)),
      getSecureReportingDeliveryHistory(runId, options),
      getSecureReportingAccessGrantHistory(runId, options),
      getSecureReportingTransportCapabilities(options)
    ]).then(([runResult, seriesResult, deliveryResult, grantResult, transportResult]) => {
      if (controller.signal.aborted) {
        return;
      }

      if (runResult.status === "rejected") {
        setLoadState({
          ...initialLoadState(),
          phase: "error",
          run: null,
          error: describeFailure(runResult.reason, "The governed report run could not be loaded.")
        });
        return;
      }

      setLoadState({
        phase: "ready",
        run: runResult.value,
        error: null,
        series: projectOptionalResult(
          seriesResult,
          null,
          "Revision and restatement discovery is unavailable from the server. No history is inferred locally."
        ),
        deliveries: projectOptionalResult(
          deliveryResult,
          [],
          "Secure delivery history is unavailable or not authorized for this run."
        ),
        grants: projectOptionalResult(
          grantResult,
          [],
          "Access-grant history is unavailable or not authorized for this run."
        ),
        transports: projectDistributionCapabilityResult(transportResult)
      });
    });

    return () => controller.abort();
  }, [refreshSequence, runId]);

  useEffect(() => () => mutationControllerRef.current?.abort(), []);

  const run = loadState.run;
  const releaseArtifacts = run?.release?.artifacts ?? [];
  const clientPackageArtifactGate = resolveClientPackageArtifactGate(run);
  const effectiveArtifactIds = enforceClientPackageArtifactSelection(
    selectedArtifactIds ?? releaseArtifacts.map((artifact) => artifact.artifactId),
    releaseArtifacts.map((artifact) => artifact.artifactId),
    clientPackageArtifactGate
  );
  const parameterEntries = useMemo(() => projectParameterEntries(run), [run]);
  const readyTransports = loadState.transports.data.transports.filter((transport) => transport.isReady);
  const effectiveTransportId = transportId || readyTransports[0]?.transportId || "";
  const selectedTransport = loadState.transports.data.transports.find(
    (transport) => transport.transportId === effectiveTransportId
  ) ?? null;
  const issuedRecipientLink = issuedGrant ? safeRecipientAccessUri(issuedGrant.recipientAccessUri) : null;

  const validateDecision = resolveServerAction(run, ["ValidateRun", "Validate"]);
  const submitDecision = resolveServerAction(run, ["SubmitRun", "Submit", "SubmitForReview"]);
  const approveDecision = resolveServerAction(run, ["ApproveRun", "Approve"]);
  const releaseDecision = resolveServerAction(run, ["ReleaseRun", "Release"]);
  const restatementDecision = resolveServerAction(run, ["RequestRestatement", "Restate"]);
  const isMutating = mutation.phase === "running";
  const isReleased = run?.governanceState === "Released";

  const runMutation = useCallback(async (
    label: string,
    action: (signal: AbortSignal) => Promise<unknown>,
    successMessage: string
  ) => {
    mutationControllerRef.current?.abort();
    const controller = new AbortController();
    mutationControllerRef.current = controller;
    setMutation({ phase: "running", label, message: `${label} is being recorded by the server.` });

    try {
      await action(controller.signal);
      if (controller.signal.aborted) {
        return;
      }
      setMutation({ phase: "success", label, message: successMessage });
      refresh();
    } catch (error) {
      if (controller.signal.aborted) {
        return;
      }
      const message = describeFailure(error, `${label} failed.`);
      setMutation({
        phase: "error",
        label,
        message: isConflict(error)
          ? `${message} The retained version changed; refresh and review the current audit trail before retrying.`
          : message
      });
    }
  }, [refresh]);

  const onValidate = () => {
    if (!run || !validateDecision.allowed) return;
    void runMutation(
      "Validate report run",
      (signal) => validateGovernedReportingRun(run.runId, run.version, { signal }),
      "The report run was validated against the server-owned readiness receipt."
    );
  };

  const onSubmit = () => {
    if (!run || !submitDecision.allowed) return;
    void runMutation(
      "Submit report run",
      (signal) => submitGovernedReportingRun(run.runId, run.version, { signal }),
      "The report run was submitted for independent review."
    );
  };

  const onApprove = () => {
    if (!run || !approveDecision.allowed || !approvalNote.trim()) return;
    void runMutation(
      "Approve report run",
      (signal) => approveGovernedReportingRun(run.runId, run.version, approvalNote.trim(), { signal }),
      "The independent approval and decision note were retained."
    );
  };

  const onRelease = () => {
    if (!run || !releaseDecision.allowed) return;
    void runMutation(
      "Release report run",
      (signal) => releaseGovernedReportingRun(run.runId, run.version, { signal }),
      "The immutable report package was released and is eligible for governed distribution."
    );
  };

  const onRequestRestatement = () => {
    if (!run || !restatementDecision.allowed || !restatementReason.trim()) return;
    void runMutation(
      "Request restatement",
      (signal) => requestGovernedReportingRestatement(
        run.runId,
        run.version,
        restatementReason.trim(),
        { signal }
      ),
      "The restatement request was retained for independent approval."
    );
  };

  const onApproveRestatement = (request: ReportingGovernanceRestatement) => {
    const decision = resolveServerAction(request, ["ApproveRestatement", "ApproveRestatementRequest"]);
    if (!decision.allowed) return;
    void runMutation(
      "Approve restatement",
      (signal) => approveGovernedReportingRestatement(request.requestId, request.version, { signal }),
      "The restatement was approved and a new independently governed draft revision was created."
    );
  };

  const onQueueDelivery = () => {
    if (
      !run
      || !isReleased
      || !loadState.transports.data.canQueueDelivery
      || !selectedTransport?.isReady
      || !distributionId.trim()
      || !subject.trim()
      || !body.trim()
      || (selectedTransport.requiresDestination && !destination.trim())
      || effectiveArtifactIds.length === 0
      || !clientPackageArtifactGate.isComplete
    ) {
      return;
    }

    void runMutation(
      "Queue secure delivery",
      (signal) => queueSecureReportingDelivery({
        runId: run.runId,
        distributionId: distributionId.trim(),
        transportId: selectedTransport.transportId,
        recipientPrincipalId: recipientPrincipalId.trim() || null,
        recipientPrincipalKind: recipientPrincipalId.trim() ? recipientPrincipalKind : null,
        destination: destination.trim(),
        subject: subject.trim(),
        body: body.trim(),
        artifactIds: effectiveArtifactIds,
        grantLifetimeSeconds: parseOptionalPositiveInteger(grantLifetimeSeconds),
        grantMaxUses: parseOptionalPositiveInteger(grantMaxUses),
        maxAttempts: parseOptionalPositiveInteger(maxAttempts) ?? 3
      }, { signal }),
      "The delivery was durably queued. Provider receipts will update its retained history."
    );
  };

  const onIssueGrant = () => {
    if (
      !run
      || !isReleased
      || !loadState.transports.data.canIssueAccessGrant
      || effectiveArtifactIds.length === 0
      || !clientPackageArtifactGate.isComplete
    ) return;
    mutationControllerRef.current?.abort();
    const controller = new AbortController();
    mutationControllerRef.current = controller;
    setIssuedGrant(null);
    setMutation({
      phase: "running",
      label: "Issue recipient access",
      message: "The server is issuing an opaque, scoped access grant."
    });

    issueSecureReportingAccessGrant({
      runId: run.runId,
      recipientPrincipalId: recipientPrincipalId.trim() || null,
      recipientPrincipalKind: recipientPrincipalId.trim() ? recipientPrincipalKind : null,
      artifactIds: effectiveArtifactIds,
      lifetimeSeconds: parseOptionalPositiveInteger(grantLifetimeSeconds),
      maxUses: parseOptionalPositiveInteger(grantMaxUses)
    }, { signal: controller.signal })
      .then((grant) => {
        if (controller.signal.aborted) return;
        setIssuedGrant(grant);
        setMutation({
          phase: safeRecipientAccessUri(grant.recipientAccessUri) ? "success" : "error",
          label: "Issue recipient access",
          message: safeRecipientAccessUri(grant.recipientAccessUri)
            ? "Recipient access was issued. The opaque fragment link is shown once and is not copied into audit text."
            : "Recipient access was issued, but its link used an unsafe query or unsupported URL and was suppressed. Revoke the grant before retrying."
        });
        refresh();
      })
      .catch((error) => {
        if (!controller.signal.aborted) {
          setMutation({
            phase: "error",
            label: "Issue recipient access",
            message: describeFailure(error, "Recipient access could not be issued.")
          });
        }
      });
  };

  const onRevokeGrant = (grantId: string) => {
    if (
      !isReleased
      || !loadState.transports.data.canRevokeAccessGrant
      || !grantRevocationReason.trim()
    ) return;
    void runMutation(
      "Revoke recipient access",
      (signal) => revokeSecureReportingAccessGrant(grantId, grantRevocationReason.trim(), { signal }),
      "The access grant was revoked and future exchanges will fail closed."
    );
  };

  if (!runId) {
    return (
      <StatusBanner
        role="alert"
        tone="warning"
        title="Select a governed report run"
        detail="Open this route with a retained runId. Meridian will not substitute a recent fixture or another report run."
      />
    );
  }

  if (loadState.phase === "loading" && !run) {
    return (
      <Card className="panel-surface" role="status" aria-busy="true" aria-live="polite">
        <CardHeader>
          <CardTitle>Loading governed report run</CardTitle>
          <CardDescription>Verifying immutable scope, snapshot, readiness, artifacts, and distribution evidence.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (loadState.phase === "error" || !run) {
    return (
      <div className="space-y-3">
        <StatusBanner
          role="alert"
          tone="danger"
          title="Governed report run unavailable"
          detail={loadState.error ?? "No authoritative report run was returned."}
        />
        <Button type="button" size="sm" variant="outline" onClick={refresh}>Retry authoritative load</Button>
      </div>
    );
  }

  return (
    <div className="space-y-4" aria-busy={loadState.phase === "loading" || isMutating}>
      <RunHeader run={run} onRefresh={refresh} refreshing={loadState.phase === "loading"} />

      {mutation.phase !== "idle" ? (
        <StatusBanner
          role={mutation.phase === "error" ? "alert" : "status"}
          aria-live="polite"
          tone={mutation.phase === "error" ? "danger" : mutation.phase === "success" ? "success" : "info"}
          title={mutation.label}
          detail={mutation.message}
        />
      ) : null}

      {issuedGrant && issuedRecipientLink ? (
        <StatusBanner
          role="status"
          tone="warning"
          title="One-time recipient link ready"
          detail={
            <span className="flex flex-wrap items-center gap-2">
              <span>Expires {formatTimestamp(issuedGrant.expiresAtUtc)}. Share only with the governed audience.</span>
              <a
                className="font-semibold underline underline-offset-2"
                href={issuedRecipientLink}
                target="_blank"
                rel="noopener noreferrer"
              >
                Open recipient access
              </a>
            </span>
          }
        />
      ) : null}

      <LifecyclePanel
        run={run}
        approvalNote={approvalNote}
        onApprovalNoteChange={setApprovalNote}
        decisions={{ validateDecision, submitDecision, approveDecision, releaseDecision }}
        busy={isMutating}
        onValidate={onValidate}
        onSubmit={onSubmit}
        onApprove={onApprove}
        onRelease={onRelease}
      />

      <section className="grid gap-4 xl:grid-cols-2" aria-label="Immutable certification evidence">
        <ParameterPanel run={run} entries={parameterEntries} />
        <ScopeAccessPanel run={run} />
        <SnapshotPanel run={run} />
        <ReadinessPanel run={run} />
      </section>

      <ArtifactPanel
        run={run}
        selectedArtifactIds={effectiveArtifactIds}
        clientPackageArtifactGate={clientPackageArtifactGate}
        onToggleArtifact={(artifactId) => setSelectedArtifactIds((current) => {
          if (clientPackageArtifactGate.requiredArtifactIds.includes(artifactId)) {
            return current;
          }
          const selected = current ?? releaseArtifacts.map((artifact) => artifact.artifactId);
          return selected.includes(artifactId)
            ? selected.filter((candidate) => candidate !== artifactId)
            : [...selected, artifactId];
        })}
      />

      <AuditPanel run={run} />

      <RestatementPanel
        run={run}
        series={loadState.series}
        reason={restatementReason}
        onReasonChange={setRestatementReason}
        requestDecision={restatementDecision}
        busy={isMutating}
        onRequest={onRequestRestatement}
        onApprove={onApproveRestatement}
      />

      <DistributionPanel
        run={run}
        transports={loadState.transports}
        deliveries={loadState.deliveries}
        grants={loadState.grants}
        selectedArtifactIds={effectiveArtifactIds}
        clientPackageArtifactGate={clientPackageArtifactGate}
        transportId={effectiveTransportId}
        onTransportIdChange={setTransportId}
        distributionId={distributionId}
        onDistributionIdChange={setDistributionId}
        recipientPrincipalId={recipientPrincipalId}
        onRecipientPrincipalIdChange={setRecipientPrincipalId}
        recipientPrincipalKind={recipientPrincipalKind}
        onRecipientPrincipalKindChange={setRecipientPrincipalKind}
        destination={destination}
        onDestinationChange={setDestination}
        subject={subject}
        onSubjectChange={setSubject}
        body={body}
        onBodyChange={setBody}
        maxAttempts={maxAttempts}
        onMaxAttemptsChange={setMaxAttempts}
        grantLifetimeSeconds={grantLifetimeSeconds}
        onGrantLifetimeSecondsChange={setGrantLifetimeSeconds}
        grantMaxUses={grantMaxUses}
        onGrantMaxUsesChange={setGrantMaxUses}
        revocationReason={grantRevocationReason}
        onRevocationReasonChange={setGrantRevocationReason}
        busy={isMutating}
        onQueueDelivery={onQueueDelivery}
        onIssueGrant={onIssueGrant}
        onRevokeGrant={onRevokeGrant}
      />
    </div>
  );
}

function RunHeader({
  run,
  onRefresh,
  refreshing
}: {
  run: GovernedReportingRun;
  onRefresh: () => void;
  refreshing: boolean;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <CardTitle>Report Run Detail</CardTitle>
          <CardDescription>
            Live governed revision {run.revision} of {run.templateId}; no workspace fixture or recent-run fallback is used.
          </CardDescription>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={governanceStateVariant(run.governanceState)}>{run.governanceState}</Badge>
          <Badge variant={executionStateVariant(run.executionState)}>{run.executionState}</Badge>
          <Button
            type="button"
            size="sm"
            variant="outline"
            busy={refreshing}
            busyLabel="Refreshing…"
            onClick={onRefresh}
          >
            Refresh retained state
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <dl className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Fact label="Run ID" value={run.runId} mono />
          <Fact label="Series ID" value={run.seriesId} mono />
          <Fact label="Template version" value={`${run.templateId} · ${run.templateVersion}`} />
          <Fact label="Retained version" value={String(run.version)} mono />
          <Fact label="Created" value={formatTimestamp(run.createdAtUtc)} />
          <Fact label="Created by" value={run.creationAuthority.actorId} />
          <Fact label="Restatement of" value={run.restatementOfRunId ?? "Original revision"} mono />
          <Fact label="Correlation" value={run.creationAuthority.correlationId} mono />
        </dl>
      </CardContent>
    </Card>
  );
}

function LifecyclePanel({
  run,
  approvalNote,
  onApprovalNoteChange,
  decisions,
  busy,
  onValidate,
  onSubmit,
  onApprove,
  onRelease
}: {
  run: GovernedReportingRun;
  approvalNote: string;
  onApprovalNoteChange: (value: string) => void;
  decisions: {
    validateDecision: ActionDecision;
    submitDecision: ActionDecision;
    approveDecision: ActionDecision;
    releaseDecision: ActionDecision;
  };
  busy: boolean;
  onValidate: () => void;
  onSubmit: () => void;
  onApprove: () => void;
  onRelease: () => void;
}) {
  const approvalReason = !approvalNote.trim()
    ? "Enter an approval decision note."
    : decisions.approveDecision.reason;

  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>Governed lifecycle</CardTitle>
        <CardDescription>
          Available commands come only from the authenticated server projection. Missing commands remain disabled.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <ol className="grid gap-2 sm:grid-cols-5" aria-label="Report lifecycle">
          {lifecycleSteps.map((step) => {
            const current = step === run.governanceState;
            return (
              <li
                key={step}
                aria-current={current ? "step" : undefined}
                className={current
                  ? "rounded-md border border-primary bg-primary/10 px-3 py-2 text-sm font-semibold text-primary"
                  : "rounded-md border border-border/70 bg-secondary/15 px-3 py-2 text-sm text-muted-foreground"}
              >
                {step}
              </li>
            );
          })}
        </ol>

        <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.8fr)]">
          <div className="flex flex-wrap items-end gap-2">
            <GovernedActionButton
              label="Validate"
              decision={decisions.validateDecision}
              busy={busy}
              onClick={onValidate}
            />
            <GovernedActionButton
              label="Submit for review"
              decision={decisions.submitDecision}
              busy={busy}
              onClick={onSubmit}
            />
            <GovernedActionButton
              label="Release"
              decision={decisions.releaseDecision}
              busy={busy}
              onClick={onRelease}
            />
          </div>
          <div className="space-y-2">
            <FormRow
              label="Approval decision note"
              labelFor="governed-report-approval-note"
              hint="The server enforces independent maker-checker authority and optimistic version matching."
            >
              <Input
                id="governed-report-approval-note"
                value={approvalNote}
                onChange={(event) => onApprovalNoteChange(event.target.value)}
                maxLength={4000}
                autoComplete="off"
              />
            </FormRow>
            <Button
              type="button"
              size="sm"
              disabled={busy || !decisions.approveDecision.allowed || !approvalNote.trim()}
              disabledReason={busy ? "Another reporting command is running." : approvalReason}
              onClick={onApprove}
            >
              Approve independently
            </Button>
          </div>
        </div>

        <dl className="grid gap-3 md:grid-cols-3">
          <Fact label="Maker" value={run.creationAuthority.actorId} />
          <Fact label="Checker" value={run.approval?.authority.actorId ?? "Not approved"} />
          <Fact
            label="Decision note"
            value={run.approval?.decisionNote
              ? redactReportingCredentialText(run.approval.decisionNote)
              : "No independent approval has been retained."}
          />
        </dl>
      </CardContent>
    </Card>
  );
}

function GovernedActionButton({
  label,
  decision,
  busy,
  onClick
}: {
  label: string;
  decision: ActionDecision;
  busy: boolean;
  onClick: () => void;
}) {
  return (
    <Button
      type="button"
      size="sm"
      variant="outline"
      disabled={busy || !decision.allowed}
      disabledReason={busy ? "Another reporting command is running." : decision.reason}
      onClick={onClick}
    >
      {label}
    </Button>
  );
}

function ParameterPanel({
  run,
  entries
}: {
  run: GovernedReportingRun;
  entries: Array<[string, string]>;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="text-base">Normalized parameters</CardTitle>
        <CardDescription>Immutable values certified by the server for this exact revision.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {entries.length > 0 ? (
          <dl className="grid gap-2 sm:grid-cols-2">
            {entries.map(([label, value]) => (
              <Fact key={label} label={presentKey(label)} value={value} />
            ))}
          </dl>
        ) : (
          <StatusBanner
            role="status"
            tone="warning"
            title="Normalized parameters unavailable"
            detail="The server did not return an immutable normalized parameter projection. Values are not reconstructed from workspace state."
          />
        )}
        <TechnicalDetails label="Parameter certification">
          <dl className="grid gap-2 sm:grid-cols-2">
            <Fact label="Parameters hash" value={run.snapshot.parametersHash ?? "Not projected"} mono />
            <Fact
              label="Canonical JSON"
              value={run.snapshot.parametersCanonicalJson ? "Retained in certified snapshot" : "Not projected"}
            />
          </dl>
        </TechnicalDetails>
      </CardContent>
    </Card>
  );
}

function ScopeAccessPanel({ run }: { run: GovernedReportingRun }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="text-base">Immutable scope &amp; access</CardTitle>
        <CardDescription>Tenant, organization, company, fund, book, period, and policy snapshot.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <dl className="grid gap-2 sm:grid-cols-2">
          <Fact label="Tenant" value={run.scope.tenantId} mono />
          <Fact label="Organization" value={run.scope.organizationId} mono />
          <Fact label="Company" value={run.scope.companyId ?? "No company"} mono />
          <Fact label="Fund" value={run.scope.fundId ?? "No fund"} mono />
          <Fact label="Book" value={run.scope.bookId} mono />
          <Fact label="Period" value={run.scope.periodId} mono />
          <Fact label="Policy" value={`${run.access.policyId} · ${run.access.policyVersion}`} mono />
          <Fact label="Access mode" value={run.access.mode} />
          <Fact label="Owner" value={run.access.ownerPrincipalId ?? "No owner principal"} mono />
          <Fact
            label="Owner access"
            value={run.access.allowOwnerAccess ? "Enabled by retained policy" : "Disabled by retained policy"}
          />
          <Fact label="Policy hash" value={run.access.policyHash} mono />
        </dl>
        <TechnicalDetails label="Retained typed principal scope">
          <ValueList
            items={run.access.principals.map((principal) => `${principal.kind}: ${principal.principalId}`)}
            empty="No explicit principals were retained; server policy still governs access."
            mono
          />
        </TechnicalDetails>
      </CardContent>
    </Card>
  );
}

function SnapshotPanel({ run }: { run: GovernedReportingRun }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="text-base">Certified point-in-time snapshot</CardTitle>
        <CardDescription>Authoritative source and reconciliation checkpoints bound to this run.</CardDescription>
      </CardHeader>
      <CardContent>
        <dl className="grid gap-2 sm:grid-cols-2">
          <Fact label="Snapshot ID" value={run.snapshot.snapshotId} mono />
          <Fact label="Snapshot hash" value={run.snapshot.snapshotHash} mono />
          <Fact label="Captured" value={formatTimestamp(run.snapshot.capturedAtUtc)} />
          <Fact label="Source checkpoint ID" value={run.snapshot.sourceCheckpointId ?? "Not projected"} mono />
          <Fact label="Source checkpoint hash" value={run.snapshot.sourceCheckpointHash ?? "Not projected"} mono />
          <Fact label="Reconciliation checkpoint ID" value={run.snapshot.reconciliationCheckpointId} mono />
          <Fact
            label="Reconciliation checkpoint hash"
            value={run.snapshot.reconciliationCheckpointHash ?? "Not projected"}
            mono
          />
          <Fact label="Parameter hash" value={run.snapshot.parametersHash ?? "Not projected"} mono />
        </dl>
      </CardContent>
    </Card>
  );
}

function ReadinessPanel({ run }: { run: GovernedReportingRun }) {
  const readiness = run.readiness;
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="text-base">Server readiness receipt</CardTitle>
        <CardDescription>Blocking status and retained evidence exactly as evaluated by the server.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {!readiness ? (
          <StatusBanner
            role="status"
            tone="warning"
            title="Readiness not retained"
            detail="Validation remains unavailable until the server returns a signed readiness receipt."
          />
        ) : (
          <>
            <dl className="grid gap-2 sm:grid-cols-2">
              <Fact label="Receipt ID" value={readiness.receiptId} mono />
              <Fact label="Receipt hash" value={readiness.receiptHash} mono />
              <Fact label="Evaluated" value={formatTimestamp(readiness.evaluatedAtUtc)} />
              <Fact label="Ready" value={readiness.isReady ? "Ready" : "Blocked"} />
            </dl>
            <ul className="space-y-2" aria-label="Readiness checks">
              {readiness.checks.map((check) => (
                <li key={check.checkId} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-mono text-xs font-semibold text-foreground">{check.checkId}</span>
                    <Badge variant={check.passed ? "success" : "danger"}>{check.passed ? "Passed" : "Blocked"}</Badge>
                  </div>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {check.failureReason
                      ? redactReportingCredentialText(check.failureReason)
                      : "No failure reason retained."}
                  </p>
                  <ValueList items={check.evidenceIds} empty="No evidence references returned." mono />
                </li>
              ))}
            </ul>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function ArtifactPanel({
  run,
  selectedArtifactIds,
  clientPackageArtifactGate,
  onToggleArtifact
}: {
  run: GovernedReportingRun;
  selectedArtifactIds: string[];
  clientPackageArtifactGate: ClientPackageArtifactGate;
  onToggleArtifact: (artifactId: string) => void;
}) {
  const release = run.release;
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>Immutable release artifacts</CardTitle>
        <CardDescription>
          Operator downloads use run-and-artifact scoped routes; recipient credentials never appear in a query string.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {!release ? (
          <StatusBanner
            role="status"
            tone="warning"
            title="No released artifact manifest"
            detail="Draft, validated, in-review, and approved runs cannot be downloaded or distributed as released output."
          />
        ) : (
          <>
            <dl className="grid gap-2 md:grid-cols-3">
              <Fact label="Manifest ID" value={release.manifestId} mono />
              <Fact label="Manifest hash" value={release.manifestHash} mono />
              <Fact label="Released" value={formatTimestamp(release.releasedAtUtc)} />
            </dl>
            {clientPackageArtifactGate.isClientPackage ? (
              <StatusBanner
                role={clientPackageArtifactGate.isComplete ? "status" : "alert"}
                tone={clientPackageArtifactGate.isComplete ? "info" : "danger"}
                title={clientPackageArtifactGate.isComplete
                  ? "Client package primaries locked"
                  : "Client package release is incomplete"}
                detail={clientPackageArtifactGate.isComplete
                  ? "The released PDF and XLSX primary documents are selected together and cannot be distributed separately."
                  : clientPackageArtifactGate.disabledReason}
              />
            ) : null}
            {release.artifacts.length > 0 ? (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[48rem] text-left text-sm">
                  <caption className="sr-only">Immutable artifacts in the released report package</caption>
                  <thead>
                    <tr className="border-b border-border text-xs text-muted-foreground">
                      <th scope="col" className="px-2 py-2">Include</th>
                      <th scope="col" className="px-2 py-2">Artifact</th>
                      <th scope="col" className="px-2 py-2">Bytes</th>
                      <th scope="col" className="px-2 py-2">SHA-256</th>
                      <th scope="col" className="px-2 py-2">Verified download</th>
                    </tr>
                  </thead>
                  <tbody>
                    {release.artifacts.map((artifact) => {
                      const inputId = `report-artifact-${safeDomId(artifact.artifactId)}`;
                      return (
                        <tr key={artifact.artifactId} className="border-b border-border/60 align-top">
                          <td className="px-2 py-2">
                            <input
                              id={inputId}
                              type="checkbox"
                              checked={selectedArtifactIds.includes(artifact.artifactId)}
                              disabled={clientPackageArtifactGate.requiredArtifactIds.includes(artifact.artifactId)}
                              onChange={() => onToggleArtifact(artifact.artifactId)}
                              aria-label={`Include ${artifact.artifactId} in distribution`}
                            />
                          </td>
                          <td className="break-all px-2 py-2 font-mono text-xs">{artifact.artifactId}</td>
                          <td className="px-2 py-2 font-mono text-xs">{formatBytes(artifact.byteLength)}</td>
                          <td className="break-all px-2 py-2 font-mono text-xs">{artifact.artifactHash}</td>
                          <td className="px-2 py-2">
                            <a
                              className="font-semibold text-primary underline underline-offset-2"
                              href={secureReportingArtifactDownloadPath(run.runId, artifact.artifactId)}
                            >
                              Download exact bytes
                            </a>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            ) : (
              <StatusBanner
                role="alert"
                tone="danger"
                title="Released manifest has no artifacts"
                detail="Downloads and distribution remain unavailable because no immutable artifact identities were returned."
              />
            )}
            <TechnicalDetails label="Release evidence references">
              <ValueList items={release.evidenceIds} empty="No release evidence references returned." mono />
            </TechnicalDetails>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function AuditPanel({ run }: { run: GovernedReportingRun }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>Immutable audit trail</CardTitle>
        <CardDescription>Append-only version, actor, permission, transition, note, and hash evidence.</CardDescription>
      </CardHeader>
      <CardContent>
        {run.auditTrail.length === 0 ? (
          <StatusBanner
            role="alert"
            tone="warning"
            title="Audit trail unavailable"
            detail="Lifecycle actions remain reviewable only when the server returns retained audit events."
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[64rem] text-left text-sm">
              <caption className="sr-only">Retained governed reporting audit events</caption>
              <thead>
                <tr className="border-b border-border text-xs text-muted-foreground">
                  <th scope="col" className="px-2 py-2">Version</th>
                  <th scope="col" className="px-2 py-2">When</th>
                  <th scope="col" className="px-2 py-2">Action</th>
                  <th scope="col" className="px-2 py-2">Actor</th>
                  <th scope="col" className="px-2 py-2">Transition</th>
                  <th scope="col" className="px-2 py-2">Note</th>
                  <th scope="col" className="px-2 py-2">Hash</th>
                </tr>
              </thead>
              <tbody>
                {run.auditTrail.map((entry) => (
                  <tr key={entry.eventId} className="border-b border-border/60 align-top">
                    <td className="px-2 py-2 font-mono text-xs">{entry.aggregateVersion}</td>
                    <td className="px-2 py-2">{formatTimestamp(entry.occurredAtUtc)}</td>
                    <td className="px-2 py-2">
                      <div>{entry.action}</div>
                      <div className="font-mono text-xs text-muted-foreground">{entry.permissionUsed}</div>
                    </td>
                    <td className="px-2 py-2">{entry.authority.actorId}</td>
                    <td className="px-2 py-2">
                      {presentTransition(entry.fromGovernanceState, entry.toGovernanceState)}
                    </td>
                    <td className="max-w-xs px-2 py-2 text-muted-foreground">
                      {entry.note ? redactReportingCredentialText(entry.note) : "No note"}
                    </td>
                    <td className="max-w-xs break-all px-2 py-2 font-mono text-xs">{entry.hash}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function RestatementPanel({
  run,
  series,
  reason,
  onReasonChange,
  requestDecision,
  busy,
  onRequest,
  onApprove
}: {
  run: GovernedReportingRun;
  series: ResourceState<ReportingGovernanceSeriesHistory | null>;
  reason: string;
  onReasonChange: (value: string) => void;
  requestDecision: ActionDecision;
  busy: boolean;
  onRequest: () => void;
  onApprove: (request: ReportingGovernanceRestatement) => void;
}) {
  const history = series.data;
  const requestDisabledReason = !reason.trim()
    ? "Enter the business reason for a governed restatement."
    : requestDecision.reason;

  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>Revision &amp; restatement history</CardTitle>
        <CardDescription>
          Restatements create a separately certified draft revision after independent approval; released bytes are never overwritten.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-end">
          <FormRow
            label="Restatement reason"
            labelFor="report-restatement-reason"
            hint="Changed-line evidence and the replacement snapshot are computed from server-owned records."
          >
            <Input
              id="report-restatement-reason"
              value={reason}
              onChange={(event) => onReasonChange(event.target.value)}
              maxLength={4000}
              autoComplete="off"
            />
          </FormRow>
          <Button
            type="button"
            size="sm"
            disabled={busy || !requestDecision.allowed || !reason.trim()}
            disabledReason={busy ? "Another reporting command is running." : requestDisabledReason}
            onClick={onRequest}
          >
            Request governed restatement
          </Button>
        </div>

        {series.phase === "loading" ? (
          <StatusBanner role="status" tone="info" title="Loading revision history" />
        ) : series.phase === "unavailable" || !history ? (
          <StatusBanner
            role="status"
            tone="warning"
            title="Revision discovery unavailable"
            detail={series.detail ?? "The server did not return tenant-filtered revision history."}
          />
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[42rem] text-left text-sm">
                <caption className="sr-only">Governed revisions in this report series</caption>
                <thead>
                  <tr className="border-b border-border text-xs text-muted-foreground">
                    <th scope="col" className="px-2 py-2">Revision</th>
                    <th scope="col" className="px-2 py-2">Run</th>
                    <th scope="col" className="px-2 py-2">Execution</th>
                    <th scope="col" className="px-2 py-2">Governance</th>
                    <th scope="col" className="px-2 py-2">Snapshot</th>
                  </tr>
                </thead>
                <tbody>
                  {history.runs.map((revision) => (
                    <tr key={revision.runId} className="border-b border-border/60">
                      <td className="px-2 py-2 font-mono text-xs">{revision.revision}</td>
                      <td className="px-2 py-2">
                        {revision.runId === run.runId ? (
                          <span className="font-mono text-xs">{revision.runId} (current)</span>
                        ) : (
                          <Link
                            className="break-all font-mono text-xs font-semibold text-primary underline underline-offset-2"
                            to={`/reporting/runs/detail?runId=${encodeURIComponent(revision.runId)}`}
                          >
                            {revision.runId}
                          </Link>
                        )}
                      </td>
                      <td className="px-2 py-2">{revision.executionState}</td>
                      <td className="px-2 py-2">{revision.governanceState}</td>
                      <td className="max-w-xs break-all px-2 py-2 font-mono text-xs">{revision.snapshot.snapshotHash}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {history.restatementRequests.length > 0 ? (
              <ul className="space-y-3" aria-label="Restatement requests">
                {history.restatementRequests.map((request) => {
                  const approvalDecision = resolveServerAction(
                    request,
                    ["ApproveRestatement", "ApproveRestatementRequest"]
                  );
                  return (
                    <li key={request.requestId} className="rounded-md border border-border/70 bg-secondary/15 p-3">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <div className="font-semibold text-foreground">
                            {redactReportingCredentialText(request.reason)}
                          </div>
                          <div className="mt-1 font-mono text-xs text-muted-foreground">
                            Request {request.requestId} · predecessor revision {request.predecessorRevision} · version {request.version}
                          </div>
                        </div>
                        <Badge variant={restatementStateVariant(request.state)}>{request.state}</Badge>
                      </div>
                      <dl className="mt-3 grid gap-2 md:grid-cols-3">
                        <Fact label="Requested by" value={request.requestedBy.actorId} />
                        <Fact label="Requested" value={formatTimestamp(request.requestedAtUtc)} />
                        <Fact label="Draft run" value={request.draftRunId ?? "Not created"} mono />
                      </dl>
                      {request.changedLines.length > 0 ? (
                        <TechnicalDetails label={`${request.changedLines.length} certified changed lines`} className="mt-3">
                          <ul className="space-y-2">
                            {request.changedLines.map((line) => (
                              <li key={line.lineKey} className="rounded border border-border/60 px-2 py-2 text-sm">
                                <div className="font-mono text-xs font-semibold">{line.lineKey}</div>
                                <div className="mt-1 grid gap-1 sm:grid-cols-2">
                                  <span>Previous: {redactReportingCredentialText(line.previousValue)}</span>
                                  <span>Current: {redactReportingCredentialText(line.currentValue)}</span>
                                </div>
                                <ValueList items={line.evidenceIds} empty="No changed-line evidence returned." mono />
                              </li>
                            ))}
                          </ul>
                        </TechnicalDetails>
                      ) : (
                        <p className="mt-3 text-sm text-warning">No certified changed-line evidence was returned.</p>
                      )}
                      <div className="mt-3">
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          disabled={busy || !approvalDecision.allowed}
                          disabledReason={busy ? "Another reporting command is running." : approvalDecision.reason}
                          onClick={() => onApprove(request)}
                        >
                          Approve restatement independently
                        </Button>
                      </div>
                    </li>
                  );
                })}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">No restatement requests are retained for this series.</p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

function DistributionPanel({
  run,
  transports,
  deliveries,
  grants,
  selectedArtifactIds,
  clientPackageArtifactGate,
  transportId,
  onTransportIdChange,
  distributionId,
  onDistributionIdChange,
  recipientPrincipalId,
  onRecipientPrincipalIdChange,
  recipientPrincipalKind,
  onRecipientPrincipalKindChange,
  destination,
  onDestinationChange,
  subject,
  onSubjectChange,
  body,
  onBodyChange,
  maxAttempts,
  onMaxAttemptsChange,
  grantLifetimeSeconds,
  onGrantLifetimeSecondsChange,
  grantMaxUses,
  onGrantMaxUsesChange,
  revocationReason,
  onRevocationReasonChange,
  busy,
  onQueueDelivery,
  onIssueGrant,
  onRevokeGrant
}: {
  run: GovernedReportingRun;
  transports: ResourceState<SecureReportingDistributionCapabilityCatalog>;
  deliveries: ResourceState<SecureReportingDelivery[]>;
  grants: ResourceState<SecureReportingAccessGrant[]>;
  selectedArtifactIds: string[];
  clientPackageArtifactGate: ClientPackageArtifactGate;
  transportId: string;
  onTransportIdChange: (value: string) => void;
  distributionId: string;
  onDistributionIdChange: (value: string) => void;
  recipientPrincipalId: string;
  onRecipientPrincipalIdChange: (value: string) => void;
  recipientPrincipalKind: "User" | "Group" | "Company";
  onRecipientPrincipalKindChange: (value: "User" | "Group" | "Company") => void;
  destination: string;
  onDestinationChange: (value: string) => void;
  subject: string;
  onSubjectChange: (value: string) => void;
  body: string;
  onBodyChange: (value: string) => void;
  maxAttempts: string;
  onMaxAttemptsChange: (value: string) => void;
  grantLifetimeSeconds: string;
  onGrantLifetimeSecondsChange: (value: string) => void;
  grantMaxUses: string;
  onGrantMaxUsesChange: (value: string) => void;
  revocationReason: string;
  onRevocationReasonChange: (value: string) => void;
  busy: boolean;
  onQueueDelivery: () => void;
  onIssueGrant: () => void;
  onRevokeGrant: (grantId: string) => void;
}) {
  const selectedTransport = transports.data.transports.find((transport) => transport.transportId === transportId) ?? null;
  const released = run.governanceState === "Released";
  const queueReason = !released
    ? "Only a Released run can be distributed."
    : transports.phase !== "ready"
      ? transports.detail ?? "The server transport catalog is unavailable."
      : !transports.data.canQueueDelivery
        ? transports.data.actionDisabledReasonCode ?? "The server did not authorize delivery queueing for this caller."
        : !selectedTransport?.isReady
          ? selectedTransport?.disabledReasonCode ?? "Select a ready server-configured transport."
          : !clientPackageArtifactGate.isComplete
            ? clientPackageArtifactGate.disabledReason ?? "The released client package is incomplete."
            : selectedArtifactIds.length === 0
              ? "Select at least one immutable artifact."
              : !distributionId.trim()
                ? "Enter a durable distribution identity."
                : !subject.trim() || !body.trim()
                  ? "Enter the notification subject and body."
                  : selectedTransport.requiresDestination && !destination.trim()
                    ? "This transport requires a destination."
                    : "The server will revalidate release, scope, audience, artifacts, and transport configuration.";
  const canQueue = released
    && transports.phase === "ready"
    && transports.data.canQueueDelivery
    && selectedTransport?.isReady === true
    && clientPackageArtifactGate.isComplete
    && selectedArtifactIds.length > 0
    && Boolean(distributionId.trim() && subject.trim() && body.trim())
    && (!selectedTransport.requiresDestination || Boolean(destination.trim()));
  const canIssueGrant = released
    && transports.phase === "ready"
    && transports.data.canIssueAccessGrant
    && clientPackageArtifactGate.isComplete
    && selectedArtifactIds.length > 0;
  const issueGrantReason = !released
    ? "Only a Released run can issue recipient access."
    : transports.phase !== "ready"
      ? transports.detail ?? "The server distribution capability catalog is unavailable."
      : !transports.data.canIssueAccessGrant
        ? transports.data.actionDisabledReasonCode ?? "The server did not authorize access-grant issuance for this caller."
        : !clientPackageArtifactGate.isComplete
          ? clientPackageArtifactGate.disabledReason ?? "The released client package is incomplete."
          : "Select at least one immutable artifact.";

  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>Secure distribution &amp; recipient access</CardTitle>
        <CardDescription>
          Release-gated queueing, provider receipts, expiring grants, revocation, and exact-byte downloads.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        {!released ? (
          <StatusBanner
            role="status"
            tone="warning"
            title="Distribution blocked until Released"
            detail="Approval alone does not authorize delivery or recipient access."
          />
        ) : null}

        <section aria-labelledby="transport-catalog-title" className="space-y-3">
          <h3 id="transport-catalog-title" className="text-sm font-semibold text-foreground">Authenticated transport catalog</h3>
          {transports.phase === "loading" ? (
            <StatusBanner role="status" tone="info" title="Loading transport readiness" />
          ) : transports.phase === "unavailable" ? (
            <StatusBanner role="status" tone="warning" title="Transport catalog unavailable" detail={transports.detail} />
          ) : transports.data.transports.length === 0 ? (
            <StatusBanner
              role="status"
              tone="warning"
              title="No secure transports configured"
              detail="Distribution remains disabled until a credential-free server capability is returned as ready."
            />
          ) : (
            <ul className="grid gap-2 md:grid-cols-2 xl:grid-cols-3" aria-label="Secure reporting transports">
              {transports.data.transports.map((transport) => (
                <li key={transport.transportId} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-semibold text-foreground">{transport.displayName}</span>
                    <Badge variant={transport.isReady ? "success" : "warning"}>
                      {transport.isReady ? "Ready" : "Unavailable"}
                    </Badge>
                  </div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">
                    {transport.transportId} · {transport.deliveryMode}
                  </div>
                  <div className="mt-1 text-xs text-muted-foreground">
                    {transport.supportsProviderReceipts ? "Provider receipts" : "Server delivery receipts"}
                    {transport.issuesAccessGrant ? " · Scoped grant" : ""}
                    {!transport.isInfrastructureReady && transport.infrastructureDisabledReasonCode
                      ? ` · infrastructure: ${transport.infrastructureDisabledReasonCode}`
                      : ""}
                    {transport.disabledReasonCode ? ` · ${transport.disabledReasonCode}` : ""}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section aria-labelledby="queue-delivery-title" className="space-y-3">
          <h3 id="queue-delivery-title" className="text-sm font-semibold text-foreground">Queue a verified delivery</h3>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            <FormRow label="Transport" labelFor="secure-report-transport">
              <Select
                id="secure-report-transport"
                value={transportId}
                onChange={(event) => onTransportIdChange(event.target.value)}
              >
                <option value="">Select server transport</option>
                {transports.data.transports.map((transport) => (
                  <option key={transport.transportId} value={transport.transportId} disabled={!transport.isReady}>
                    {transport.displayName}{transport.isReady ? "" : ` — ${transport.disabledReasonCode ?? "unavailable"}`}
                  </option>
                ))}
              </Select>
            </FormRow>
            <FormRow label="Distribution ID" labelFor="secure-report-distribution-id" hint="Stable idempotency identity supplied by the operator workflow.">
              <Input
                id="secure-report-distribution-id"
                value={distributionId}
                onChange={(event) => onDistributionIdChange(event.target.value)}
                maxLength={256}
                autoComplete="off"
              />
            </FormRow>
            <FormRow label="Recipient principal" labelFor="secure-report-recipient-principal" hint="Optional; the server resolves the governed audience.">
              <Input
                id="secure-report-recipient-principal"
                value={recipientPrincipalId}
                onChange={(event) => onRecipientPrincipalIdChange(event.target.value)}
                maxLength={256}
                autoComplete="off"
              />
            </FormRow>
            <FormRow label="Recipient kind" labelFor="secure-report-recipient-kind" hint="Select the immutable user, group, or company namespace for an explicit recipient.">
              <Select
                id="secure-report-recipient-kind"
                value={recipientPrincipalKind}
                onChange={(event) => onRecipientPrincipalKindChange(
                  event.target.value as "User" | "Group" | "Company")}
                disabled={!recipientPrincipalId.trim()}
              >
                <option value="User">User</option>
                <option value="Group">Group</option>
                <option value="Company">Company</option>
              </Select>
            </FormRow>
            <FormRow
              label="Destination"
              labelFor="secure-report-destination"
              hint={selectedTransport?.requiresDestination
                ? "Required by the selected transport."
                : selectedTransport?.isExternal
                  ? "Optional equality assertion; the server resolves the governed recipient destination."
                  : "Optional for governed portal delivery."}
            >
              <Input
                id="secure-report-destination"
                value={destination}
                onChange={(event) => onDestinationChange(event.target.value)}
                maxLength={2000}
                autoComplete="off"
              />
            </FormRow>
            <FormRow label="Subject" labelFor="secure-report-subject">
              <Input
                id="secure-report-subject"
                value={subject}
                onChange={(event) => onSubjectChange(event.target.value)}
                maxLength={1024}
                autoComplete="off"
              />
            </FormRow>
            <FormRow label="Body" labelFor="secure-report-body">
              <Input
                id="secure-report-body"
                value={body}
                onChange={(event) => onBodyChange(event.target.value)}
                maxLength={4000}
                autoComplete="off"
              />
            </FormRow>
            <FormRow label="Maximum attempts" labelFor="secure-report-max-attempts">
              <Input
                id="secure-report-max-attempts"
                type="number"
                min={1}
                value={maxAttempts}
                onChange={(event) => onMaxAttemptsChange(event.target.value)}
              />
            </FormRow>
            <FormRow label="Grant lifetime seconds" labelFor="secure-report-grant-lifetime" hint="Optional; server defaults and maximums apply.">
              <Input
                id="secure-report-grant-lifetime"
                type="number"
                min={1}
                value={grantLifetimeSeconds}
                onChange={(event) => onGrantLifetimeSecondsChange(event.target.value)}
              />
            </FormRow>
            <FormRow label="Grant maximum uses" labelFor="secure-report-grant-max-uses" hint="Optional; server defaults and maximums apply.">
              <Input
                id="secure-report-grant-max-uses"
                type="number"
                min={1}
                value={grantMaxUses}
                onChange={(event) => onGrantMaxUsesChange(event.target.value)}
              />
            </FormRow>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              size="sm"
              disabled={busy || !canQueue}
              disabledReason={busy ? "Another reporting command is running." : queueReason}
              onClick={onQueueDelivery}
            >
              Queue secure delivery
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={busy || !canIssueGrant}
              disabledReason={busy ? "Another reporting command is running." : issueGrantReason}
              onClick={onIssueGrant}
            >
              Issue scoped recipient access
            </Button>
          </div>
        </section>

        <DeliveryHistory resource={deliveries} />

        <section aria-labelledby="grant-history-title" className="space-y-3">
          <h3 id="grant-history-title" className="text-sm font-semibold text-foreground">Durable access-grant history</h3>
          <FormRow label="Revocation reason" labelFor="secure-report-revocation-reason" hint="Required before revoking any active grant.">
            <Input
              id="secure-report-revocation-reason"
              value={revocationReason}
              onChange={(event) => onRevocationReasonChange(event.target.value)}
              maxLength={4000}
              autoComplete="off"
            />
          </FormRow>
          {grants.phase === "loading" ? (
            <StatusBanner role="status" tone="info" title="Loading access grants" />
          ) : grants.phase === "unavailable" ? (
            <StatusBanner role="status" tone="warning" title="Access grants unavailable" detail={grants.detail} />
          ) : grants.data.length === 0 ? (
            <p className="text-sm text-muted-foreground">No access grants are retained for this run.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[64rem] text-left text-sm">
                <caption className="sr-only">Durable recipient access grants</caption>
                <thead>
                  <tr className="border-b border-border text-xs text-muted-foreground">
                    <th scope="col" className="px-2 py-2">Grant</th>
                    <th scope="col" className="px-2 py-2">Audience</th>
                    <th scope="col" className="px-2 py-2">State</th>
                    <th scope="col" className="px-2 py-2">Expiry</th>
                    <th scope="col" className="px-2 py-2">Uses</th>
                    <th scope="col" className="px-2 py-2">Artifacts</th>
                    <th scope="col" className="px-2 py-2">Control</th>
                  </tr>
                </thead>
                <tbody>
                  {grants.data.map((grant) => {
                    const revoked = Boolean(grant.revokedAtUtc) || normalizeToken(grant.state) === "revoked";
                    const canRevoke = released
                      && transports.phase === "ready"
                      && transports.data.canRevokeAccessGrant
                      && !revoked
                      && Boolean(revocationReason.trim());
                    const revokeReason = revoked
                      ? "This grant is already revoked."
                      : !released
                        ? "The owning run is not Released."
                        : transports.phase !== "ready"
                          ? transports.detail ?? "The server distribution capability catalog is unavailable."
                          : !transports.data.canRevokeAccessGrant
                            ? transports.data.actionDisabledReasonCode ?? "The server did not authorize access-grant revocation for this caller."
                            : !revocationReason.trim()
                              ? "Enter a revocation reason."
                              : "Another reporting command is running.";
                    return (
                      <tr key={grant.grantId} className="border-b border-border/60 align-top">
                        <td className="break-all px-2 py-2 font-mono text-xs">{grant.grantId}</td>
                        <td className="break-all px-2 py-2">{grant.audience}</td>
                        <td className="px-2 py-2">
                          <Badge variant={revoked ? "danger" : "outline"}>{grant.state}</Badge>
                          {grant.revocationReason ? (
                            <div className="mt-1 text-xs text-muted-foreground">
                              {redactReportingCredentialText(grant.revocationReason)}
                            </div>
                          ) : null}
                        </td>
                        <td className="px-2 py-2">{formatTimestamp(grant.expiresAtUtc)}</td>
                        <td className="px-2 py-2 font-mono text-xs">{grant.useCount}/{grant.maxUses}</td>
                        <td className="px-2 py-2 font-mono text-xs">
                          {grant.artifactIds.length} · {grant.allowPackageRead ? "package read" : "artifact only"}
                        </td>
                        <td className="px-2 py-2">
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            disabled={busy || !canRevoke}
                            disabledReason={busy ? "Another reporting command is running." : revokeReason}
                            onClick={() => onRevokeGrant(grant.grantId)}
                          >
                            Revoke
                          </Button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </CardContent>
    </Card>
  );
}

function DeliveryHistory({ resource }: { resource: ResourceState<SecureReportingDelivery[]> }) {
  return (
    <section aria-labelledby="delivery-history-title" className="space-y-3">
      <h3 id="delivery-history-title" className="text-sm font-semibold text-foreground">Delivery history &amp; provider receipts</h3>
      {resource.phase === "loading" ? (
        <StatusBanner role="status" tone="info" title="Loading delivery history" />
      ) : resource.phase === "unavailable" ? (
        <StatusBanner role="status" tone="warning" title="Delivery history unavailable" detail={resource.detail} />
      ) : resource.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">No durable delivery jobs are retained for this run.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[68rem] text-left text-sm">
            <caption className="sr-only">Durable report deliveries and provider receipts</caption>
            <thead>
              <tr className="border-b border-border text-xs text-muted-foreground">
                <th scope="col" className="px-2 py-2">Job</th>
                <th scope="col" className="px-2 py-2">Transport</th>
                <th scope="col" className="px-2 py-2">Recipient</th>
                <th scope="col" className="px-2 py-2">State</th>
                <th scope="col" className="px-2 py-2">Attempts</th>
                <th scope="col" className="px-2 py-2">Provider reference</th>
                <th scope="col" className="px-2 py-2">Receipts</th>
              </tr>
            </thead>
            <tbody>
              {resource.data.map((delivery) => (
                <tr key={delivery.jobId} className="border-b border-border/60 align-top">
                  <td className="break-all px-2 py-2 font-mono text-xs">
                    <div>{delivery.jobId}</div>
                    <div className="mt-1 text-muted-foreground">release v{delivery.releaseVersion}</div>
                    <div className="mt-1">{delivery.artifactManifestHashSha256}</div>
                  </td>
                  <td className="px-2 py-2">{delivery.transportId}</td>
                  <td className="px-2 py-2">
                    <div>
                      <span className="text-xs text-muted-foreground">{delivery.recipientKind ?? "User"}</span>
                      {" · "}{redactReportingCredentialText(delivery.recipient)}
                    </div>
                    <div className="mt-1 break-all text-xs text-muted-foreground">
                      {redactReportingCredentialText(delivery.destination)}
                    </div>
                  </td>
                  <td className="px-2 py-2">
                    <Badge variant={deliveryStateVariant(delivery.state)}>{delivery.state}</Badge>
                    {delivery.lastErrorCode ? <div className="mt-1 text-xs text-danger">{delivery.lastErrorCode}</div> : null}
                    {delivery.lastError ? (
                      <div className="mt-1 text-xs text-danger">
                        {redactReportingCredentialText(delivery.lastError)}
                      </div>
                    ) : null}
                  </td>
                  <td className="px-2 py-2 font-mono text-xs">{delivery.attemptCount}/{delivery.maxAttempts}</td>
                  <td className="break-all px-2 py-2 font-mono text-xs">
                    {delivery.providerMessageId
                      ? redactReportingCredentialText(delivery.providerMessageId)
                      : "Pending"}
                  </td>
                  <td className="px-2 py-2">
                    {delivery.receipts.length > 0 ? (
                      <ul className="space-y-1" aria-label={`Receipts for ${delivery.jobId}`}>
                        {delivery.receipts.map((receipt) => (
                          <li key={receipt.receiptId} className="rounded border border-border/60 px-2 py-1 text-xs">
                            <span className="font-semibold">{receipt.kind}</span> · {formatTimestamp(receipt.occurredAtUtc)}
                            {receipt.providerReference ? (
                              <span className="break-all font-mono">
                                {" · "}{redactReportingCredentialText(receipt.providerReference)}
                              </span>
                            ) : null}
                            {receipt.detail ? (
                              <div className="mt-1 text-muted-foreground">
                                {redactReportingCredentialText(receipt.detail)}
                              </div>
                            ) : null}
                          </li>
                        ))}
                      </ul>
                    ) : "No receipts"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function Fact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className={mono ? "mt-1 break-all font-mono text-xs text-foreground" : "mt-1 break-words text-sm text-foreground"}>
        {redactReportingCredentialText(value)}
      </dd>
    </div>
  );
}

function ValueList({ items, empty, mono = false }: { items: string[]; empty: string; mono?: boolean }) {
  if (items.length === 0) {
    return <p className="mt-2 text-xs text-muted-foreground">{empty}</p>;
  }

  return (
    <ul className="mt-2 space-y-1">
      {items.map((item) => (
        <li
          key={item}
          className={mono
            ? "break-all rounded border border-border/60 px-2 py-1 font-mono text-xs text-muted-foreground"
            : "rounded border border-border/60 px-2 py-1 text-xs text-muted-foreground"}
        >
          {redactReportingCredentialText(item)}
        </li>
      ))}
    </ul>
  );
}

function projectOptionalResult<T>(
  result: PromiseSettledResult<T>,
  fallback: T,
  unavailableDetail: string
): ResourceState<T> {
  if (result.status === "fulfilled") {
    if (result.value === null || result.value === undefined) {
      return { phase: "unavailable", data: fallback, detail: unavailableDetail };
    }
    return { phase: "ready", data: result.value, detail: null };
  }

  return {
    phase: "unavailable",
    data: fallback,
    detail: describeFailure(result.reason, unavailableDetail)
  };
}

function projectDistributionCapabilityResult(
  result: PromiseSettledResult<SecureReportingDistributionCapabilityCatalog>
): ResourceState<SecureReportingDistributionCapabilityCatalog> {
  const unavailableDetail = "The authenticated transport capability catalog is unavailable or malformed. Distribution controls remain disabled.";
  if (result.status === "rejected") {
    return {
      phase: "unavailable",
      data: unavailableDistributionCapabilities,
      detail: describeFailure(result.reason, unavailableDetail)
    };
  }

  const value = result.value;
  if (
    !value
    || typeof value.canQueueDelivery !== "boolean"
    || typeof value.canIssueAccessGrant !== "boolean"
    || typeof value.canRevokeAccessGrant !== "boolean"
    || !Array.isArray(value.transports)
  ) {
    return {
      phase: "unavailable",
      data: unavailableDistributionCapabilities,
      detail: unavailableDetail
    };
  }

  return { phase: "ready", data: value, detail: null };
}

export function resolveServerAction(
  subject: GovernedActionSubject | null | undefined,
  aliases: string[]
): ActionDecision {
  const normalizedAliases = new Set(aliases.map(normalizeToken));
  if (!subject) {
    return { allowed: false, reason: "No authoritative run state is loaded." };
  }

  for (const candidate of subject.actionAvailability) {
    if (!normalizedAliases.has(normalizeToken(candidate.action))) continue;

    if (candidate.expectedVersion !== subject.version) {
      return {
        allowed: false,
        reason: "The server action projection targets a different retained version. Refresh before continuing."
      };
    }

    return {
      allowed: candidate.isAllowed,
      reason: candidate.isAllowed
        ? "Authorized by the current server projection."
        : candidate.blockedReason?.trim()
          ? redactReportingCredentialText(candidate.blockedReason.trim())
          : "The server did not authorize this command."
    };
  }

  return {
    allowed: false,
    reason: "This command is absent from the server-owned allowed-action projection."
  };
}

function projectParameterEntries(run: GovernedReportingRun | null): Array<[string, string]> {
  if (!run) return [];
  return flattenParameterObject(run.normalizedParameters as unknown as Record<string, unknown>);
}

function flattenParameterObject(
  record: Record<string, unknown>,
  prefix = ""
): Array<[string, string]> {
  const entries: Array<[string, string]> = [];
  for (const [key, value] of Object.entries(record)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value && typeof value === "object" && !Array.isArray(value)) {
      entries.push(...flattenParameterObject(value as Record<string, unknown>, path));
      continue;
    }
    const presented = presentValue(value);
    if (presented !== null) {
      entries.push([path, presented]);
    }
  }
  return entries;
}

function presentValue(value: unknown): string | null {
  if (typeof value === "string") {
    const retained = value.trim();
    return retained ? redactReportingCredentialText(retained) : null;
  }
  if (typeof value === "number" && Number.isFinite(value)) return String(value);
  if (typeof value === "boolean") return value ? "Yes" : "No";
  if (value === null) return "None";
  if (Array.isArray(value)) {
    const values = value.map(presentValue).filter((item): item is string => item !== null);
    return values.length > 0 ? values.join(", ") : "None";
  }
  return null;
}

/**
 * Recipient links may carry the one-time bearer only in the URL fragment. Query strings are
 * suppressed wholesale so legacy ?token= links can never become an operator-visible anchor.
 */
export function safeRecipientAccessUri(value: string | null | undefined): string | null {
  return safeReportingHref(value, { requireOpaqueFragment: true });
}

function describeFailure(error: unknown, fallback: string): string {
  const description = describeApiError(error, fallback);
  return redactReportingCredentialText([description.summary, ...description.details]
    .map((part) => part.trim())
    .filter(Boolean)
    .filter((part, index, all) => all.indexOf(part) === index)
    .join(" "));
}

function isConflict(error: unknown): boolean {
  return Boolean(error && typeof error === "object" && "status" in error && error.status === 409);
}

function parseOptionalPositiveInteger(value: string): number | null {
  if (!value.trim()) return null;
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
}

function presentKey(value: string): string {
  const spaced = value
    .replace(/\./g, " · ")
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\s+/g, " ")
    .trim();
  return spaced ? `${spaced.charAt(0).toUpperCase()}${spaced.slice(1)}` : "Parameter";
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString("en-US", { dateStyle: "medium", timeStyle: "short" });
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value < 0) return "Unavailable";
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function presentTransition(from: string | null, to: string | null): string {
  if (from && to) return `${from} → ${to}`;
  if (to) return `Created as ${to}`;
  return "No governance transition";
}

function safeDomId(value: string): string {
  return value.replace(/[^a-zA-Z0-9_-]+/g, "-").slice(0, 80) || "artifact";
}

function normalizeToken(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, "");
}

function governanceStateVariant(state: string): "outline" | "warning" | "success" {
  const normalized = normalizeToken(state);
  if (normalized === "released" || normalized === "approved") return "success";
  if (normalized === "inreview") return "warning";
  return "outline";
}

function executionStateVariant(state: string): "outline" | "warning" | "danger" | "success" {
  const normalized = normalizeToken(state);
  if (normalized === "failed") return "danger";
  if (normalized === "succeeded") return "success";
  if (normalized === "running" || normalized === "queued") return "warning";
  return "outline";
}

function restatementStateVariant(state: string): "outline" | "warning" | "danger" | "success" {
  const normalized = normalizeToken(state);
  if (normalized.includes("reject")) return "danger";
  if (normalized.includes("approve")) return "success";
  if (normalized.includes("pending") || normalized.includes("request")) return "warning";
  return "outline";
}

function deliveryStateVariant(state: string): "outline" | "warning" | "danger" | "success" {
  const normalized = normalizeToken(state);
  if (normalized === "failed" || normalized === "deadlettered") return "danger";
  if (normalized === "delivered" || normalized === "sent") return "success";
  if (normalized === "queued" || normalized === "leased" || normalized === "retrying") return "warning";
  return "outline";
}

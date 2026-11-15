import { useEffect, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import { downloadProfileCandidate, getProfilePromotionAssessments, type ProfilePromotionAssessment, type ProfilePromotionAssessments } from "@/lib/api/security-asset-profile-promotion.api";

/** Demand evidence stays separate from definition complexity and runtime capabilities. */
export function SettingsAssetProfilePromotion() {
  const [result, setResult] = useState<ProfilePromotionAssessments | null>(null);
  const [revision, setRevision] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getProfilePromotionAssessments(controller.signal).then(value => {
      if (!controller.signal.aborted) setResult(value);
    }).catch(reason => {
      if (!controller.signal.aborted) setError(describeApiError(reason, "Profile usage is unavailable.").summary);
    });
    return () => controller.abort();
  }, [revision]);

  const refresh = () => {
    setResult(null);
    setError(null);
    setNotice(null);
    setRevision(value => value + 1);
  };
  const exportCandidate = async (assessment: ProfilePromotionAssessment) => {
    setExporting(`${assessment.profileId}:${assessment.version}`);
    setNotice(null);
    setError(null);
    try {
      await downloadProfileCandidate(assessment);
      setNotice(`Downloaded ${assessment.name} v${assessment.version} for developer review. No class was registered.`);
    } catch (reason) {
      setError(describeApiError(reason, "Candidate export failed. Refresh the assessment and retry.").summary);
    } finally {
      setExporting(null);
    }
  };
  const columns: DenseDataTableColumn<ProfilePromotionAssessment>[] = [
    { id: "profile", label: "Approved profile", rowHeader: true, render: assessment => <>
      <span className="font-medium">{assessment.name} v{assessment.version}</span>
      <span className="block text-xs font-normal text-muted-foreground">{assessment.profileId}</span>
    </> },
    { id: "active", label: "Active records", numeric: true, render: assessment => assessment.usage.activeRecordCount },
    { id: "inactive", label: "Inactive", numeric: true, render: assessment => assessment.usage.inactiveRecordCount },
    { id: "unknown", label: "Unknown status", numeric: true, render: assessment => assessment.usage.unknownStatusRecordCount },
    { id: "presence", label: "Required-field presence · active", render: assessment => <div className="text-xs text-muted-foreground">
      <p>Complete {assessment.usage.completeActiveRecordCount ?? "unavailable"} · Incomplete {assessment.usage.incompleteActiveRecordCount ?? "unavailable"} · Unknown {assessment.usage.unknownActiveRecordCount ?? assessment.usage.activeRecordCount}</p>
      {assessment.usage.unresolvedReason ? <p className="mt-1">{assessment.usage.unresolvedReason}</p> : null}
      {assessment.usage.missingRequiredFieldCounts ? <details className="mt-1"><summary className="cursor-pointer">Missing fields · assessed active records</summary>
        {Object.keys(assessment.usage.missingRequiredFieldCounts).length === 0 ? <p>No required fields are declared.</p> : <ul>{Object.entries(assessment.usage.missingRequiredFieldCounts).map(([field, count]) => <li key={field}>{field}: {count}</li>)}</ul>}
      </details> : <p className="mt-1">Field presence evidence unavailable.</p>}
    </div> },
    { id: "export", label: "Developer review", render: assessment => <Button variant="outline" size="sm" disabled={!result?.canExport || exporting !== null}
      aria-label={`Download developer draft for ${assessment.name} v${assessment.version}`}
      onClick={() => void exportCandidate(assessment)}>{exporting === `${assessment.profileId}:${assessment.version}` ? "Preparing…" : "Download developer draft"}</Button> }
  ];

  return <section className="grid gap-3 rounded-md border border-border/70 p-4" aria-labelledby="asset-profile-promotion-title">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h3 id="asset-profile-promotion-title" className="text-sm font-semibold">Profile usage and developer drafts</h3>
        <p className="mt-1 text-sm text-muted-foreground">Prioritize a first-class asset pack from observed usage. Exports contain approved field definitions and review tasks; observed values supply no defaults.</p>
      </div>
      <Button variant="outline" size="sm" onClick={refresh} disabled={exporting !== null}>Refresh usage</Button>
    </div>
    {error ? <StatusBanner tone="warning" title="Profile assessment needs attention" detail={error} role="alert" /> : null}
    {notice ? <p role="status" className="text-sm text-muted-foreground">{notice}</p> : null}
    {!result && !error ? <p role="status" className="text-sm text-muted-foreground">Loading profile usage…</p> : null}
    {result ? <>
      <p className="text-sm text-muted-foreground">{result.scope}</p>
      <p className="text-xs text-muted-foreground">Observed <time dateTime={result.observedAtUtc}>{result.observedAtUtc}</time></p>
      <p className="text-xs text-muted-foreground">Required-field presence covers active reference records. Missing, null and blank values are absent; presence does not establish type or economic validity.</p>
      {!result.canExport ? <p className="text-sm text-muted-foreground">Modify Security Master permission is required to export a developer draft.</p> : null}
      <DenseDataTable columns={columns} rows={result.profiles}
        getRowId={assessment => `${assessment.profileId}:${assessment.version}`}
        getRowAriaLabel={assessment => `${assessment.name} v${assessment.version}; ${assessment.usage.activeRecordCount} active reference records`}
        emptyText="No currently approved profile versions are available."
        ariaLabel="Usage of exact approved profile versions"
        caption="Reference-record counts and required-field presence for exact currently approved profile versions; developer export does not register a class." />
      {result.unresolvedUsage.length > 0 ? <details className="rounded border border-border/60 p-3">
        <summary className="cursor-pointer text-sm font-medium">Unresolved profile references ({result.unresolvedUsage.reduce((sum, row) => sum + row.activeRecordCount + row.inactiveRecordCount + row.unknownStatusRecordCount, 0)} records)</summary>
        <ul className="mt-2 grid gap-2 text-sm text-muted-foreground">{result.unresolvedUsage.map((row, index) => <li key={index}>
          {row.profileId ?? "Missing profile"} · {row.version === null ? "version unavailable" : `v${row.version}`} — {row.unresolvedReason}; {row.activeRecordCount} active, {row.inactiveRecordCount} inactive, {row.unknownStatusRecordCount} unknown status.
        </li>)}</ul>
      </details> : null}
      <p className="text-xs text-muted-foreground">Developer draft only. Lifecycle, valuation and journal behavior require implementation and review before any class can be registered.</p>
    </> : null}
  </section>;
}

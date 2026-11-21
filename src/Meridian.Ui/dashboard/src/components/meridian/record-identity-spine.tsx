import { ArrowLeft, ChevronRight, X } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import {
  appendRecordWorkspaceContext,
  buildRecordWorkspaceContext,
  RECORD_SCOPE_LABELS,
  relatedRecordWork,
  withoutRecordWorkspaceContext,
  type RecordIdentity,
  type RecordWorkspaceContext
} from "@/lib/record-workspace-context";
import { workspaceForPath, type WorkstationRouteQueryValue } from "@/lib/workspace";
import "@/styles/record-identity-spine.css";

const kindLabels = { instrument: "Instrument", account: "Account", fund: "Fund", position: "Position" };

export function useRecordWorkspaceContext(
  record: RecordIdentity | null,
  originQuery: Record<string, WorkstationRouteQueryValue> | null,
  originHash?: string
) {
  const location = useLocation();
  return record && originQuery ? buildRecordWorkspaceContext(record, location, originQuery, originHash) : null;
}

export function RecordIdentitySpine({
  record,
  context,
  showName = false
}: {
  record: RecordIdentity;
  context: RecordWorkspaceContext | null;
  showName?: boolean;
}) {
  const { pathname } = useLocation();
  return (
    <section className="record-identity-spine" aria-label={`${kindLabels[record.kind]} identity`}>
      {showName || !record.identifier ? <RecordName record={record} /> : null}
      <IdentityFacts record={record} />
      <RecordTechnicalIdentity record={record} />
      {context ? <RelatedWork context={context} pathname={pathname} /> : null}
    </section>
  );
}

export function RecordWorkspaceHandoff({ context }: { context: RecordWorkspaceContext }) {
  const { pathname, search, hash } = useLocation();
  const { record, origin, operatingScope } = context;
  const originWorkspace = workspaceForPath(origin.split(/[?#]/, 1)[0]).label;
  const recordScope = scopeEntries(record.scope);
  const workspaceScope = scopeEntries(operatingScope).filter(([key, value]) => record.scope?.[key] !== value);

  return (
    <section className="record-identity-spine record-identity-spine--handoff" aria-label="Originating record">
      <div className="record-identity-heading">
        <RecordName record={record} originWorkspace={originWorkspace} />
        <div className="record-identity-links">
          <Link to={origin}><ArrowLeft aria-hidden="true" />Return to {originWorkspace}</Link>
          <Link to={`${pathname}${withoutRecordWorkspaceContext(search)}${hash}`} replace aria-label="Dismiss originating record" title="Dismiss originating record">
            <X aria-hidden="true" />
          </Link>
        </div>
      </div>
      <IdentityFacts record={record} captured />
      <RecordTechnicalIdentity record={record} />
      <div className="record-identity-disclosures">
        {recordScope.length > 0 || workspaceScope.length > 0 ? (
          <details className="record-identity-disclosure">
            <summary><ChevronRight aria-hidden="true" />Origin scope</summary>
            <dl className="record-identity-facts record-identity-scope">
              {recordScope.map(([key, value]) => <IdentityFact key={key} label={RECORD_SCOPE_LABELS[key]} value={value} mono />)}
              {workspaceScope.map(([key, value]) => <IdentityFact key={`workspace-${key}`} label={`Workspace ${RECORD_SCOPE_LABELS[key].toLowerCase()}`} value={value} mono />)}
            </dl>
          </details>
        ) : null}
        <RelatedWork context={context} pathname={pathname} />
      </div>
    </section>
  );
}

function RecordName({ record, originWorkspace }: { record: RecordIdentity; originWorkspace?: string }) {
  return (
    <div className="record-identity-name">
      <span>{kindLabels[record.kind]}{originWorkspace ? ` from ${originWorkspace}` : null}</span>
      <strong>{record.name}</strong>
    </div>
  );
}

function IdentityFacts({ record, captured = false }: { record: RecordIdentity; captured?: boolean }) {
  return (
    <dl className="record-identity-facts">
      {record.identifier ? <IdentityFact label="Identifiers" value={record.identifier} mono /> : null}
      <IdentityFact label="Asset class" value={record.assetClass} />
      <IdentityFact label="Entity / book" value={[record.entity, record.book].filter(Boolean).join(" / ")} />
      <IdentityFact label={captured ? "Origin status" : "Status"} value={record.status} />
      <IdentityFact label="As of" value={record.asOf} asDate mono />
      <div className="record-identity-trust">
        <dt>{captured ? "Origin trust" : "Trust"}</dt>
        <dd>
          <Badge variant={captured ? "outline" : record.trustTone === "review" ? "warning" : record.trustTone === "ready" ? "success" : "outline"}>
            {record.trust || "Unknown"}
          </Badge>
        </dd>
      </div>
    </dl>
  );
}

function RecordTechnicalIdentity({ record }: { record: RecordIdentity }) {
  return (
    <details className="record-identity-disclosure">
      <summary><ChevronRight aria-hidden="true" />Technical identity</summary>
      <dl className="record-identity-facts">
        <IdentityFact label="Record ID" value={record.id} mono />
      </dl>
    </details>
  );
}

function IdentityFact({ label, value, mono, asDate }: { label: string; value?: string | null; mono?: boolean; asDate?: boolean }) {
  const isoDate = asDate ? value?.match(/^\d{4}-\d{2}-\d{2}(?=$|T)/)?.[0] : null;
  return (
    <div>
      <dt>{label}</dt>
      <dd className={mono ? "font-mono" : undefined}>{isoDate ? <time dateTime={value ?? undefined}>{isoDate}</time> : value || "Not supplied"}</dd>
    </div>
  );
}

function RelatedWork({ context, pathname }: { context: RecordWorkspaceContext; pathname: string }) {
  const links = relatedRecordWork(context.record, pathname).flatMap(({ label, route }) => {
    const href = appendRecordWorkspaceContext(route, context);
    return href ? [{ label, href }] : [];
  });

  return links.length > 0 ? (
    <details className="record-identity-disclosure">
      <summary><ChevronRight aria-hidden="true" />Related work</summary>
      <div className="record-identity-links">
        {links.map(({ href, label }) => <Link key={href} to={href}>{label}</Link>)}
      </div>
    </details>
  ) : <span className="record-identity-unavailable">Related links unavailable for this record context</span>;
}

function scopeEntries(scope: RecordWorkspaceContext["operatingScope"] | undefined) {
  return (Object.keys(RECORD_SCOPE_LABELS) as (keyof typeof RECORD_SCOPE_LABELS)[]).flatMap((key) => {
    const value = scope?.[key];
    return value ? [[key, value] as const] : [];
  });
}

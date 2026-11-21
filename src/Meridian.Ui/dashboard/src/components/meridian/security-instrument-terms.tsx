import { useEffect, useId, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { getSecurityInstrumentTerms } from "@/lib/api/security-instrument-terms.api";
import type { SecurityInstrumentTermField, SecurityInstrumentTerms as Terms } from "@/types/security-instrument-terms";

export interface SecurityInstrumentTermsProps {
  securityId: string;
  mode?: "summary" | "full";
  expectedVersion?: number;
  expectedFingerprint?: string;
  onOpenFull?: () => void;
}

type TermsState =
  | { request: string; status: "loading" }
  | { request: string; status: "error"; message: string }
  | { request: string; status: "ready"; terms: Terms };

const importantKeys: Record<string, string[]> = {
  bond: ["couponType", "couponRate", "maturity", "maturityDate", "paymentFrequency", "dayCount", "dayCountConvention", "par", "faceValue"],
  equity: ["classification", "shareClass", "votingRightsCat", "exchange", "exchangeCode", "dividendCurrency", "votingRights", "sharesOutstanding"],
  option: [
    "underlyingId",
    "underlyingSecurityId",
    "putCall",
    "strikePrice",
    "strike",
    "expiry",
    "expiryDate",
    "expirationDate",
    "exerciseStyle",
    "multiplier",
    "contractMultiplier"
  ],
  future: [
    "rootSymbol",
    "underlyingId",
    "underlyingSecurityId",
    "contractMonth",
    "expiry",
    "expiryDate",
    "expirationDate",
    "contractSize",
    "multiplier",
    "contractMultiplier",
    "settlementType"
  ],
  swap: ["notional", "notionalAmount", "effectiveDate", "maturityDate", "fixedRate", "floatingRateIndex"],
  loan: ["borrower", "maturity", "currentCouponRate", "referenceIndex", "spreadBps", "principal", "principalAmount", "interestRate", "maturityDate", "paymentFrequency", "dayCount", "dayCountConvention"],
  mortgagebackedsecurity: ["poolFactor", "currentFactor", "couponRate", "maturityDate", "paymentFrequency"],
  cash: ["accountType", "interestRate", "dayCountConvention"]
};

function allFields(fields: SecurityInstrumentTermField[]): SecurityInstrumentTermField[] {
  return fields.flatMap((field) => [field, ...allFields(field.children)]);
}

function highlights(terms: Terms) {
  const fields = allFields(terms.fields);
  const normalizedClass = terms.schema.assetClass.replace(/[^a-z]/gi, "").toLowerCase();
  const keys =
    importantKeys[normalizedClass] ??
    Object.entries(importantKeys).find(([name]) => normalizedClass.includes(name))?.[1] ??
    [];
  const preferred = keys.flatMap((key) =>
    fields.filter((field) => field.key.toLowerCase() === key.toLowerCase() && !field.children.length)
  );
  return (
    preferred.length
      ? preferred
      : fields.filter((field) => field.isDeclared && !field.children.length && field.state !== "Missing")
  ).slice(0, 8);
}

function filterField(field: SecurityInstrumentTermField, query: string): SecurityInstrumentTermField | null {
  if (
    [field.label, field.key, field.group, field.path, field.valueText, field.validationMessage].some((value) =>
      value?.toLowerCase().includes(query)
    )
  )
    return field;
  const children = field.children
    .map((child) => filterField(child, query))
    .filter((child): child is SecurityInstrumentTermField => child !== null);
  return children.length ? { ...field, children } : null;
}

function stateLabel(field: SecurityInstrumentTermField): string | null {
  switch (field.state) {
    case "Missing":
      return "Not supplied";
    case "Null":
      return "Explicit null";
    case "Invalid":
      return "Invalid value";
    case "Ambiguous":
      return "Ambiguous value";
    default:
      return null;
  }
}

function FieldValue({ field }: { field: SecurityInstrumentTermField }) {
  const state = stateLabel(field);
  const scalar =
    field.valueText !== null
      ? field.valueText === ""
        ? "Empty string"
        : field.valueText
      : field.jsonType === "Array"
        ? "Array"
        : field.jsonType === "Object"
          ? "Object"
          : "Value unavailable";
  const hasValue = field.state !== "Missing" && field.state !== "Null";
  return (
    <div className="space-y-1">
      {state ? (
        <span
          className={
            field.state === "Invalid" || field.state === "Ambiguous"
              ? "font-semibold text-warning"
              : "text-muted-foreground"
          }
        >
          {state}
        </span>
      ) : null}
      {hasValue ? (
        <span className="block whitespace-pre-wrap break-words font-medium tabular-nums">{scalar}</span>
      ) : null}
      {field.unitStatus === "Explicit" ? (
        <span className="block text-xs text-muted-foreground">Unit: {field.unit || "Explicit unit unavailable"}</span>
      ) : null}
      {field.unitStatus === "Unspecified" ? (
        <span className="block text-xs text-muted-foreground">Unit unspecified in retained terms</span>
      ) : null}
      {field.validationMessage ? (
        <p className="text-xs leading-5 text-muted-foreground">{field.validationMessage}</p>
      ) : null}
    </div>
  );
}

function TermFields({ fields, initiallyOpen }: { fields: SecurityInstrumentTermField[]; initiallyOpen: boolean }) {
  const [limit, setLimit] = useState(40);
  return (
    <>
      <dl className="divide-y divide-border/70">
        {fields.slice(0, limit).map((field, index) => (
          <TermField key={`${field.path}-${index}`} field={field} initiallyOpen={initiallyOpen} />
        ))}
      </dl>
      {fields.length > limit ? (
        <Button type="button" size="sm" variant="outline" onClick={() => setLimit((value) => value + 40)}>
          Show next {Math.min(40, fields.length - limit)} of {fields.length - limit} remaining terms
        </Button>
      ) : null}
    </>
  );
}

function TermField({ field, initiallyOpen }: { field: SecurityInstrumentTermField; initiallyOpen: boolean }) {
  const [open, setOpen] = useState(initiallyOpen);
  return (
    <div className="grid min-w-0 gap-2 py-3 lg:grid-cols-[minmax(12rem,0.45fr)_minmax(0,1fr)] lg:gap-5">
      <dt className="min-w-0 space-y-1 text-sm font-semibold">
        <span className="block break-words">{field.label || field.key}</span>
        {field.required ? (
          <span className="block text-xs font-normal text-muted-foreground">Required by schema</span>
        ) : null}
        {!field.isDeclared ? <Badge variant="outline">Undeclared field</Badge> : null}
        {field.role?.toLowerCase() === "operational" ? <Badge variant="outline">Operational reference</Badge> : null}
      </dt>
      <dd className="min-w-0 space-y-2 text-sm">
        <FieldValue field={field} />
        {field.children.length ? (
          <details
            open={open}
            onToggle={(event) => setOpen(event.currentTarget.open)}
            className="border-l border-border pl-3"
          >
            <summary className="cursor-pointer py-1 text-xs font-semibold focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40">
              Inspect {field.children.length} nested {field.children.length === 1 ? "term" : "terms"} in{" "}
              {field.label || field.key}
            </summary>
            {open ? <TermFields fields={field.children} initiallyOpen={initiallyOpen} /> : null}
          </details>
        ) : null}
        <details className="text-xs">
          <summary className="cursor-pointer py-1 text-muted-foreground focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40">
            Field evidence for {field.label || field.key}
          </summary>
          <dl className="mt-2 grid gap-2 border-l border-border pl-3">
            <div>
              <dt className="text-muted-foreground">Retained path</dt>
              <dd className="break-all font-mono">{field.path}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Declared / retained type</dt>
              <dd>
                {field.declaredType ?? "Not declared"} / {field.jsonType}
              </dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Role</dt>
              <dd>{field.role ?? "Not declared"}</dd>
            </div>
            {field.unitPath ? (
              <div>
                <dt className="text-muted-foreground">Unit source path</dt>
                <dd className="break-all font-mono">{field.unitPath}</dd>
              </div>
            ) : null}
          </dl>
          {field.rawJson !== null ? (
            <pre className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap break-all border border-border bg-secondary/20 p-2 font-mono text-xs">
              {field.rawJson}
            </pre>
          ) : null}
        </details>
      </dd>
    </div>
  );
}

function RawTerms({ label, value }: { label: string; value: string | null }) {
  return (
    <details className="border-t border-border py-2">
      <summary className="cursor-pointer py-1 text-sm focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40">
        {label}
      </summary>
      {value === null ? (
        <p className="py-2 text-sm text-muted-foreground">Not retained.</p>
      ) : (
        <pre className="mt-2 max-h-96 overflow-auto whitespace-pre-wrap break-all border border-border bg-secondary/20 p-3 font-mono text-xs">
          {value}
        </pre>
      )}
    </details>
  );
}

export function SecurityInstrumentTerms({
  securityId,
  mode = "full",
  expectedVersion,
  expectedFingerprint,
  onOpenFull
}: SecurityInstrumentTermsProps) {
  const headingId = useId();
  const request = JSON.stringify([securityId, expectedVersion, expectedFingerprint]);
  const [state, setState] = useState<TermsState>({ request, status: "loading" });
  const [attempt, setAttempt] = useState(0);
  const [search, setSearch] = useState("");

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();
    setState({ request, status: "loading" });
    setSearch("");
    getSecurityInstrumentTerms(securityId, { signal: controller.signal, expectedVersion, expectedFingerprint })
      .then((terms) => {
        if (!cancelled) setState({ request, status: "ready", terms });
      })
      .catch((error: unknown) => {
        if (!cancelled)
          setState({
            request,
            status: "error",
            message: error instanceof Error ? error.message : "Instrument terms are unavailable."
          });
      });
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [securityId, expectedVersion, expectedFingerprint, request, attempt]);

  const frameClass = "min-w-0 space-y-4 border border-border bg-background/60 px-4 py-3";
  if (state.request !== request || state.status === "loading") {
    return (
      <section aria-label="Instrument terms" className={frameClass}>
        <p role="status" className="text-sm text-muted-foreground">
          Loading retained instrument terms…
        </p>
      </section>
    );
  }
  if (state.status === "error") {
    return (
      <section aria-label="Instrument terms" className={frameClass}>
        <p role="alert" className="text-sm">
          Couldn’t load instrument terms. {state.message}
        </p>
        <Button type="button" size="sm" variant="outline" onClick={() => setAttempt((value) => value + 1)}>
          Retry instrument terms
        </Button>
      </section>
    );
  }

  const { terms } = state;
  const query = search.trim().toLowerCase();
  const visibleFields = query
    ? terms.fields
        .map((field) => filterField(field, query))
        .filter((field): field is SecurityInstrumentTermField => field !== null)
    : terms.fields;
  const flattened = allFields(terms.fields);
  const reviewCount = flattened.filter(
    (field) =>
      field.state === "Invalid" ||
      field.state === "Ambiguous" ||
      (field.required && (field.state === "Missing" || field.state === "Null"))
  ).length;
  const groups = new Map<string, SecurityInstrumentTermField[]>();
  for (const field of visibleFields) {
    const group = field.group || "Other retained terms";
    groups.set(group, [...(groups.get(group) ?? []), field]);
  }
  const summaryFields = highlights(terms);

  return (
    <section aria-labelledby={headingId} className={frameClass}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id={headingId} className="text-sm font-semibold">
          {mode === "summary" ? "Instrument terms" : "Full instrument terms"}
        </h3>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={terms.status === "Ready" ? "outline" : "warning"}>
            {terms.status === "ReviewRequired" ? "Review required" : terms.status}
          </Badge>
          {mode === "summary" && onOpenFull ? (
            <Button type="button" size="sm" variant="outline" onClick={onOpenFull}>
              View all terms
            </Button>
          ) : null}
        </div>
      </div>
      <p className="text-xs text-muted-foreground">
        {terms.assetClass} · {terms.currency} · Economic definition v{terms.version} · Retrieved {terms.retrievedAtUtc}
      </p>
      {terms.source.status !== "Matched" || reviewCount || terms.diagnostics.length ? (
        <div className="space-y-1 border-l-2 border-warning pl-3 text-sm">
          {terms.source.status !== "Matched" ? (
            <p>
              Source evidence: {terms.source.status === "VersionMismatch" ? "Version mismatch" : "Unavailable"}.{" "}
              {terms.source.summary}
            </p>
          ) : null}
          {reviewCount ? (
            <p>
              {reviewCount} {reviewCount === 1 ? "term requires" : "terms require"} review. Retained values remain
              inspectable.
            </p>
          ) : null}
          {terms.diagnostics.length ? (
            <ul className="list-disc space-y-1 pl-4">
              {terms.diagnostics.map((message, index) => (
                <li key={`${index}-${message}`}>{message}</li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
      {mode === "summary" ? (
        summaryFields.length ? (
          <dl className="grid grid-cols-2 gap-4 xl:grid-cols-3">
            {summaryFields.map((field, index) => (
              <div key={`${field.path}-${index}`} className="min-w-0 text-sm">
                <dt className="mb-1 text-xs text-muted-foreground">{field.label || field.key}</dt>
                <dd>
                  <FieldValue field={field} />
                </dd>
              </div>
            ))}
          </dl>
        ) : (
          <p className="text-sm text-muted-foreground">
            No scalar terms are supplied. Open the full terms to inspect retained blocks and schema coverage.
          </p>
        )
      ) : (
        <>
          <Input
            type="search"
            aria-label="Search instrument terms"
            placeholder="Find a term, value, or retained path"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          <p role="status" className="text-xs text-muted-foreground">
            {query
              ? `${allFields(visibleFields).length} of ${flattened.length} terms shown with matching context`
              : `${flattened.length} retained and schema-declared terms`}
            . Values preserve source text; units are shown only when explicitly retained.
          </p>
          {!visibleFields.length ? (
            <p className="text-sm text-muted-foreground">
              {query ? `No terms match “${search.trim()}”.` : "No instrument terms are supplied for this record."}
            </p>
          ) : null}
          {[...groups].map(([group, fields]) => (
            <section
              key={`${group}-${terms.fingerprint}-${query}`}
              aria-label={`${group} instrument terms`}
              className="border-t border-border pt-3"
            >
              <h4 className="text-sm font-semibold">{group}</h4>
              <TermFields fields={fields} initiallyOpen={Boolean(query)} />
            </section>
          ))}
          <details className="border-t border-border pt-3 text-sm">
            <summary className="cursor-pointer py-1 font-semibold focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40">
              Terms provenance & schema
            </summary>
            <dl className="mt-3 grid gap-3 lg:grid-cols-2">
              {[
                ["Security ID", terms.securityId],
                ["Economic-definition version", String(terms.version)],
                ["Effective from", terms.effectiveFrom ?? "Not supplied"],
                ["Effective to", terms.effectiveTo ?? "Open / not supplied"],
                ["Retained terms format", terms.termsFormat],
                ["Asset-class terms version", String(terms.assetClassTermsVersion)],
                ["Retained asset class", terms.assetClass],
                ["Canonical schema asset class", terms.schema.assetClass],
                ["Schema read / write version", `${terms.schema.readVersion} / ${terms.schema.writeVersion}`],
                ["Lot unit", terms.schema.lotUnit ?? "Not declared"],
                ["Cash-flow behavior", terms.schema.cashFlowBehavior ?? "Not declared"],
                ["Profile ID", terms.schema.profileId ?? "No profile"],
                [
                  "Profile version",
                  terms.schema.profileVersion === null ? "Not supplied" : String(terms.schema.profileVersion)
                ],
                ["Profile status", terms.schema.profileStatus ?? "Not supplied"],
                ["Source evidence", `${terms.source.status} · ${terms.source.summary}`],
                ["Retained fingerprint (SHA-256)", terms.fingerprint]
              ].map(([label, value]) => (
                <div key={label} className="min-w-0">
                  <dt className="text-xs text-muted-foreground">{label}</dt>
                  <dd className="mt-1 break-all text-sm">{value}</dd>
                </div>
              ))}
            </dl>
          </details>
          <div aria-label="Retained term blocks">
            <RawTerms label="Raw common terms" value={terms.commonTermsJson} />
            <RawTerms label="Raw asset-specific terms" value={terms.assetSpecificTermsJson} />
            <RawTerms label="Raw source provenance" value={terms.source.provenanceJson} />
          </div>
        </>
      )}
    </section>
  );
}

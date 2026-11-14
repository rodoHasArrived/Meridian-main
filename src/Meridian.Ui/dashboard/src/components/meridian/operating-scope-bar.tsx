import { useId } from "react";
import { ChevronDown } from "lucide-react";
import {
  buildOperatingScopeBarSegments,
  type AppShellOperatingScopeQueryKey,
  type AppShellOperatingScopeState,
  type OperatingScopeValueLabels
} from "@/app-shell.operating-scope";
import { cn } from "@/lib/utils";

/**
 * The operating scope as a permanent, labelled part of the top bar:
 * `Scope  Entity … › Fund … › Account … › Period …`, plus Subject, Run and Provider when set. An
 * unset Entity, Fund or Account reads "All authorized" and an unset Period "All periods", so an
 * unscoped view never looks like an empty one. Ids show as names when `valueLabels` supplies them. It is shown on every route (the scope still carries
 * on a page that does not filter by it) and opens the shell's scope picker. A set dimension the
 * current screen does not filter by stays in the bar, muted, so a carried scope is never invisible.
 */
export function OperatingScopeBar({
  operatingScope,
  onEditOperatingScope,
  scopeDimensionsInEffect,
  valueLabels
}: {
  operatingScope: AppShellOperatingScopeState;
  onEditOperatingScope?: () => void;
  scopeDimensionsInEffect?: AppShellOperatingScopeQueryKey[];
  valueLabels?: OperatingScopeValueLabels;
}) {
  const applicabilityId = useId();
  const segments = buildOperatingScopeBarSegments(operatingScope, scopeDimensionsInEffect, valueLabels);
  const spoken = segments.map((segment) => `${segment.label} ${segment.value}`).join(", ");
  const applied = segments.filter((segment) => segment.isSet && segment.applied);
  const retained = segments.filter((segment) => segment.isSet && !segment.applied);
  const describe = (items: typeof segments) => items.map((segment) => `${segment.label} ${segment.value}`).join(", ");
  const hasRetained = retained.length > 0;

  const value = (
    <>
      <span className="operating-scope-bar-label" aria-hidden="true">Scope</span>
      <span className="operating-scope-bar-segments" aria-hidden="true">
        {segments.map((segment, index) => (
          <span
            key={segment.id}
            className={cn(
              "operating-scope-bar-segment",
              !segment.isSet && "operating-scope-bar-segment-unset",
              !segment.applied && "operating-scope-bar-segment-retained"
            )}
            title={!segment.isSet
              ? segment.id === "window"
                ? `${segment.label}: not narrowed; each screen shows its own default range`
                : `${segment.label}: not narrowed; covers everything you are authorized to see`
              : segment.applied
                ? `${segment.label}: applied here`
                : `${segment.label}: carried, but not applied on this screen`}
          >
            {index > 0 ? <span className="operating-scope-bar-sep">›</span> : null}
            <span className="operating-scope-bar-dimension">{segment.label}</span>
            <span className="operating-scope-bar-value">{segment.value}</span>
          </span>
        ))}
      </span>
    </>
  );

  return (
    <section className="operating-scope-bar" aria-label="Operating scope bar">
      {hasRetained ? (
        <p id={applicabilityId} className="sr-only">
          {applied.length > 0 ? `Applied on this screen: ${describe(applied)}.` : "No selected scope dimensions apply on this screen."}
          {` Retained for other screens: ${describe(retained)}.`}
        </p>
      ) : null}
      {onEditOperatingScope ? (
        <button
          type="button"
          className="operating-scope-bar-button"
          onClick={onEditOperatingScope}
          aria-label={`Operating scope: ${spoken}`}
          aria-describedby={hasRetained ? applicabilityId : undefined}
          aria-haspopup="dialog"
          title="Change operating scope"
        >
          {value}
          <ChevronDown className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        </button>
      ) : (
        <span
          className="operating-scope-bar-button operating-scope-bar-static"
          role="group"
          aria-label={`Operating scope: ${spoken}`}
          aria-describedby={hasRetained ? applicabilityId : undefined}
        >
          {value}
        </span>
      )}
    </section>
  );
}

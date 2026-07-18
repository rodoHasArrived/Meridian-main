import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

const generatedRoutes = UI_API_ROUTES as Record<string, string>;

function replaceRouteToken(template: string, token: string, value: string): string {
  const marker = `{${token}}`;
  if (!template.includes(marker)) {
    throw new Error(`Reporting route template is missing ${marker}.`);
  }

  return template.replace(marker, encodeURIComponent(value.trim()));
}

function generatedRoute(...names: string[]): string | null {
  for (const name of names) {
    const route = generatedRoutes[name];
    if (typeof route === "string" && route.trim()) {
      return route;
    }
  }

  return null;
}

function requiredGeneratedRoute(name: string): string {
  const route = generatedRoute(name);
  if (!route) {
    throw new Error(`Required generated reporting route ${name} is unavailable.`);
  }

  return route;
}

export function governedReportingRunPath(runId: string): string {
  return replaceRouteToken(requiredGeneratedRoute("ReportingGovernedRun"), "runId", runId);
}

export function governedReportingTransitionPath(
  runId: string,
  transition: "validate" | "submit" | "approve" | "release"
): string {
  const routeName = {
    validate: "ReportingGovernedRunValidate",
    submit: "ReportingGovernedRunSubmit",
    approve: "ReportingGovernedRunApprove",
    release: "ReportingGovernedRunRelease"
  }[transition];

  return replaceRouteToken(requiredGeneratedRoute(routeName), "runId", runId);
}

export function governedReportingRestatementRequestPath(runId: string): string {
  return replaceRouteToken(requiredGeneratedRoute("ReportingGovernedRunRestatementRequests"), "runId", runId);
}

export function governedReportingRestatementApprovalPath(requestId: string): string {
  return replaceRouteToken(requiredGeneratedRoute("ReportingGovernedRestatementApprove"), "requestId", requestId);
}

/** Tenant- and access-filtered immutable revision history for a governed series. */
export function governedReportingSeriesHistoryPath(seriesId: string): string {
  const route = generatedRoute(
    "ReportingGovernedRunSeries",
    "ReportingGovernedRunSeriesHistory",
    "ReportingGovernedSeries"
  );
  if (!route) {
    return `/api/fund-structure/reporting/runs/series/${encodeURIComponent(seriesId.trim())}`;
  }

  if (route.includes("{seriesId}")) {
    return replaceRouteToken(route, "seriesId", seriesId);
  }

  return replaceRouteToken(route, "runId", seriesId);
}

const distributionRoot = "/api/fund-structure/reporting/distribution";

export function secureReportingDeliveryQueuePath(): string {
  return generatedRoute(
    "ReportingDistributionQueueDelivery",
    "ReportingSecureDeliveryQueue",
    "ReportingDistributionDeliveries"
  )
    ?? `${distributionRoot}/deliveries`;
}

export function secureReportingDeliveryPath(jobId: string): string {
  const route = generatedRoute("ReportingSecureDelivery", "ReportingDistributionDelivery");
  return route
    ? replaceRouteToken(route, "jobId", jobId)
    : `${distributionRoot}/deliveries/${encodeURIComponent(jobId.trim())}`;
}

export function secureReportingDeliveryHistoryPath(runId: string): string {
  const route = generatedRoute("ReportingSecureDeliveryHistory", "ReportingDistributionPackageDeliveries");
  return route
    ? replaceRouteToken(route, "runId", runId)
    : `${distributionRoot}/packages/${encodeURIComponent(runId.trim())}/deliveries`;
}

export function secureReportingArtifactDownloadPath(runId: string, artifactId: string): string {
  const route = generatedRoute("ReportingDistributionArtifactDownload");
  if (route) {
    return replaceRouteToken(
      replaceRouteToken(route, "runId", runId),
      "artifactId",
      artifactId
    );
  }
  const runPath = `${distributionRoot}/packages/${encodeURIComponent(runId.trim())}`;
  return `${runPath}/artifacts/${encodeURIComponent(artifactId.trim())}`;
}

export function secureReportingAccessGrantIssuePath(): string {
  return generatedRoute(
    "ReportingDistributionIssueAccessGrant",
    "ReportingSecureAccessGrantIssue",
    "ReportingDistributionAccessGrants"
  )
    ?? `${distributionRoot}/access-grants`;
}

export function secureReportingAccessGrantRevokePath(grantId: string): string {
  const route = generatedRoute(
    "ReportingDistributionRevokeAccessGrant",
    "ReportingSecureAccessGrantRevoke",
    "ReportingDistributionAccessGrantRevoke"
  );
  return route
    ? replaceRouteToken(route, "grantId", grantId)
    : `${distributionRoot}/access-grants/${encodeURIComponent(grantId.trim())}/revoke`;
}

/** Authenticated, credential-free transport capability catalog. */
export function secureReportingTransportCapabilitiesPath(): string {
  return generatedRoute(
    "ReportingSecureTransportCapabilities",
    "ReportingDistributionTransportCapabilities",
    "ReportingDistributionTransports"
  ) ?? `${distributionRoot}/transports`;
}

/** Durable tenant/run-scoped grant discovery. */
export function secureReportingAccessGrantHistoryPath(runId: string): string {
  const route = generatedRoute(
    "ReportingSecureAccessGrantHistory",
    "ReportingDistributionPackageAccessGrants",
    "ReportingDistributionRunAccessGrants"
  );
  return route
    ? replaceRouteToken(route, "runId", runId)
    : `${distributionRoot}/packages/${encodeURIComponent(runId.trim())}/access-grants`;
}

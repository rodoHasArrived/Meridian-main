import type { GovernedReportingRun } from "@/types/reporting-governance";

export interface ClientPackageArtifactGate {
  isClientPackage: boolean;
  isComplete: boolean;
  requiredArtifactIds: string[];
  missingArtifactIds: string[];
  disabledReason: string | null;
}

export function resolveClientPackageArtifactGate(
  run: GovernedReportingRun | null
): ClientPackageArtifactGate {
  if (!run || run.normalizedParameters.outputFormat !== "ClientPackage") {
    return {
      isClientPackage: false,
      isComplete: true,
      requiredArtifactIds: [],
      missingArtifactIds: [],
      disabledReason: null
    };
  }

  const normalizedRunId = run.runId.trim();
  const requiredArtifactIds = [`${normalizedRunId}.pdf`, `${normalizedRunId}.xlsx`];
  const releasedArtifactIds = new Set((run.release?.artifacts ?? []).map((artifact) => artifact.artifactId));
  const missingArtifactIds = requiredArtifactIds.filter((artifactId) => !releasedArtifactIds.has(artifactId));

  return {
    isClientPackage: true,
    isComplete: missingArtifactIds.length === 0,
    requiredArtifactIds,
    missingArtifactIds,
    disabledReason: missingArtifactIds.length === 0
      ? null
      : `Distribution is blocked because the released client package is missing ${missingArtifactIds.join(" and ")}.`
  };
}

export function enforceClientPackageArtifactSelection(
  selectedArtifactIds: readonly string[],
  releasedArtifactIds: readonly string[],
  gate: ClientPackageArtifactGate
): string[] {
  const selected = new Set(selectedArtifactIds);
  for (const artifactId of gate.requiredArtifactIds) {
    if (releasedArtifactIds.includes(artifactId)) {
      selected.add(artifactId);
    }
  }

  return releasedArtifactIds.filter((artifactId) => selected.has(artifactId));
}

import { apiGetJson, apiPostJson } from "@/lib/api/http-client";

export interface ProfileUsage {
  profileId: string | null;
  version: number | null;
  activeRecordCount: number;
  inactiveRecordCount: number;
  unknownStatusRecordCount: number;
  unresolvedReason: string | null;
  completeActiveRecordCount?: number | null;
  incompleteActiveRecordCount?: number | null;
  unknownActiveRecordCount?: number | null;
  missingRequiredFieldCounts?: Record<string, number> | null;
}

export interface ProfilePromotionAssessment {
  profileId: string;
  version: number;
  name: string;
  profileFingerprint: string;
  usage: ProfileUsage;
  definitionAssessment: { score: number; readiness: string; promotionRationale: string } | null;
}

export interface ProfilePromotionAssessments {
  scope: string;
  observedAtUtc: string;
  profiles: ProfilePromotionAssessment[];
  unresolvedUsage: ProfileUsage[];
  canExport: boolean;
}

export function getProfilePromotionAssessments(signal?: AbortSignal): Promise<ProfilePromotionAssessments> {
  return apiGetJson("/api/security-master/asset-profiles/promotion-assessments", { signal, allowDevelopmentFallback: false });
}

export async function downloadProfileCandidate(assessment: ProfilePromotionAssessment): Promise<void> {
  const artifact = await apiPostJson<unknown>(
    `/api/security-master/asset-profiles/${encodeURIComponent(assessment.profileId)}/versions/${assessment.version}/candidate-export`,
    { expectedProfileFingerprint: assessment.profileFingerprint }, { allowDevelopmentFallback: false });
  const url = URL.createObjectURL(new Blob([JSON.stringify(artifact, null, 2)], { type: "application/json" }));
  try {
    const link = document.createElement("a");
    link.href = url;
    link.download = `meridian-asset-pack-draft-v${assessment.version}.json`;
    link.click();
  } finally {
    // Let the browser consume the download before releasing the local object URL.
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}

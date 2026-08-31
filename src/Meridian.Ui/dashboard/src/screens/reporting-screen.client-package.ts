import type {
  ReportRunParameterDraftField,
  ReportRunParameterDraftState
} from "@/screens/report-run-parameters-screen.view-model";
import type {
  ReportingScheduleArtifactFormat,
  ReportingScheduleDraftState
} from "@/screens/reporting-screen.schedule-management";

export const clientPackageScheduleArtifactFormats: readonly ReportingScheduleArtifactFormat[] = ["Pdf", "Xlsx"];

export function buildClientPackageScheduleFormatSelection(): ReportingScheduleDraftState["formats"] {
  return {
    Pdf: true,
    Xlsx: true,
    Csv: false
  };
}

export function updateScheduleRunParameterDraft(
  current: ReportingScheduleDraftState,
  field: ReportRunParameterDraftField,
  value: string | boolean
): ReportingScheduleDraftState {
  const runParameters = {
    ...current.runParameters,
    [field]: value
  } as ReportRunParameterDraftState;
  if (runParameters.outputFormat !== "ClientPackage") {
    return {
      ...current,
      runParameters
    };
  }

  return {
    ...current,
    runParameters,
    formats: buildClientPackageScheduleFormatSelection(),
    deliveryTargets: current.deliveryTargets.map((target) => ({
      ...target,
      formats: buildClientPackageScheduleFormatSelection()
    }))
  };
}

export function updateScheduleArtifactFormatDraft(
  current: ReportingScheduleDraftState,
  format: ReportingScheduleArtifactFormat,
  isSelected: boolean
): ReportingScheduleDraftState {
  if (current.runParameters.outputFormat === "ClientPackage") {
    return {
      ...current,
      formats: buildClientPackageScheduleFormatSelection()
    };
  }

  return {
    ...current,
    formats: {
      ...current.formats,
      [format]: isSelected
    }
  };
}

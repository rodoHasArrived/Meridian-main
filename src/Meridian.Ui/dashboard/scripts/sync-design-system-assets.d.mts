export interface DesignSystemAssetOptions {
  dashboardRoot?: string;
  packageRoot?: string;
  write?: boolean;
}
export interface DesignSystemAssetResult {
  version: string;
  files: number;
  updated: string[];
}
export function synchronizeDesignSystemAssets(options?: DesignSystemAssetOptions): DesignSystemAssetResult;

import type { WorkspaceKey } from "@/types";

import meridianHeroUrl from "@/assets/brand/meridian-hero.svg";
import meridianMarkLightUrl from "@/assets/brand/meridian-mark-light.svg";
import meridianMarkMonochromeUrl from "@/assets/brand/meridian-mark-monochrome.svg";
import meridianMarkUrl from "@/assets/brand/meridian-mark.svg";
import meridianSymbolUrl from "@/assets/brand/meridian-symbol.svg";
import meridianTilePngUrl from "@/assets/brand/meridian-tile-256.png";
import meridianTileUrl from "@/assets/brand/meridian-tile.svg";
import meridianWordmarkStackedUrl from "@/assets/brand/meridian-wordmark-stacked.svg";
import meridianWordmarkUrl from "@/assets/brand/meridian-wordmark.svg";
import accountingIconUrl from "@/assets/icons/run-ledger.svg";
import dataIconUrl from "@/assets/icons/data-sources.svg";
import portfolioIconUrl from "@/assets/icons/account-portfolio.svg";
import reportingIconUrl from "@/assets/icons/governance.svg";
import settingsIconUrl from "@/assets/icons/settings.svg";
import strategyIconUrl from "@/assets/icons/strategy-builder.svg";
import tradingIconUrl from "@/assets/icons/trading.svg";

export const DESIGN_SYSTEM_PACKAGE_ROOT = "../../../Meridian Design System" as const;
export const DESIGN_SYSTEM_MANIFEST_FILE = `${DESIGN_SYSTEM_PACKAGE_ROOT}/_ds_manifest.json` as const;

export const DESIGN_SYSTEM_TOKEN_FILES = [
  `${DESIGN_SYSTEM_PACKAGE_ROOT}/tokens/theme.css`,
  `${DESIGN_SYSTEM_PACKAGE_ROOT}/tokens/colors.css`,
  `${DESIGN_SYSTEM_PACKAGE_ROOT}/tokens/colors-dark.css`,
  `${DESIGN_SYSTEM_PACKAGE_ROOT}/tokens/typography.css`,
  `${DESIGN_SYSTEM_PACKAGE_ROOT}/tokens/elevation.css`
] as const;

export const DESIGN_SYSTEM_SHELL_COMPONENTS = [
  "NavRail",
  "WorkstationTopbar",
  "StatusBar",
  "SessionControls"
] as const;

export const DESIGN_SYSTEM_CORE_COMPONENTS = [
  "Button",
  "Badge"
] as const;

export const DESIGN_SYSTEM_STATUS_COMPONENTS = [
  "TrustStrip",
  "StatusBar"
] as const;

export const DESIGN_SYSTEM_ACCOUNTING_COMPONENTS = [
  "TrialBalance",
  "AgingTable",
  "ReconciliationPanel"
] as const;

export const DESIGN_SYSTEM_LIVE_COMPONENTS = {
  core: DESIGN_SYSTEM_CORE_COMPONENTS,
  shell: DESIGN_SYSTEM_SHELL_COMPONENTS,
  status: DESIGN_SYSTEM_STATUS_COMPONENTS,
  accounting: DESIGN_SYSTEM_ACCOUNTING_COMPONENTS
} as const;

export type DesignSystemCoreComponentName = typeof DESIGN_SYSTEM_CORE_COMPONENTS[number];
export type DesignSystemShellComponentName = typeof DESIGN_SYSTEM_SHELL_COMPONENTS[number];
export type DesignSystemStatusComponentName = typeof DESIGN_SYSTEM_STATUS_COMPONENTS[number];
export type DesignSystemAccountingComponentName = typeof DESIGN_SYSTEM_ACCOUNTING_COMPONENTS[number];
export type DesignSystemLiveComponentName =
  | DesignSystemCoreComponentName
  | DesignSystemShellComponentName
  | DesignSystemStatusComponentName
  | DesignSystemAccountingComponentName;

export const DESIGN_SYSTEM_SHELL_ASSET_MAPPINGS = {
  brandMark: "assets/brand/meridian-mark-light.svg",
  workspaceTrading: "assets/icons/trading.svg",
  workspacePortfolio: "assets/icons/account-portfolio.svg",
  workspaceAccounting: "assets/icons/run-ledger.svg",
  workspaceReporting: "assets/icons/governance.svg",
  workspaceStrategy: "assets/icons/strategy-builder.svg",
  workspaceData: "assets/icons/data-sources.svg",
  workspaceSettings: "assets/icons/settings.svg"
} as const;

export const meridianBrandAssets = {
  hero: meridianHeroUrl,
  mark: meridianMarkUrl,
  markLight: meridianMarkLightUrl,
  markMonochrome: meridianMarkMonochromeUrl,
  symbol: meridianSymbolUrl,
  tile: meridianTileUrl,
  tilePng: meridianTilePngUrl,
  wordmark: meridianWordmarkUrl,
  wordmarkStacked: meridianWordmarkStackedUrl
} as const;

export const meridianMastheadBrandMarkAsset = meridianBrandAssets.markLight;

export const meridianWorkspaceIconAssets = {
  trading: tradingIconUrl,
  portfolio: portfolioIconUrl,
  accounting: accountingIconUrl,
  reporting: reportingIconUrl,
  strategy: strategyIconUrl,
  data: dataIconUrl,
  settings: settingsIconUrl
} satisfies Record<WorkspaceKey, string>;

export const meridianDesignSystemShellAssets = {
  brandMark: meridianMastheadBrandMarkAsset,
  workspaceIcons: meridianWorkspaceIconAssets
} as const;

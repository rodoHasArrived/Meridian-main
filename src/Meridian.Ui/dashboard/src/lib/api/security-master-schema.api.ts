/**
 * Client for the Security Master asset-class registry (`/api/security-master/asset-classes`), the
 * schema the passport editor builds its term fields from instead of a free-text field path.
 */

import { apiGetJson } from "@/lib/api";
import { securityMasterAssetClassDefinitionEndpoint } from "@/lib/workstation-endpoints";

export type SecurityAssetTermEditorKind =
  | "Text"
  | "Decimal"
  | "Integer"
  | "Toggle"
  | "Date"
  | "Choice"
  | "Convention"
  | "Identifier"
  | "Json";

export type SecurityAssetTermRole = "Economic" | "Operational";

export interface SecurityAssetTermFieldDefinition {
  key: string;
  fieldPath: string;
  label: string;
  type: string;
  editor: SecurityAssetTermEditorKind;
  group: string;
  order: number;
  required: boolean;
  scalarEditable: boolean;
  choices: string[];
  aliases: string[];
  role?: SecurityAssetTermRole;
}

export interface SecurityAssetClassDefinition {
  assetClass: string;
  termsVersion: number;
  lotUnit: string;
  cashFlowBehavior: string;
  fields: SecurityAssetTermFieldDefinition[];
}

export function getSecurityAssetClassDefinition(
  assetClass: string,
  signal?: AbortSignal
): Promise<SecurityAssetClassDefinition> {
  return apiGetJson<SecurityAssetClassDefinition>(securityMasterAssetClassDefinitionEndpoint(assetClass), { signal });
}

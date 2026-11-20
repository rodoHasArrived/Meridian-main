import { useState } from "react";
import { useLinkedRecordSelection } from "@/hooks/use-linked-record-selection";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type {
  AccountingWorkspaceResponse,
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  MultiAssetCoverageSummary,
  PortfolioWorkspaceResponse,
  StrategyRunContinuityDto,
  StrategyWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";
import { buildPortfolioScreenViewModel, type PortfolioRunDrillInData, type PortfolioScreenViewModel } from "./portfolio-screen.view-model";

export function usePortfolioScreenViewModel({
  portfolio,
  trading,
  strategy,
  accounting,
  brokerageConnection,
  brokeragePortfolio,
  multiAssetCoverage,
  selectedRunContinuity = null,
  selectedRunDrillIn = null,
  requestedPositionRef = null,
  requestedBrokerageAccount = null,
  requestedBrokeragePositionId = null,
  pathname = WORKSTATION_ROUTE_CATALOG.portfolio
}: {
  portfolio?: PortfolioWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  strategy: StrategyWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  brokeragePortfolio?: BrokerageHouseholdPortfolio | null;
  multiAssetCoverage?: MultiAssetCoverageSummary | null;
  selectedRunContinuity?: StrategyRunContinuityDto | null;
  selectedRunDrillIn?: PortfolioRunDrillInData | null;
  requestedPositionRef?: string | null;
  requestedBrokerageAccount?: string | null;
  requestedBrokeragePositionId?: string | null;
  pathname?: string;
}): PortfolioScreenViewModel {
  const [selectedPositionId, setSelectedPositionId] = useLinkedRecordSelection(requestedPositionRef);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [selectedBrokeragePositionId, setSelectedBrokeragePositionId] = useLinkedRecordSelection(requestedBrokeragePositionId);
  const [selectedBrokerageAccountKey, setSelectedBrokerageAccountKey] = useLinkedRecordSelection(requestedBrokerageAccount);

  return buildPortfolioScreenViewModel({
    portfolio,
    trading,
    strategy,
    accounting,
    brokerageConnection,
    brokeragePortfolio,
    multiAssetCoverage,
    selectedRunContinuity,
    selectedRunDrillIn,
    pathname,
    selectedPositionId,
    requestedPositionRef,
    selectedRunId,
    selectedBrokeragePositionId,
    requestedBrokeragePositionId,
    selectedBrokerageAccountKey: selectedBrokerageAccountKey ?? requestedBrokerageAccount ?? "all",
    selectPosition: setSelectedPositionId,
    selectRun: setSelectedRunId,
    selectBrokeragePosition: setSelectedBrokeragePositionId,
    selectBrokerageAccount: setSelectedBrokerageAccountKey
  });
}

import { useLocation, useNavigate } from "react-router-dom";
import { DailyControlTowerScreen, type DailyControlTowerScreenProps } from "./daily-control-tower-screen";
import { OperatorReadinessConsole } from "./operator-readiness-console";
import { Tabs, TabPanel } from "@/components/ui/tabs";
import { updateWorkstationSearch } from "@/lib/workstation-close-routing";
import type { AccountingWorkspaceResponse, DataWorkspaceResponse, ReportingWorkspaceResponse, StrategyWorkspaceResponse, TradingWorkspaceResponse } from "@/types";

export interface TodayScreenProps extends DailyControlTowerScreenProps {
  strategy: StrategyWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  data: DataWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  reporting: ReportingWorkspaceResponse | null;
  fundAccountId?: string | null;
}

export function TodayScreen({ strategy, trading, data, accounting, reporting, fundAccountId, ...tower }: TodayScreenProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const view = new URLSearchParams(location.search).get("view") === "readiness" ? "readiness" : "queue";
  return <Tabs aria-label="Today views" value={view} tabs={[{ id: "queue", label: "Queue" }, { id: "readiness", label: "Operational readiness" }]}
    onValueChange={(next) => navigate({ search: updateWorkstationSearch(location.search, "view", next === "queue" ? null : next), hash: location.hash })}>
    <TabPanel>{view === "queue" ? <DailyControlTowerScreen {...tower} /> : null}</TabPanel>
    <TabPanel>{view === "readiness" ? <OperatorReadinessConsole strategy={strategy} trading={trading} data={data} accounting={accounting} reporting={reporting} fundAccountId={fundAccountId} /> : null}</TabPanel>
  </Tabs>;
}

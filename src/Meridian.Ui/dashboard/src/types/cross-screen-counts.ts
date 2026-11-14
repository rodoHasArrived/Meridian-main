// Mirror of Meridian.Contracts.Workstation.WorkstationCrossScreenCountsDto (A3). A count is null
// when its source is not registered or the caller may not read it; render "—", never a substitute.

export interface WorkstationCrossScreenCount {
  count: number;
  definition: string;
}

export interface WorkstationProviderHealthCount {
  healthy: number;
  total: number;
  definition: string;
}

export interface WorkstationCrossScreenCounts {
  asOf: string;
  openOrders: WorkstationCrossScreenCount | null;
  providers: WorkstationProviderHealthCount | null;
  openBreaks: WorkstationCrossScreenCount | null;
}

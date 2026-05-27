export type PriceAlertCondition = "above" | "below" | "crosses-up" | "crosses-down";

export type PriceAlertField = "last" | "bid" | "ask" | "mid";

export interface PriceAlert {
  id: string;
  symbol: string;
  condition: PriceAlertCondition;
  field: PriceAlertField;
  threshold: number;
  note: string | null;
  createdAt: string;
  snoozedUntil: string | null;
  enabled: boolean;
  triggeredAt: string | null;
  lastObservedPrice: number | null;
  lastObservedAt: string | null;
}

export interface PriceAlertTrigger {
  id: string;
  alertId: string;
  symbol: string;
  condition: PriceAlertCondition;
  field: PriceAlertField;
  threshold: number;
  triggeredPrice: number;
  triggeredAt: string;
  acknowledged: boolean;
  note: string | null;
}

export interface PriceAlertDraft {
  symbol: string;
  condition: PriceAlertCondition;
  field: PriceAlertField;
  threshold: number;
  note?: string | null;
}

export interface PriceAlertStorageState {
  version: 1;
  alerts: PriceAlert[];
  triggers: PriceAlertTrigger[];
}

export const PRICE_ALERT_STORAGE_KEY = "meridian.priceAlerts.v1";
export const PRICE_ALERT_MAX_TRIGGER_HISTORY = 200;

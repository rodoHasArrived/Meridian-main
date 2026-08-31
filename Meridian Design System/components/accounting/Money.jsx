// Meridian `Money` carrier — exposes the money.js arithmetic/formatting helpers on the
// namespace (they're lowercase, so unreachable directly). Same pattern as TableHooks/FormHooks:
//   const { allocateAmount, roundMoney } = window.MeridianDesignSystem_4f61be.Money;
// These are the exact functions the accounting components use internally, so consumer-computed
// figures always agree with what AmountCell / LedgerTable / TrialBalance render.
import {
  formatMoney,
  formatMoneyCompact,
  formatQuantity,
  formatBps,
  roundMoney,
  allocateAmount,
  convertAmount,
  sumAmounts,
  toNumber,
  currencySymbol,
  currencyDecimals,
  accountNormalSide,
  daysBetween,
  agingBucketIndex,
  buildTrialBalance,
  amortizeStraightLine,
  fxRevalue,
} from "./money";

export const Money = {
  formatMoney,
  formatMoneyCompact,
  formatQuantity,
  formatBps,
  roundMoney,
  allocateAmount,
  convertAmount,
  sumAmounts,
  toNumber,
  currencySymbol,
  currencyDecimals,
  accountNormalSide,
  daysBetween,
  agingBucketIndex,
  buildTrialBalance,
  amortizeStraightLine,
  fxRevalue,
};

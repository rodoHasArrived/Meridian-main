// Meridian `Money` carrier — exposes the money arithmetic and the money formatters on the
// namespace (they're lowercase, so unreachable directly). Same pattern as TableHooks/FormHooks:
//   const { allocateAmount, roundMoney } = window.MeridianDesignSystem_205d17.Money;
// Two halves, one carrier: the arithmetic lives in `accounting/money.js`, the formatting in
// `core/format.js` (the numeric authority). These are the exact functions the accounting components
// use internally, so consumer-computed figures always agree with what AmountCell / LedgerTable /
// TrialBalance render. Formatting a number anywhere else through `Intl` is a governance violation.
import {
  formatMoney,
  formatMoneyCompact,
  formatQuantity,
  formatBps,
  toNumber,
  currencySymbol,
  currencyDecimals,
} from "../core/format.js";
import {
  setDefaultLocale,
  getDefaultLocale,
  roundMoney,
  allocateAmount,
  convertAmount,
  sumAmounts,
  accountNormalSide,
  daysBetween,
  agingBucketIndex,
  buildTrialBalance,
  amortizeStraightLine,
  fxRevalue,
  effectiveYieldSchedule,
} from "./money";

export const Money = {
  formatMoney,
  formatMoneyCompact,
  formatQuantity,
  setDefaultLocale,
  getDefaultLocale,
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
  effectiveYieldSchedule,
};

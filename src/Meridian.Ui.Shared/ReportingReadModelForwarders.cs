using System.Runtime.CompilerServices;
using Meridian.Ui.Shared.Services;

// Preserve legacy assembly-qualified public identities while the owning readers build in
// an independent domain assembly. No duplicate implementation remains in Ui.Shared.
[assembly: TypeForwardedTo(typeof(ReportingIncomeComparisonService))]
[assembly: TypeForwardedTo(typeof(ReportAccessQueryContext))]
[assembly: TypeForwardedTo(typeof(ReportAccessPolicyEvaluator))]
[assembly: TypeForwardedTo(typeof(LedgerDisposalTaxReadService))]
[assembly: TypeForwardedTo(typeof(LedgerReportingPartnersCapitalSource))]
[assembly: TypeForwardedTo(typeof(ReportingLedgerPopulationSnapshot))]
[assembly: TypeForwardedTo(typeof(ReportAmountBindingBuilder))]
[assembly: TypeForwardedTo(typeof(ReportPeriodRange))]

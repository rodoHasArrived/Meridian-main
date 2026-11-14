using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static LedgerPeriodPnlSummaryDto BuildPnlSummary(LedgerPeriodSummaryDto summary)
    {
        var revenueLines = summary.TrialBalance
            .Where(static row => string.Equals(row.AccountType, "Revenue", StringComparison.Ordinal))
            .ToArray();
        var expenseLines = summary.TrialBalance
            .Where(static row => string.Equals(row.AccountType, "Expense", StringComparison.Ordinal))
            .ToArray();
        var accrualAdjustmentLines = revenueLines
            .Concat(expenseLines)
            .Where(IsAccrualAdjustmentLine)
            .ToArray();
        var realizedRevenue = revenueLines
            .Where(static row => !IsAccrualAdjustmentLine(row))
            .Sum(static row => row.Balance);
        var realizedExpenses = expenseLines
            .Where(static row => !IsAccrualAdjustmentLine(row))
            .Sum(static row => row.Balance);
        var accrualAdjustmentRevenue = accrualAdjustmentLines
            .Where(static row => string.Equals(row.AccountType, "Revenue", StringComparison.Ordinal))
            .Sum(static row => row.Balance);
        var accrualAdjustmentExpenses = accrualAdjustmentLines
            .Where(static row => string.Equals(row.AccountType, "Expense", StringComparison.Ordinal))
            .Sum(static row => row.Balance);

        return new LedgerPeriodPnlSummaryDto(
            summary.PeriodId,
            summary.LedgerBookId,
            summary.FiscalYear,
            summary.PeriodNo,
            summary.Label,
            TotalRevenue: revenueLines.Sum(static row => row.Balance),
            TotalExpenses: expenseLines.Sum(static row => row.Balance),
            summary.NetIncome,
            summary.PeriodOnPeriodVariance,
            summary.OpenBreakCount,
            summary.SignoffStatus,
            summary.CompletedAt,
            revenueLines,
            expenseLines,
            summary.AccountingBasis,
            summary.AccountingPolicyId,
            summary.AccountingPolicyVersion,
            RealizedRevenue: realizedRevenue,
            RealizedExpenses: realizedExpenses,
            RealizedNetIncome: realizedRevenue - realizedExpenses,
            AccrualAdjustmentRevenue: accrualAdjustmentRevenue,
            AccrualAdjustmentExpenses: accrualAdjustmentExpenses,
            AccrualBasisAdjustmentNetImpact: accrualAdjustmentRevenue - accrualAdjustmentExpenses,
            AccrualAdjustmentLines: accrualAdjustmentLines);
    }

    private static bool IsAccrualAdjustmentLine(LedgerPeriodTrialBalanceLineDto row)
        => ContainsAccrualMarker(row.AccountName)
           || ContainsAccrualMarker(row.RuleId)
           || ContainsAccrualMarker(row.RuleVersion)
           || ContainsAccrualMarker(row.SourceEventId);

    private static LedgerTrialBalanceReportDto BuildTrialBalanceReport(LedgerPeriodSummaryDto summary, HttpContext context)
    {
        var signedAtUtc = DateTimeOffset.UtcNow;
        var actor = TryResolveActor(context, out var resolvedActor) ? resolvedActor : "system";
        var lines = summary.TrialBalance
            .Select(static row => row with { Dimensions = CanonicalizeDimensions(row.Dimensions) })
            .OrderBy(static row => row.AccountType, StringComparer.Ordinal)
            .ThenBy(static row => row.AccountName, StringComparer.Ordinal)
            .ThenBy(static row => row.Symbol, StringComparer.Ordinal)
            .ThenBy(static row => row.FinancialAccountId, StringComparer.Ordinal)
            .ThenBy(static row => BuildDimensionSignature(row.Dimensions), StringComparer.Ordinal)
            .ToArray();
        var reportSummary = summary with
        {
            TrialBalance = lines,
            TotalDebits = lines.Sum(static row => row.DebitTotal),
            TotalCredits = lines.Sum(static row => row.CreditTotal),
            NetIncome = CalculateNetIncome(lines)
        };
        var signature = new LedgerReportSignatureDto(
            "SHA256",
            ComputeTrialBalanceReportChecksum(reportSummary, lines, BuildDimensionReportFilter(context.Request.Query)),
            actor,
            signedAtUtc);

        return new LedgerTrialBalanceReportDto(
            reportSummary.PeriodId,
            reportSummary.LedgerBookId,
            reportSummary.FiscalYear,
            reportSummary.PeriodNo,
            reportSummary.Label,
            IsPeriodLocked: true,
            reportSummary.TotalDebits,
            reportSummary.TotalCredits,
            reportSummary.NetIncome,
            reportSummary.PeriodOnPeriodVariance,
            reportSummary.OpenBreakCount,
            reportSummary.SignoffStatus,
            reportSummary.CompletedAt,
            lines,
            signature,
            reportSummary.AccountingBasis,
            reportSummary.AccountingPolicyId,
            reportSummary.AccountingPolicyVersion);
    }

    private static string ComputeTrialBalanceReportChecksum(
        LedgerPeriodSummaryDto summary,
        IReadOnlyList<LedgerPeriodTrialBalanceLineDto> lines,
        LedgerDimensionReportFilter dimensionFilter)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ledger-trial-balance-report-v1");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-id,{summary.PeriodId:D}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ledger-book-id,{summary.LedgerBookId:D}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"fiscal-year,{summary.FiscalYear}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-no,{summary.PeriodNo}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"label,{EscapeSignatureField(summary.Label)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"total-debits,{FormatSignatureDecimal(summary.TotalDebits)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"total-credits,{FormatSignatureDecimal(summary.TotalCredits)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"net-income,{FormatSignatureDecimal(summary.NetIncome)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-variance,{FormatSignatureDecimal(summary.PeriodOnPeriodVariance ?? 0m)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"open-break-count,{summary.OpenBreakCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"accounting-basis,{summary.AccountingBasis}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"accounting-policy-id,{EscapeSignatureField(summary.AccountingPolicyId)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"accounting-policy-version,{EscapeSignatureField(summary.AccountingPolicyVersion)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"dimension-filter,{EscapeSignatureField(BuildDimensionFilterSignature(dimensionFilter))}");
        builder.AppendLine("account-name,account-type,symbol,financial-account-id,debits,credits,balance,entry-count,rule-id,rule-version,source-event-id,source-journal-entry-id,dimensions");

        foreach (var line in lines)
        {
            builder.Append(EscapeSignatureField(line.AccountName));
            builder.Append(',');
            builder.Append(line.AccountType);
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.Symbol ?? string.Empty));
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.FinancialAccountId ?? string.Empty));
            builder.Append(',');
            builder.Append(FormatSignatureDecimal(line.DebitTotal));
            builder.Append(',');
            builder.Append(FormatSignatureDecimal(line.CreditTotal));
            builder.Append(',');
            builder.Append(FormatSignatureDecimal(line.Balance));
            builder.Append(',');
            builder.Append(line.EntryCount);
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.RuleId ?? string.Empty));
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.RuleVersion ?? string.Empty));
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.SourceEventId ?? string.Empty));
            builder.Append(',');
            builder.Append(line.SourceJournalEntryId?.ToString("D") ?? string.Empty);
            builder.Append(',');
            builder.AppendLine(EscapeSignatureField(BuildDimensionSignature(line.Dimensions)));
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    private static LedgerLineDimensionSet? ToLineDimensionSet(LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return null;
        }

        var instrumentId = Guid.TryParse(filter.InstrumentId, out var parsedInstrumentId)
            ? parsedInstrumentId
            : (Guid?)null;
        var positionId = Guid.TryParse(filter.PositionId, out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        return new LedgerLineDimensionSet(
            FundId: filter.FundId,
            EntityId: filter.EntityId,
            SleeveId: filter.SleeveId,
            StrategyId: filter.StrategyId,
            InvestorId: filter.InvestorId,
            CapitalAccountId: filter.CapitalAccountId,
            InstrumentId: instrumentId,
            TaxLotId: filter.TaxLotId,
            CostCenterId: filter.CostCenterId,
            CounterpartyId: filter.CounterpartyId,
            ExternalGlDimensions: filter.ExternalGlDimensions,
            OrganizationId: filter.OrganizationId,
            PortfolioId: filter.PortfolioId,
            BookId: filter.BookId,
            AccountId: filter.AccountId,
            CustomerId: filter.CustomerId,
            VendorId: filter.VendorId,
            ProjectId: filter.ProjectId)
        {
            PositionId = positionId,
            AccountingArrangementId = filter.AccountingArrangementId,
            AccountingArrangementRevisionId = filter.AccountingArrangementRevisionId,
            ChartAccountNodeId = filter.ChartAccountNodeId,
            AccountBindingRevisionId = filter.AccountBindingRevisionId,
            IntercompanyTransactionId = filter.IntercompanyTransactionId,
            IntercompanyAgreementId = Guid.TryParse(filter.IntercompanyAgreementId, out var agreementId) ? agreementId : null,
            IntercompanyAgreementVersion = int.TryParse(filter.IntercompanyAgreementVersion, out var agreementVersion) ? agreementVersion : null
        };
    }

    private static LedgerDimensionSetDto? ToLedgerDimensionSetDto(LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return null;
        }

        var instrumentId = Guid.TryParse(filter.InstrumentId, out var parsedInstrumentId)
            ? parsedInstrumentId
            : (Guid?)null;
        var positionId = Guid.TryParse(filter.PositionId, out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        return new LedgerDimensionSetDto(
            FundId: filter.FundId,
            EntityId: filter.EntityId,
            SleeveId: filter.SleeveId,
            StrategyId: filter.StrategyId,
            InvestorId: filter.InvestorId,
            CapitalAccountId: filter.CapitalAccountId,
            InstrumentId: instrumentId,
            TaxLotId: filter.TaxLotId,
            CostCenterId: filter.CostCenterId,
            CounterpartyId: filter.CounterpartyId,
            ExternalGlDimensions: filter.ExternalGlDimensions,
            OrganizationId: filter.OrganizationId,
            PortfolioId: filter.PortfolioId,
            BookId: filter.BookId,
            AccountId: filter.AccountId,
            CustomerId: filter.CustomerId,
            VendorId: filter.VendorId,
            ProjectId: filter.ProjectId)
        {
            PositionId = positionId,
            AccountingArrangementId = filter.AccountingArrangementId,
            AccountingArrangementRevisionId = filter.AccountingArrangementRevisionId,
            ChartAccountNodeId = filter.ChartAccountNodeId,
            AccountBindingRevisionId = filter.AccountBindingRevisionId,
            IntercompanyTransactionId = filter.IntercompanyTransactionId,
            IntercompanyAgreementId = Guid.TryParse(filter.IntercompanyAgreementId, out var agreementId) ? agreementId : null,
            IntercompanyAgreementVersion = int.TryParse(filter.IntercompanyAgreementVersion, out var agreementVersion) ? agreementVersion : null
        };
    }

    private static string? GetQueryValue(IQueryCollection query, string key)
        => query.TryGetValue(key, out var value) ? value.ToString() : null;

    private static string? GetFirstQueryValue(IQueryCollection query, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetQueryValue(query, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static LedgerPeriodSummaryDto ApplyDimensionFilter(
        LedgerPeriodSummaryDto summary,
        LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return summary;
        }

        var lines = summary.TrialBalance
            .Where(row => MatchesDimensionFilter(row.Dimensions, filter))
            .ToArray();
        return summary with
        {
            TrialBalance = lines,
            TotalDebits = lines.Sum(static row => row.DebitTotal),
            TotalCredits = lines.Sum(static row => row.CreditTotal),
            NetIncome = CalculateNetIncome(lines),
            PeriodOnPeriodVariance = null
        };
    }

    private static IReadOnlyList<LedgerJournalEntryDto> BuildJournalEntryDtos(
        IReadOnlyList<LedgerJournalEntryRecord> entries,
        Func<LedgerJournalEntryRecord, Guid?> resolveLedgerBookId,
        LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        return entries
            .OrderBy(static entry => entry.GlobalSequence)
            .Select(entry => BuildJournalEntryDto(entry, resolveLedgerBookId(entry), filter))
            .Where(static entry => entry is not null)
            .Select(static entry => entry!)
            .ToArray();
    }

    private static LedgerJournalEntryDto? BuildJournalEntryDto(
        LedgerJournalEntryRecord record,
        Guid? ledgerBookId,
        LedgerDimensionReportFilter filter)
    {
        var entryDimensions = BuildDimensions(record.Entry.Metadata);
        var lines = record.Entry.Lines
            .Select(line => BuildJournalEntryLineDto(
                line,
                BuildDimensions(line.Dimensions)
                ?? BuildDimensions(record.Entry.Metadata, line.EntryId)
                ?? entryDimensions))
            .Where(line => MatchesDimensionFilter(line.Dimensions, filter))
            .ToArray();
        if (filter.HasCriteria && lines.Length == 0)
        {
            return null;
        }

        return new LedgerJournalEntryDto(
            record.Entry.JournalEntryId,
            record.PeriodId,
            ledgerBookId,
            record.AggregateId,
            record.CommandId,
            record.CorrelationId,
            record.GlobalSequence,
            record.CreatedAt,
            record.Entry.Timestamp,
            record.Entry.Description,
            lines.Sum(static line => line.Debit),
            lines.Sum(static line => line.Credit),
            Math.Abs(lines.Sum(static line => line.Debit) - lines.Sum(static line => line.Credit)) <= LedgerToleranceConstants.Balance,
            lines,
            record.AccountingBasis,
            record.AccountingPolicyId,
            record.AccountingPolicyVersion,
            record.RuleId,
            record.RuleVersion,
            record.SourceEventId,
            record.SourceJournalEntryId,
            record.PostingKind,
            record.AdjustmentApproval);
    }

    private static LedgerJournalEntryLineDto BuildJournalEntryLineDto(
        LedgerEntry line,
        LedgerDimensionSetDto? dimensions)
        => new(
            line.EntryId,
            line.JournalEntryId,
            line.Timestamp,
            line.Account.Name,
            line.Account.AccountType.ToString(),
            line.Account.Symbol,
            line.Account.FinancialAccountId,
            line.Debit,
            line.Credit,
            line.Description,
            dimensions);

    private static LedgerDimensionSetDto? BuildDimensions(JournalEntryMetadata metadata)
    {
        var tags = metadata.Tags;
        var positionId = Guid.TryParse(LedgerDimensionTags.FirstTag(tags, "positionId"), out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        var dimensions = new LedgerDimensionSetDto(
            FundId: LedgerDimensionTags.FirstTag(tags, "fundId", "fundProfileId"),
            EntityId: LedgerDimensionTags.FirstTag(tags, "entityId", "legalEntityId"),
            SleeveId: LedgerDimensionTags.FirstTag(tags, "sleeveId"),
            StrategyId: metadata.StrategyId ?? LedgerDimensionTags.FirstTag(tags, "strategyId"),
            InvestorId: metadata.InvestorId ?? LedgerDimensionTags.FirstTag(tags, "investorId"),
            CapitalAccountId: metadata.CapitalAccountId ?? LedgerDimensionTags.FirstTag(tags, "capitalAccountId"),
            InstrumentId: metadata.SecurityId,
            TaxLotId: LedgerDimensionTags.FirstTag(tags, "taxLotId", "lotId"),
            CostCenterId: LedgerDimensionTags.FirstTag(tags, "costCenterId"),
            CounterpartyId: metadata.CounterpartyAccountId ?? LedgerDimensionTags.FirstTag(tags, "counterpartyId", "counterpartyAccountId"),
            ExternalGlDimensions: LedgerDimensionTags.ExtractExternalGlDimensions(tags),
            OrganizationId: LedgerDimensionTags.FirstTag(tags, "organizationId"),
            PortfolioId: LedgerDimensionTags.FirstTag(tags, "portfolioId"),
            BookId: metadata.LedgerBook ?? LedgerDimensionTags.FirstTag(tags, "bookId"),
            AccountId: metadata.FinancialAccountId ?? LedgerDimensionTags.FirstTag(tags, "accountId"),
            CustomerId: LedgerDimensionTags.FirstTag(tags, "customerId"),
            VendorId: LedgerDimensionTags.FirstTag(tags, "vendorId"),
            ProjectId: metadata.ProjectId ?? LedgerDimensionTags.FirstTag(tags, "projectId"))
        {
            PositionId = positionId,
            AccountingArrangementId = LedgerDimensionTags.FirstTag(tags, "accountingArrangementId"),
            AccountingArrangementRevisionId = LedgerDimensionTags.FirstTag(tags, "accountingArrangementRevisionId"),
            ChartAccountNodeId = LedgerDimensionTags.FirstTag(tags, "chartAccountNodeId"),
            AccountBindingRevisionId = LedgerDimensionTags.FirstTag(tags, "accountBindingRevisionId"),
            IntercompanyTransactionId = LedgerDimensionTags.FirstTag(tags, "intercompanyTransactionId"),
            IntercompanyAgreementId = LedgerAgreementDimensions.ReadId(tags),
            IntercompanyAgreementVersion = LedgerAgreementDimensions.ReadVersion(tags)
        };

        return CanonicalizeDimensions(dimensions);
    }

    private static LedgerDimensionSetDto? BuildDimensions(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var result = new LedgerDimensionSetDto(
            FundId: dimensions.FundId,
            EntityId: dimensions.EntityId,
            SleeveId: dimensions.SleeveId,
            StrategyId: dimensions.StrategyId,
            InvestorId: dimensions.InvestorId,
            CapitalAccountId: dimensions.CapitalAccountId,
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: dimensions.TaxLotId,
            CostCenterId: dimensions.CostCenterId,
            CounterpartyId: dimensions.CounterpartyId,
            ExternalGlDimensions: dimensions.ExternalGlDimensions,
            OrganizationId: dimensions.OrganizationId,
            PortfolioId: dimensions.PortfolioId,
            BookId: dimensions.BookId,
            AccountId: dimensions.AccountId,
            CustomerId: dimensions.CustomerId,
            VendorId: dimensions.VendorId,
            ProjectId: dimensions.ProjectId)
        {
            PositionId = dimensions.PositionId,
            AccountingArrangementId = dimensions.AccountingArrangementId,
            AccountingArrangementRevisionId = dimensions.AccountingArrangementRevisionId,
            ChartAccountNodeId = dimensions.ChartAccountNodeId,
            AccountBindingRevisionId = dimensions.AccountBindingRevisionId,
            IntercompanyTransactionId = dimensions.IntercompanyTransactionId,
            IntercompanyAgreementId = dimensions.IntercompanyAgreementId,
            IntercompanyAgreementVersion = dimensions.IntercompanyAgreementVersion
        };

        return CanonicalizeDimensions(result);
    }

    private static LedgerDimensionSetDto? BuildDimensions(JournalEntryMetadata metadata, Guid lineEntryId)
    {
        var tags = metadata.Tags;
        var prefix = $"lineDimensions.{lineEntryId:N}.";
        if (tags is null || tags.Keys.All(key => !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var positionId = Guid.TryParse(LedgerDimensionTags.FirstTag(tags, prefix + "positionId"), out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        var dimensions = new LedgerDimensionSetDto(
            FundId: LedgerDimensionTags.FirstTag(tags, prefix + "fundId"),
            EntityId: LedgerDimensionTags.FirstTag(tags, prefix + "entityId"),
            SleeveId: LedgerDimensionTags.FirstTag(tags, prefix + "sleeveId"),
            StrategyId: LedgerDimensionTags.FirstTag(tags, prefix + "strategyId"),
            InvestorId: LedgerDimensionTags.FirstTag(tags, prefix + "investorId"),
            CapitalAccountId: LedgerDimensionTags.FirstTag(tags, prefix + "capitalAccountId"),
            InstrumentId: Guid.TryParse(LedgerDimensionTags.FirstTag(tags, prefix + "instrumentId"), out var instrumentId) ? instrumentId : null,
            TaxLotId: LedgerDimensionTags.FirstTag(tags, prefix + "taxLotId"),
            CostCenterId: LedgerDimensionTags.FirstTag(tags, prefix + "costCenterId"),
            CounterpartyId: LedgerDimensionTags.FirstTag(tags, prefix + "counterpartyId"),
            ExternalGlDimensions: LedgerDimensionTags.ExtractExternalGlDimensions(tags, prefix),
            OrganizationId: LedgerDimensionTags.FirstTag(tags, prefix + "organizationId"),
            PortfolioId: LedgerDimensionTags.FirstTag(tags, prefix + "portfolioId"),
            BookId: LedgerDimensionTags.FirstTag(tags, prefix + "bookId"),
            AccountId: LedgerDimensionTags.FirstTag(tags, prefix + "accountId"),
            CustomerId: LedgerDimensionTags.FirstTag(tags, prefix + "customerId"),
            VendorId: LedgerDimensionTags.FirstTag(tags, prefix + "vendorId"),
            ProjectId: LedgerDimensionTags.FirstTag(tags, prefix + "projectId"))
        {
            PositionId = positionId,
            AccountingArrangementId = LedgerDimensionTags.FirstTag(tags, prefix + "accountingArrangementId"),
            AccountingArrangementRevisionId = LedgerDimensionTags.FirstTag(tags, prefix + "accountingArrangementRevisionId"),
            ChartAccountNodeId = LedgerDimensionTags.FirstTag(tags, prefix + "chartAccountNodeId"),
            AccountBindingRevisionId = LedgerDimensionTags.FirstTag(tags, prefix + "accountBindingRevisionId"),
            IntercompanyTransactionId = LedgerDimensionTags.FirstTag(tags, prefix + "intercompanyTransactionId"),
            IntercompanyAgreementId = LedgerAgreementDimensions.ReadId(tags, prefix),
            IntercompanyAgreementVersion = LedgerAgreementDimensions.ReadVersion(tags, prefix)
        };

        return CanonicalizeDimensions(dimensions);
    }

    private static decimal CalculateNetIncome(IReadOnlyList<LedgerPeriodTrialBalanceLineDto> lines)
        => lines.Sum(static row =>
            row.AccountType switch
            {
                "Revenue" => row.Balance,
                "Expense" => -row.Balance,
                _ => 0m
            });

    private static bool MatchesDimensionFilter(
        LedgerDimensionSetDto? dimensions,
        LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return true;
        }

        dimensions = CanonicalizeDimensions(dimensions);
        if (dimensions is null)
        {
            return false;
        }

        return Matches(filter.FundId, dimensions.FundId)
               && Matches(filter.EntityId, dimensions.EntityId)
               && Matches(filter.SleeveId, dimensions.SleeveId)
               && Matches(filter.StrategyId, dimensions.StrategyId)
               && Matches(filter.InvestorId, dimensions.InvestorId)
               && Matches(filter.CapitalAccountId, dimensions.CapitalAccountId)
               && Matches(filter.InstrumentId, dimensions.InstrumentId?.ToString("D"))
               && Matches(filter.PositionId, dimensions.PositionId?.ToString("D"))
               && Matches(filter.AccountingArrangementId, dimensions.AccountingArrangementId)
               && Matches(filter.AccountingArrangementRevisionId, dimensions.AccountingArrangementRevisionId)
               && Matches(filter.ChartAccountNodeId, dimensions.ChartAccountNodeId)
               && Matches(filter.AccountBindingRevisionId, dimensions.AccountBindingRevisionId)
               && Matches(filter.IntercompanyTransactionId, dimensions.IntercompanyTransactionId)
               && Matches(filter.IntercompanyAgreementId, dimensions.IntercompanyAgreementId?.ToString("D"))
               && Matches(filter.IntercompanyAgreementVersion, dimensions.IntercompanyAgreementVersion?.ToString(CultureInfo.InvariantCulture))
               && Matches(filter.TaxLotId, dimensions.TaxLotId)
               && Matches(filter.CostCenterId, dimensions.CostCenterId)
               && Matches(filter.CounterpartyId, dimensions.CounterpartyId)
               && Matches(filter.OrganizationId, dimensions.OrganizationId)
               && Matches(filter.PortfolioId, dimensions.PortfolioId)
               && Matches(filter.BookId, dimensions.BookId)
               && Matches(filter.AccountId, dimensions.AccountId)
               && Matches(filter.CustomerId, dimensions.CustomerId)
               && Matches(filter.VendorId, dimensions.VendorId)
               && Matches(filter.ProjectId, dimensions.ProjectId)
               && MatchesExternalGlDimensions(filter.ExternalGlDimensions, dimensions.ExternalGlDimensions);
    }

    private static bool Matches(string? expected, string? actual)
        => expected is null || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesExternalGlDimensions(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        foreach (var pair in expected)
        {
            if (!actual.TryGetValue(pair.Key, out var actualValue) ||
                !string.Equals(pair.Value, actualValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatSignatureDecimal(decimal value)
        => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string EscapeSignatureField(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool IsValidDateRange(DateOnly? startDate, DateOnly? endDate)
        => !startDate.HasValue || !endDate.HasValue || startDate.Value <= endDate.Value;

    private static LedgerDimensionReportFilter CanonicalizeFilter(LedgerDimensionReportFilter filter)
        => new(
            FundId: NormalizeOptional(filter.FundId),
            EntityId: NormalizeOptional(filter.EntityId),
            SleeveId: NormalizeOptional(filter.SleeveId),
            StrategyId: NormalizeOptional(filter.StrategyId),
            InvestorId: NormalizeOptional(filter.InvestorId),
            CapitalAccountId: NormalizeOptional(filter.CapitalAccountId),
            InstrumentId: NormalizeOptional(filter.InstrumentId),
            PositionId: NormalizeOptional(filter.PositionId),
            TaxLotId: NormalizeOptional(filter.TaxLotId),
            CostCenterId: NormalizeOptional(filter.CostCenterId),
            CounterpartyId: NormalizeOptional(filter.CounterpartyId),
            OrganizationId: NormalizeOptional(filter.OrganizationId),
            PortfolioId: NormalizeOptional(filter.PortfolioId),
            BookId: NormalizeOptional(filter.BookId),
            AccountId: NormalizeOptional(filter.AccountId),
            CustomerId: NormalizeOptional(filter.CustomerId),
            VendorId: NormalizeOptional(filter.VendorId),
            ProjectId: NormalizeOptional(filter.ProjectId),
            ExternalGlDimensions: NormalizeExternalGlDimensions(filter.ExternalGlDimensions))
        {
            AccountingArrangementId = NormalizeOptional(filter.AccountingArrangementId),
            AccountingArrangementRevisionId = NormalizeOptional(filter.AccountingArrangementRevisionId),
            ChartAccountNodeId = NormalizeOptional(filter.ChartAccountNodeId),
            AccountBindingRevisionId = NormalizeOptional(filter.AccountBindingRevisionId),
            IntercompanyTransactionId = NormalizeOptional(filter.IntercompanyTransactionId),
            IntercompanyAgreementId = NormalizeOptional(filter.IntercompanyAgreementId),
            IntercompanyAgreementVersion = NormalizeOptional(filter.IntercompanyAgreementVersion)
        };

    private static LedgerDimensionSetDto? CanonicalizeDimensions(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var canonical = new LedgerDimensionSetDto(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: NormalizeExternalGlDimensions(dimensions.ExternalGlDimensions),
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId,
            AccountingArrangementId = dimensions.AccountingArrangementId,
            AccountingArrangementRevisionId = dimensions.AccountingArrangementRevisionId,
            ChartAccountNodeId = dimensions.ChartAccountNodeId,
            AccountBindingRevisionId = dimensions.AccountBindingRevisionId,
            IntercompanyTransactionId = dimensions.IntercompanyTransactionId,
            IntercompanyAgreementId = dimensions.IntercompanyAgreementId,
            IntercompanyAgreementVersion = dimensions.IntercompanyAgreementVersion
        };

        return LedgerDimensionTags.HasAnyDimension(canonical) ? canonical : null;
    }
}

using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Security Master coverage helpers for the workstation API surface: builds coverage
/// payloads, resolved/missing security references, position-to-security lookups, and
/// coverage review predicates. Split out of the WorkstationEndpoints core partial.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static WorkstationSecurityCoveragePayload BuildSecurityCoverage(StrategyRunDetail? detail)
    {
        var portfolio = detail?.Portfolio;
        var ledger = detail?.Ledger;
        var portfolioResolved = portfolio?.SecurityResolvedCount ?? 0;
        var portfolioMissing = portfolio?.SecurityMissingCount ?? 0;
        var ledgerResolved = ledger?.SecurityResolvedCount ?? 0;
        var ledgerMissing = ledger?.SecurityMissingCount ?? 0;
        var hasIssues = portfolioMissing > 0 || ledgerMissing > 0;
        var resolvedReferences = BuildResolvedSecurityReferences(detail);
        var missingReferences = BuildMissingSecurityReferences(detail);
        var resolvedCount = portfolioResolved + ledgerResolved;
        var missingCount = portfolioMissing + ledgerMissing;

        return new WorkstationSecurityCoveragePayload(
            PortfolioResolved: portfolioResolved,
            PortfolioMissing: portfolioMissing,
            LedgerResolved: ledgerResolved,
            LedgerMissing: ledgerMissing,
            HasIssues: hasIssues,
            Tone: hasIssues ? "warning" : resolvedCount > 0 ? "success" : "default",
            Summary: missingCount > 0
                ? $"{resolvedCount} references mapped, {missingCount} unresolved."
                : resolvedCount > 0
                    ? $"{resolvedCount} references mapped with no unresolved symbols."
                    : "Security Master coverage not yet evaluated.",
            ResolvedReferences: resolvedReferences,
            MissingReferences: missingReferences);
    }

    private static WorkstationSecurityCoverageReferencePayload[] BuildResolvedSecurityReferences(StrategyRunDetail? detail)
    {
        if (detail is null)
        {
            return [];
        }

        var results = new List<WorkstationSecurityCoverageReferencePayload>();

        if (detail.Portfolio is not null)
        {
            results.AddRange(
                detail.Portfolio.Positions
                    .Where(static position => position.Security is not null)
                    .Select(static position => new WorkstationSecurityCoverageReferencePayload(
                        Source: "portfolio",
                        Symbol: position.Symbol,
                        AccountName: null,
                        SecurityId: position.Security!.SecurityId.ToString("N"),
                        DisplayName: position.Security.DisplayName,
                        AssetClass: position.Security.AssetClass,
                        SubType: position.Security.SubType,
                        Currency: position.Security.Currency,
                        Status: position.Security.Status.ToString(),
                        PrimaryIdentifier: position.Security.PrimaryIdentifier,
                        CoverageStatus: position.Security.CoverageStatus.ToString(),
                        CoverageReason: position.Security.ResolutionReason,
                        MatchedIdentifierKind: position.Security.MatchedIdentifierKind,
                        MatchedIdentifierValue: position.Security.MatchedIdentifierValue,
                        MatchedProvider: position.Security.MatchedProvider)));
        }

        if (detail.Ledger is not null)
        {
            results.AddRange(
                detail.Ledger.TrialBalance
                    .Where(static line => line.Security is not null && !string.IsNullOrWhiteSpace(line.Symbol))
                    .Select(static line => new WorkstationSecurityCoverageReferencePayload(
                        Source: "ledger",
                        Symbol: line.Symbol!,
                        AccountName: line.AccountName,
                        SecurityId: line.Security!.SecurityId.ToString("N"),
                        DisplayName: line.Security.DisplayName,
                        AssetClass: line.Security.AssetClass,
                        SubType: line.Security.SubType,
                        Currency: line.Security.Currency,
                        Status: line.Security.Status.ToString(),
                        PrimaryIdentifier: line.Security.PrimaryIdentifier,
                        CoverageStatus: line.Security.CoverageStatus.ToString(),
                        CoverageReason: line.Security.ResolutionReason,
                        MatchedIdentifierKind: line.Security.MatchedIdentifierKind,
                        MatchedIdentifierValue: line.Security.MatchedIdentifierValue,
                        MatchedProvider: line.Security.MatchedProvider)));
        }

        return results
            .DistinctBy(static item => $"{item.Source}|{item.Symbol}|{item.AccountName}|{item.SecurityId}", StringComparer.OrdinalIgnoreCase)
            .Take(SecurityCoveragePreviewLimit)
            .ToArray();
    }

    private static WorkstationSecurityCoverageGapPayload[] BuildMissingSecurityReferences(StrategyRunDetail? detail)
    {
        if (detail is null)
        {
            return [];
        }

        var results = new List<WorkstationSecurityCoverageGapPayload>();

        if (detail.Portfolio is not null)
        {
            results.AddRange(
                detail.Portfolio.Positions
                    .Where(static position => position.Security is null && !string.IsNullOrWhiteSpace(position.Symbol))
                    .Select(static position => new WorkstationSecurityCoverageGapPayload(
                        Source: "portfolio",
                        Symbol: position.Symbol,
                        AccountName: null,
                        Reason: "Portfolio position is missing a Security Master match.")));
        }

        if (detail.Ledger is not null)
        {
            results.AddRange(
                detail.Ledger.TrialBalance
                    .Where(static line => line.Security is null && !string.IsNullOrWhiteSpace(line.Symbol))
                    .Select(static line => new WorkstationSecurityCoverageGapPayload(
                        Source: "ledger",
                        Symbol: line.Symbol!,
                        AccountName: line.AccountName,
                        Reason: "Ledger coverage is missing a Security Master match.")));
        }

        return results
            .DistinctBy(static item => $"{item.Source}|{item.Symbol}|{item.AccountName}", StringComparer.OrdinalIgnoreCase)
            .Take(SecurityCoveragePreviewLimit)
            .ToArray();
    }

    private static Dictionary<string, WorkstationSecurityReference?> BuildPositionSecurityLookup(StrategyRunDetail? detail)
        => detail?.Portfolio?.Positions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
            .GroupBy(static position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Security, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, WorkstationSecurityReference?>(StringComparer.OrdinalIgnoreCase);

    private static object BuildTradingPositionPayload(
        string symbol,
        string side,
        string quantity,
        string averagePrice,
        string markPrice,
        string dayPnl,
        string unrealizedPnl,
        string exposure,
        WorkstationSecurityReference? security)
        => new
        {
            symbol,
            side,
            quantity,
            averagePrice,
            markPrice,
            dayPnl,
            unrealizedPnl,
            exposure,
            security = BuildInlineSecurityReference(symbol, security)
        };

    private static object? BuildInlineSecurityReference(string symbol, WorkstationSecurityReference? security)
    {
        if (security is null)
        {
            return null;
        }

        return new
        {
            securityId = security.SecurityId == Guid.Empty ? null : security.SecurityId.ToString("N"),
            displayName = string.IsNullOrWhiteSpace(security.DisplayName) ? symbol : security.DisplayName,
            assetClass = string.IsNullOrWhiteSpace(security.AssetClass) ? null : security.AssetClass,
            subType = security.SubType,
            currency = string.IsNullOrWhiteSpace(security.Currency) ? null : security.Currency,
            status = security.Status.ToString(),
            primaryIdentifier = security.PrimaryIdentifier,
            coverageStatus = security.CoverageStatus.ToString(),
            matchedIdentifierKind = security.MatchedIdentifierKind,
            matchedIdentifierValue = security.MatchedIdentifierValue,
            matchedProvider = security.MatchedProvider,
            resolutionReason = security.ResolutionReason
        };
    }

    private static WorkstationSecurityCoverageReferencePayload BuildSecurityCoverageReference(
        string Source,
        string Symbol,
        string? AccountName,
        WorkstationSecurityReference? Security)
        => new(
            Source: Source,
            Symbol: Symbol,
            AccountName: AccountName,
            SecurityId: Security is null || Security.SecurityId == Guid.Empty ? null : Security.SecurityId.ToString("N"),
            DisplayName: string.IsNullOrWhiteSpace(Security?.DisplayName) ? Symbol : Security!.DisplayName,
            AssetClass: string.IsNullOrWhiteSpace(Security?.AssetClass) ? null : Security!.AssetClass,
            SubType: Security?.SubType,
            Currency: string.IsNullOrWhiteSpace(Security?.Currency) ? null : Security!.Currency,
            Status: Security?.Status.ToString(),
            PrimaryIdentifier: Security?.PrimaryIdentifier,
            CoverageStatus: Security?.CoverageStatus.ToString() ?? WorkstationSecurityCoverageStatus.Missing.ToString(),
            CoverageReason: BuildSecurityCoverageReason(Source, AccountName, Security),
            MatchedIdentifierKind: Security?.MatchedIdentifierKind,
            MatchedIdentifierValue: Security?.MatchedIdentifierValue,
            MatchedProvider: Security?.MatchedProvider);

    private static string? BuildSecurityCoverageReason(
        string source,
        string? accountName,
        WorkstationSecurityReference? security)
    {
        if (!string.IsNullOrWhiteSpace(security?.ResolutionReason))
        {
            return security.ResolutionReason;
        }

        return security?.CoverageStatus switch
        {
            WorkstationSecurityCoverageStatus.Resolved => null,
            WorkstationSecurityCoverageStatus.Partial => "Security Master coverage is partial and requires operator review.",
            WorkstationSecurityCoverageStatus.Unavailable => "Security Master is unavailable in this environment.",
            _ when string.Equals(source, "ledger", StringComparison.OrdinalIgnoreCase)
                => string.IsNullOrWhiteSpace(accountName)
                    ? "Ledger coverage is missing a Security Master match."
                    : $"Ledger coverage in '{accountName}' is missing a Security Master match.",
            _ => "Portfolio position is missing a Security Master match."
        };
    }

    private static bool HasAuthoritativeSecurityMatch(WorkstationSecurityReference? security)
        => security is not null &&
           security.SecurityId != Guid.Empty &&
           security.CoverageStatus is WorkstationSecurityCoverageStatus.Resolved
               or WorkstationSecurityCoverageStatus.Partial;

    private static bool NeedsSecurityReview(WorkstationSecurityReference? security)
        => security is null ||
           security.CoverageStatus is WorkstationSecurityCoverageStatus.Partial
                or WorkstationSecurityCoverageStatus.Missing
                or WorkstationSecurityCoverageStatus.Unavailable;
}

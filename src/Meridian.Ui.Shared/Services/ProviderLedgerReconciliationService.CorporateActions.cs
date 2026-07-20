using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.ProviderSdk;
using Meridian.Storage.Archival;
using Meridian.Storage.SecurityMaster;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderLedgerReconciliationService
{
    private static ProviderCorporateActionReadinessDto BuildCorporateActionReadiness(
        FundAccountBrokerageSyncActivityDto? providerProjection,
        IReadOnlyList<ProviderLedgerReconciliationCheckDto> checks,
        IReadOnlyList<ProviderSecurityMasterPassportDto> securityMasterPassports,
        IReadOnlyList<string> evidenceLinks)
    {
        if (providerProjection is null)
        {
            return new ProviderCorporateActionReadinessDto(
                ProviderCorporateActionsRoutable: false,
                Status: ProviderLedgerReconciliationCheckStatusDto.Blocked,
                PositionCount: 0,
                SecurityResolvedCount: 0,
                EquityPositionCount: 0,
                FixedIncomeOrStructuredPositionCount: 0,
                FactorScheduleCandidateCount: 0,
                IncomeCashTransactionCount: 0,
                DividendCashTransactionCount: 0,
                InterestCashTransactionCount: 0,
                RequiredFeeds: [],
                MissingFeeds: ["provider-projection"],
                Warnings: ["Corporate-action readiness is blocked until a provider projection is retained."],
                EvidenceLinks: [],
                Lines:
                [
                    new ProviderCorporateActionReadinessLineDto(
                        "provider-projection",
                        "Provider projection",
                        ProviderLedgerReconciliationCheckStatusDto.Blocked,
                        "provider-projection",
                        "provider-sync",
                        0,
                        "No brokerage sync projection exists for this fund account.")
                ]);
        }

        var positions = providerProjection.Positions;
        var corporateActionEvents = providerProjection.CorporateActions ?? [];
        var equityPositionCount = positions.Count(static position => IsEquityAssetClass(position.AssetClass));
        var fixedIncomeOrStructuredPositionCount = positions.Count(static position => IsFixedIncomeOrStructuredAssetClass(position.AssetClass));
        var incomeCashTransactionCount = providerProjection.CashTransactions.Count(static transaction => IsIncomeTransaction(transaction.TransactionType));
        var dividendCashTransactionCount = providerProjection.CashTransactions.Count(static transaction => IsDividendTransaction(transaction.TransactionType));
        var interestCashTransactionCount = providerProjection.CashTransactions.Count(static transaction => IsInterestTransaction(transaction.TransactionType));
        var principalCashTransactionCount = providerProjection.CashTransactions.Count(static transaction => IsPrincipalReturnTransaction(transaction.TransactionType));
        var providerCorporateActionEventCount = corporateActionEvents.Count;
        var amortizationScheduleEventCount = corporateActionEvents.Count(static action => IsAmortizationScheduleEvent(action.EventType));
        var factorScheduleEventCount = corporateActionEvents.Count(static action => IsFactorScheduleEvent(action.EventType));
        var loanScheduleEventCount = corporateActionEvents.Count(static action => IsLoanScheduleEvent(action.EventType));
        var factorLikeScheduleEventCount = amortizationScheduleEventCount + factorScheduleEventCount + loanScheduleEventCount;
        var positionSecurityIdentityCount = positions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
            .Select(static position => position.Symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var securityResolvedCount = securityMasterPassports.Count(static passport =>
            passport.Status is ProviderSecurityMasterPassportStatusDto.Resolved or ProviderSecurityMasterPassportStatusDto.Inferred);
        var candidateCount = equityPositionCount
            + fixedIncomeOrStructuredPositionCount
            + incomeCashTransactionCount
            + principalCashTransactionCount
            + providerCorporateActionEventCount;
        var nonFactorCorporateActionEventCount = Math.Max(0, providerCorporateActionEventCount - factorLikeScheduleEventCount);
        var unresolvedSecurityCount = Math.Max(0, positionSecurityIdentityCount - securityResolvedCount);
        var corporateActionCapability = checks.FirstOrDefault(static check =>
            string.Equals(check.CheckId, "provider-capability:CorporateActions", StringComparison.OrdinalIgnoreCase));
        var factorScheduleCapability = checks.FirstOrDefault(static check =>
            string.Equals(check.CheckId, "provider-capability:FactorSchedule", StringComparison.OrdinalIgnoreCase));
        var hasCorporateActionCapabilityEvidence = corporateActionCapability is not null;
        var hasFactorScheduleCapabilityEvidence = factorScheduleCapability is not null;
        var providerCorporateActionsRoutable =
            corporateActionCapability?.Status == ProviderLedgerReconciliationCheckStatusDto.Matched;
        var factorScheduleRoutable =
            factorScheduleCapability?.Status == ProviderLedgerReconciliationCheckStatusDto.Matched;
        var requiresCorporateActionCapability =
            equityPositionCount > 0 ||
            incomeCashTransactionCount > 0 ||
            nonFactorCorporateActionEventCount > 0;
        var requiresFactorScheduleCapability =
            fixedIncomeOrStructuredPositionCount > 0 ||
            principalCashTransactionCount > 0 ||
            factorLikeScheduleEventCount > 0;
        var hasRequiredCorporateActionCapabilityEvidence =
            !requiresCorporateActionCapability || hasCorporateActionCapabilityEvidence;
        var hasRequiredFactorScheduleCapabilityEvidence =
            !requiresFactorScheduleCapability || hasFactorScheduleCapabilityEvidence;
        var requiredProviderCapabilitiesRoutable =
            (!requiresCorporateActionCapability || providerCorporateActionsRoutable) &&
            (!requiresFactorScheduleCapability || factorScheduleRoutable);
        var requiredFeeds = new List<string>();
        var missingFeeds = new List<string>();
        var warnings = new List<string>();
        var lines = new List<ProviderCorporateActionReadinessLineDto>();
        var evidenceCandidates = new List<ProviderCorporateActionEvidenceCandidateDto>();
        var passportsBySymbol = securityMasterPassports
            .Where(static passport => !string.IsNullOrWhiteSpace(passport.Symbol))
            .GroupBy(static passport => NormalizeSymbol(passport.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (equityPositionCount > 0)
        {
            requiredFeeds.Add("splits");
            requiredFeeds.Add("dividends");
        }

        if (fixedIncomeOrStructuredPositionCount > 0)
        {
            requiredFeeds.Add("factor-schedule");
            requiredFeeds.Add("coupon-schedule");
        }

        if (incomeCashTransactionCount > 0)
        {
            requiredFeeds.Add("income-cash-activity");
        }

        if (principalCashTransactionCount > 0)
        {
            requiredFeeds.Add("principal-cash-activity");
            requiredFeeds.Add("factor-schedule");
        }

        if (providerCorporateActionEventCount > 0)
        {
            requiredFeeds.Add("provider-corporate-actions");
        }

        if (factorScheduleEventCount > 0)
        {
            requiredFeeds.Add("factor-schedule");
        }

        if (amortizationScheduleEventCount > 0)
        {
            requiredFeeds.Add("amortization-schedule");
            requiredFeeds.Add("factor-schedule");
        }

        if (loanScheduleEventCount > 0)
        {
            requiredFeeds.Add("loan-schedule");
            requiredFeeds.Add("factor-schedule");
        }

        if (candidateCount > 0 &&
            (!hasRequiredCorporateActionCapabilityEvidence || !hasRequiredFactorScheduleCapabilityEvidence))
        {
            missingFeeds.Add("provider-capability-matrix");
            warnings.Add("Provider capability routing metadata is not registered, so corporate-action feed readiness cannot be confirmed.");
        }
        else if (requiresCorporateActionCapability && !providerCorporateActionsRoutable)
        {
            missingFeeds.Add("provider-corporate-actions");
            warnings.Add("Provider corporate-action capability is not routable for this account; controller review is required before relying on split, dividend, or factor evidence.");
        }

        if (requiresFactorScheduleCapability && hasFactorScheduleCapabilityEvidence && !factorScheduleRoutable)
        {
            missingFeeds.Add("factor-schedule");
            warnings.Add("Provider factor-schedule capability is not routable for this account; fixed-income, structured, amortization, or paydown evidence requires controller review.");
        }

        if (unresolvedSecurityCount > 0)
        {
            missingFeeds.Add("security-master-identities");
            warnings.Add($"{unresolvedSecurityCount} provider position(s) are missing a resolved Security Master identity for corporate-action/factor attribution.");
        }

        lines.Add(BuildCorporateActionReadinessLine(
            "equity-corporate-actions",
            "Equity corporate actions",
            "splits,dividends",
            "provider-sync",
            equityPositionCount,
            candidateCount,
            hasCorporateActionCapabilityEvidence,
            providerCorporateActionsRoutable,
            unresolvedSecurityCount,
            "corporate-action",
            "Equity positions require split and dividend evidence before provider values can be used as accounting support."));

        lines.Add(BuildCorporateActionReadinessLine(
            "factor-schedule",
            "Factor schedule candidates",
            "factor-schedule,coupon-schedule",
            "security-master-provider",
            fixedIncomeOrStructuredPositionCount,
            candidateCount,
            hasFactorScheduleCapabilityEvidence,
            factorScheduleRoutable,
            unresolvedSecurityCount,
            "factor-schedule",
            "Fixed income and structured positions require factor, coupon, amortization, or paydown schedules for valuation support."));

        lines.Add(BuildCorporateActionReadinessLine(
            "income-cash-activity",
            "Income cash activity",
            "income-cash-activity",
            "provider-activity",
            incomeCashTransactionCount,
            candidateCount,
            hasCorporateActionCapabilityEvidence,
            providerCorporateActionsRoutable,
            unresolvedSecurityCount,
            "corporate-action",
            "Dividend, interest, coupon, and distribution cash movements require retained provider activity and Security Master attribution."));

        lines.Add(BuildCorporateActionReadinessLine(
            "principal-cash-activity",
            "Principal cash activity",
            "principal-cash-activity,factor-schedule",
            "provider-activity",
            principalCashTransactionCount,
            candidateCount,
            hasFactorScheduleCapabilityEvidence,
            factorScheduleRoutable,
            unresolvedSecurityCount,
            "factor-schedule",
            "Principal, amortization, and paydown cash movements require retained provider activity, factor schedule support, and Security Master attribution."));

        var providerEventsHaveCapabilityEvidence = nonFactorCorporateActionEventCount == 0
            ? hasFactorScheduleCapabilityEvidence
            : factorLikeScheduleEventCount == 0
                ? hasCorporateActionCapabilityEvidence
                : hasCorporateActionCapabilityEvidence && hasFactorScheduleCapabilityEvidence;
        var providerEventsRoutable = nonFactorCorporateActionEventCount == 0
            ? factorScheduleRoutable
            : factorLikeScheduleEventCount == 0
                ? providerCorporateActionsRoutable
                : providerCorporateActionsRoutable && factorScheduleRoutable;
        lines.Add(BuildCorporateActionReadinessLine(
            "provider-corporate-action-events",
            "Provider corporate-action events",
            providerCorporateActionEventCount == factorLikeScheduleEventCount && factorLikeScheduleEventCount > 0
                ? "factor-schedule"
                : "provider-corporate-actions",
            "provider-corporate-action",
            providerCorporateActionEventCount,
            candidateCount,
            providerEventsHaveCapabilityEvidence,
            providerEventsRoutable,
            unresolvedSecurityCount,
            factorLikeScheduleEventCount > 0 && nonFactorCorporateActionEventCount == 0
                ? "factor-schedule"
                : "corporate-action/factor-schedule",
            "Retained provider corporate-action, factor, and loan-schedule events are direct evidence for split, dividend, amortization, paydown, loan schedule, or factor support."));

        if (candidateCount == 0)
        {
            warnings.Add("No corporate-action-sensitive positions, provider corporate-action events, income transactions, or principal cash movements were present in the provider projection.");
        }

        var status = candidateCount == 0
            ? ProviderLedgerReconciliationCheckStatusDto.Matched
            : !hasRequiredCorporateActionCapabilityEvidence || !hasRequiredFactorScheduleCapabilityEvidence
                ? ProviderLedgerReconciliationCheckStatusDto.Blocked
                : requiredProviderCapabilitiesRoutable && unresolvedSecurityCount == 0
                    ? ProviderLedgerReconciliationCheckStatusDto.Matched
                    : ProviderLedgerReconciliationCheckStatusDto.Break;

        foreach (var position in positions)
        {
            if (!IsEquityAssetClass(position.AssetClass) &&
                !IsFixedIncomeOrStructuredAssetClass(position.AssetClass))
            {
                continue;
            }

            passportsBySymbol.TryGetValue(NormalizeSymbol(position.Symbol), out var passport);
            var candidateType = IsFixedIncomeOrStructuredAssetClass(position.AssetClass)
                ? "FactorScheduleCandidate"
                : "EquityCorporateActionCandidate";
            var requiredFeed = candidateType == "FactorScheduleCandidate"
                ? "factor-schedule,coupon-schedule"
                : "splits,dividends";
            var providerEventId = string.IsNullOrWhiteSpace(position.PositionId)
                ? position.Symbol
                : position.PositionId;
            var candidateStatus = BuildCorporateActionCandidateStatus(
                candidateType == "FactorScheduleCandidate"
                    ? hasFactorScheduleCapabilityEvidence
                    : hasCorporateActionCapabilityEvidence,
                candidateType == "FactorScheduleCandidate"
                    ? factorScheduleRoutable
                    : providerCorporateActionsRoutable,
                passport,
                requiresSecurityIdentity: true);

            evidenceCandidates.Add(new ProviderCorporateActionEvidenceCandidateDto(
                CandidateId: BuildCorporateActionCandidateId(
                    providerProjection.Link.ProviderId,
                    providerProjection.Link.ExternalAccountId,
                    candidateType,
                    providerEventId,
                    position.Symbol),
                CandidateType: candidateType,
                Symbol: NormalizeOptional(position.Symbol),
                SecurityId: passport?.SecurityId ?? position.Security?.SecurityId,
                SecurityDisplayName: passport?.SecurityDisplayName ?? position.Security?.DisplayName,
                Status: candidateStatus,
                RequiredFeed: requiredFeed,
                EvidenceSource: "provider-position",
                ProviderId: providerProjection.Link.ProviderId,
                ExternalAccountId: providerProjection.Link.ExternalAccountId,
                ProviderEventId: providerEventId,
                ObservedAt: providerProjection.SyncedAt,
                Amount: position.MarketValue,
                Quantity: position.Quantity,
                Currency: NormalizeOptional(position.Currency) ?? NormalizeOptional(providerProjection.Balance?.Currency),
                Reason: BuildCorporateActionCandidateReason(
                    candidateType,
                    candidateType == "FactorScheduleCandidate"
                        ? hasFactorScheduleCapabilityEvidence
                        : hasCorporateActionCapabilityEvidence,
                    candidateType == "FactorScheduleCandidate"
                        ? factorScheduleRoutable
                        : providerCorporateActionsRoutable,
                    passport,
                    requiresSecurityIdentity: true,
                    candidateType == "FactorScheduleCandidate" ? "factor-schedule" : "corporate-action")));
        }

        foreach (var transaction in providerProjection.CashTransactions.Where(static transaction =>
                     IsIncomeTransaction(transaction.TransactionType) ||
                     IsPrincipalReturnTransaction(transaction.TransactionType)))
        {
            passportsBySymbol.TryGetValue(NormalizeSymbol(transaction.Symbol), out var passport);
            var isPrincipalReturn = IsPrincipalReturnTransaction(transaction.TransactionType);
            var candidateType = isPrincipalReturn
                ? "PrincipalCashActivity"
                : IsDividendTransaction(transaction.TransactionType)
                    ? "DividendCashActivity"
                    : IsInterestTransaction(transaction.TransactionType)
                        ? "InterestCashActivity"
                        : "DistributionCashActivity";
            var requiresSecurityIdentity = !string.IsNullOrWhiteSpace(transaction.Symbol);
            var candidateStatus = BuildCorporateActionCandidateStatus(
                isPrincipalReturn ? hasFactorScheduleCapabilityEvidence : hasCorporateActionCapabilityEvidence,
                isPrincipalReturn ? factorScheduleRoutable : providerCorporateActionsRoutable,
                passport,
                requiresSecurityIdentity);

            evidenceCandidates.Add(new ProviderCorporateActionEvidenceCandidateDto(
                CandidateId: BuildCorporateActionCandidateId(
                    providerProjection.Link.ProviderId,
                    providerProjection.Link.ExternalAccountId,
                    candidateType,
                    transaction.TransactionId,
                    transaction.Symbol),
                CandidateType: candidateType,
                Symbol: NormalizeOptional(transaction.Symbol),
                SecurityId: passport?.SecurityId,
                SecurityDisplayName: passport?.SecurityDisplayName,
                Status: candidateStatus,
                RequiredFeed: isPrincipalReturn ? "principal-cash-activity,factor-schedule" : "income-cash-activity",
                EvidenceSource: "provider-activity",
                ProviderId: providerProjection.Link.ProviderId,
                ExternalAccountId: providerProjection.Link.ExternalAccountId,
                ProviderEventId: transaction.TransactionId,
                ObservedAt: transaction.PostedAt,
                Amount: transaction.Amount,
                Quantity: null,
                Currency: NormalizeOptional(transaction.Currency),
                Reason: BuildCorporateActionCandidateReason(
                    candidateType,
                    isPrincipalReturn ? hasFactorScheduleCapabilityEvidence : hasCorporateActionCapabilityEvidence,
                    isPrincipalReturn ? factorScheduleRoutable : providerCorporateActionsRoutable,
                    passport,
                    requiresSecurityIdentity,
                    isPrincipalReturn ? "factor-schedule" : "corporate-action")));
        }

        foreach (var action in corporateActionEvents)
        {
            passportsBySymbol.TryGetValue(NormalizeSymbol(action.Symbol), out var passport);
            var candidateType = ResolveCorporateActionEventCandidateType(action.EventType);
            var requiredFeed = ResolveCorporateActionEventRequiredFeed(action.EventType);
            var requiresSecurityIdentity = !string.IsNullOrWhiteSpace(action.Symbol);
            var isScheduleEvent = IsScheduleEvidenceCandidate(candidateType);
            var candidateStatus = BuildCorporateActionCandidateStatus(
                isScheduleEvent
                    ? hasFactorScheduleCapabilityEvidence
                    : hasCorporateActionCapabilityEvidence,
                isScheduleEvent
                    ? factorScheduleRoutable
                    : providerCorporateActionsRoutable,
                passport,
                requiresSecurityIdentity);
            var amount = action.Amount ?? action.Factor;

            evidenceCandidates.Add(new ProviderCorporateActionEvidenceCandidateDto(
                CandidateId: BuildCorporateActionCandidateId(
                    providerProjection.Link.ProviderId,
                    providerProjection.Link.ExternalAccountId,
                    candidateType,
                    action.EventId,
                    action.Symbol),
                CandidateType: candidateType,
                Symbol: NormalizeOptional(action.Symbol),
                SecurityId: passport?.SecurityId,
                SecurityDisplayName: passport?.SecurityDisplayName,
                Status: candidateStatus,
                RequiredFeed: requiredFeed,
                EvidenceSource: "provider-corporate-action",
                ProviderId: providerProjection.Link.ProviderId,
                ExternalAccountId: providerProjection.Link.ExternalAccountId,
                ProviderEventId: action.EventId,
                ObservedAt: providerProjection.SyncedAt,
                Amount: amount,
                Quantity: action.Quantity,
                Currency: NormalizeOptional(action.Currency) ?? NormalizeOptional(providerProjection.Balance?.Currency),
                Reason: BuildCorporateActionCandidateReason(
                    candidateType,
                    isScheduleEvent
                        ? hasFactorScheduleCapabilityEvidence
                        : hasCorporateActionCapabilityEvidence,
                    isScheduleEvent
                        ? factorScheduleRoutable
                        : providerCorporateActionsRoutable,
                    passport,
                    requiresSecurityIdentity,
                    isScheduleEvent ? "factor-schedule" : "corporate-action")));
        }

        var orderedCandidates = evidenceCandidates
            .OrderBy(static candidate => candidate.CandidateType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.ProviderEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var ledgerEffects = BuildCorporateActionLedgerEffects(orderedCandidates);
        return new ProviderCorporateActionReadinessDto(
            ProviderCorporateActionsRoutable: providerCorporateActionsRoutable,
            Status: status,
            PositionCount: positions.Count,
            SecurityResolvedCount: securityResolvedCount,
            EquityPositionCount: equityPositionCount,
            FixedIncomeOrStructuredPositionCount: fixedIncomeOrStructuredPositionCount,
            FactorScheduleCandidateCount: fixedIncomeOrStructuredPositionCount,
            IncomeCashTransactionCount: incomeCashTransactionCount,
            DividendCashTransactionCount: dividendCashTransactionCount,
            InterestCashTransactionCount: interestCashTransactionCount,
            RequiredFeeds: requiredFeeds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MissingFeeds: missingFeeds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings: warnings.ToArray(),
            EvidenceLinks: evidenceLinks.Where(static link => !string.IsNullOrWhiteSpace(link)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Lines: lines,
            ProviderCorporateActionEventCount: providerCorporateActionEventCount,
            FactorScheduleEventCount: factorScheduleEventCount,
            FactorScheduleRoutable: factorScheduleRoutable,
            LoanScheduleEventCount: loanScheduleEventCount)
        {
            EvidenceCandidates = orderedCandidates,
            LedgerEffects = ledgerEffects,
            SecurityMasterScheduleFeeds = BuildSecurityMasterScheduleFeeds(orderedCandidates, ledgerEffects),
            PrincipalCashTransactionCount = principalCashTransactionCount,
            AmortizationScheduleEventCount = amortizationScheduleEventCount
        };
    }

    private static IReadOnlyList<ProviderCorporateActionLedgerEffectDto> BuildCorporateActionLedgerEffects(
        IReadOnlyList<ProviderCorporateActionEvidenceCandidateDto> candidates)
        => candidates
            .Select(BuildCorporateActionLedgerEffect)
            .Where(static effect => effect is not null)
            .Cast<ProviderCorporateActionLedgerEffectDto>()
            .ToArray();

    private static bool IsScheduleEvidenceCandidate(string candidateType) =>
        string.Equals(candidateType, "FactorScheduleEvent", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(candidateType, "AmortizationScheduleEvent", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(candidateType, "LoanScheduleEvent", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ProviderSecurityMasterScheduleFeedDto> BuildSecurityMasterScheduleFeeds(
        IReadOnlyList<ProviderCorporateActionEvidenceCandidateDto> candidates,
        IReadOnlyList<ProviderCorporateActionLedgerEffectDto> ledgerEffects)
    {
        var candidatesById = candidates.ToDictionary(static candidate => candidate.CandidateId, StringComparer.OrdinalIgnoreCase);
        return ledgerEffects
            .Where(static effect => IsSecurityMasterScheduleFeedEffect(effect.LedgerEffectKind))
            .Select(effect =>
            {
                candidatesById.TryGetValue(effect.CandidateId, out var candidate);
                var canUpdateSecurityMaster = effect.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                    effect.SecurityId.HasValue &&
                    !string.Equals(effect.LedgerEffectKind, "FactorScheduleCoverageCandidate", StringComparison.OrdinalIgnoreCase);
                var canSupportLedgerValuation = effect.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                    !string.Equals(effect.LedgerEffectKind, "FactorScheduleCoverageCandidate", StringComparison.OrdinalIgnoreCase);
                return new ProviderSecurityMasterScheduleFeedDto(
                    FeedId: $"security-master-feed:{NormalizeBreakIdPart(effect.CandidateId)}",
                    CandidateId: effect.CandidateId,
                    CandidateType: effect.CandidateType,
                    FeedKind: MapSecurityMasterScheduleFeedKind(effect.LedgerEffectKind),
                    RequiredFeed: candidate?.RequiredFeed ?? ResolveRequiredFeedFromLedgerEffect(effect.LedgerEffectKind),
                    EvidenceSource: candidate?.EvidenceSource ?? "provider-ledger",
                    ProviderId: candidate?.ProviderId ?? "unknown-provider",
                    ExternalAccountId: candidate?.ExternalAccountId ?? "unknown-account",
                    ProviderEventId: effect.ProviderEventId,
                    Symbol: effect.Symbol,
                    SecurityId: effect.SecurityId,
                    EffectiveDate: effect.EffectiveDate,
                    Factor: effect.Factor,
                    CashAmount: effect.CashAmount,
                    PrincipalAmount: effect.PrincipalAmount,
                    IncomeAmount: effect.IncomeAmount,
                    Currency: effect.Currency,
                    LedgerEffectKind: effect.LedgerEffectKind,
                    Status: effect.Status,
                    CanUpdateSecurityMaster: canUpdateSecurityMaster,
                    CanSupportLedgerValuation: canSupportLedgerValuation,
                    Reason: BuildSecurityMasterScheduleFeedReason(effect, canUpdateSecurityMaster, canSupportLedgerValuation));
            })
            .OrderBy(static feed => feed.EffectiveDate)
            .ThenBy(static feed => feed.FeedKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static feed => feed.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSecurityMasterScheduleFeedEffect(string ledgerEffectKind) =>
        string.Equals(ledgerEffectKind, "FactorScheduleValuationInput", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "AmortizationScheduleValuationInput", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "LoanScheduleValuationInput", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "CorporateActionCoverageInput", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "DividendIncomeRecognition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "DistributionIncomeRecognition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "CashIncomeRecognition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "PrincipalReturnRecognition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "FactorScheduleCoverageCandidate", StringComparison.OrdinalIgnoreCase);

    private static string MapSecurityMasterScheduleFeedKind(string ledgerEffectKind) =>
        ledgerEffectKind switch
        {
            "FactorScheduleValuationInput" => "SecurityMasterFactorHistory",
            "AmortizationScheduleValuationInput" => "SecurityMasterAmortizationSchedule",
            "LoanScheduleValuationInput" => "SecurityMasterLoanSchedule",
            "CorporateActionCoverageInput" => "SecurityMasterCorporateAction",
            "DividendIncomeRecognition" or "DistributionIncomeRecognition" or "CashIncomeRecognition" => "SecurityMasterIncomeSchedule",
            "PrincipalReturnRecognition" => "SecurityMasterPrincipalSchedule",
            "FactorScheduleCoverageCandidate" => "SecurityMasterFactorCoverageRequirement",
            _ => "SecurityMasterScheduleEvidence"
        };

    private static string ResolveRequiredFeedFromLedgerEffect(string ledgerEffectKind) =>
        ledgerEffectKind switch
        {
            "FactorScheduleValuationInput" or "FactorScheduleCoverageCandidate" => "factor-schedule",
            "AmortizationScheduleValuationInput" => "amortization-schedule,factor-schedule",
            "LoanScheduleValuationInput" => "loan-schedule,factor-schedule",
            "DividendIncomeRecognition" or "DistributionIncomeRecognition" or "CashIncomeRecognition" => "income-cash-activity",
            "PrincipalReturnRecognition" => "principal-cash-activity,factor-schedule",
            "CorporateActionCoverageInput" => "provider-corporate-actions",
            _ => "provider-ledger"
        };

    private static string BuildSecurityMasterScheduleFeedReason(
        ProviderCorporateActionLedgerEffectDto effect,
        bool canUpdateSecurityMaster,
        bool canSupportLedgerValuation)
    {
        if (canUpdateSecurityMaster && canSupportLedgerValuation)
        {
            return $"{effect.LedgerEffectKind} is matched, Security Master-attributed, and ready to feed schedule/factor history plus downstream ledger valuation support.";
        }

        if (!effect.SecurityId.HasValue)
        {
            return $"{effect.LedgerEffectKind} cannot update Security Master schedule history until the provider evidence resolves to a Security Master identity.";
        }

        if (string.Equals(effect.LedgerEffectKind, "FactorScheduleCoverageCandidate", StringComparison.OrdinalIgnoreCase))
        {
            return "Fixed-income or structured holdings require provider factor/coupon schedule evidence before Security Master history or ledger valuation can be updated.";
        }

        return $"{effect.LedgerEffectKind} requires controller review before it can feed Security Master schedule history or ledger valuation support.";
    }

    private static ProviderCorporateActionLedgerEffectDto? BuildCorporateActionLedgerEffect(
        ProviderCorporateActionEvidenceCandidateDto candidate)
    {
        var eventDate = DateOnly.FromDateTime(candidate.ObservedAt.UtcDateTime);
        if (string.Equals(candidate.CandidateType, "FactorScheduleEvent", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "FactorScheduleValuationInput",
                eventDate,
                Factor: candidate.Amount,
                CashAmount: null,
                PrincipalAmount: null,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Retained provider factor evidence can feed Security Master factor history and downstream ledger valuation; journal amount generation still requires par and prior-factor context."
                    : candidate.Reason,
                JournalLines: []);
        }

        if (string.Equals(candidate.CandidateType, "AmortizationScheduleEvent", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "AmortizationScheduleValuationInput",
                eventDate,
                Factor: candidate.Quantity,
                CashAmount: candidate.Amount,
                PrincipalAmount: candidate.Amount,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Retained provider amortization schedule evidence can feed Security Master amortization history and downstream ledger valuation; final journal generation still requires amortization policy context."
                    : candidate.Reason,
                JournalLines: []);
        }

        if (string.Equals(candidate.CandidateType, "LoanScheduleEvent", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "LoanScheduleValuationInput",
                eventDate,
                Factor: candidate.Quantity,
                CashAmount: candidate.Amount,
                PrincipalAmount: candidate.Amount,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Retained provider loan schedule evidence can feed Security Master schedule history and downstream ledger valuation; final journal generation still requires amortization policy context."
                    : candidate.Reason,
                JournalLines: []);
        }

        if (string.Equals(candidate.CandidateType, "FactorScheduleCandidate", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "FactorScheduleCoverageCandidate",
                eventDate,
                Factor: null,
                CashAmount: null,
                PrincipalAmount: null,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Fixed-income or structured position requires factor/coupon schedule coverage before final ledger valuation support."
                    : candidate.Reason,
                JournalLines: []);
        }

        if (string.Equals(candidate.CandidateType, "InterestCashActivity", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCashIncomeLedgerEffect(
                candidate,
                eventDate,
                "CashIncomeRecognition",
                "Coupon Income");
        }

        if (string.Equals(candidate.CandidateType, "PrincipalCashActivity", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPrincipalCashLedgerEffect(candidate, eventDate);
        }

        if (string.Equals(candidate.CandidateType, "DividendCashActivity", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.CandidateType, "DistributionCashActivity", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCashIncomeLedgerEffect(
                candidate,
                eventDate,
                string.Equals(candidate.CandidateType, "DividendCashActivity", StringComparison.OrdinalIgnoreCase)
                    ? "DividendIncomeRecognition"
                    : "DistributionIncomeRecognition",
                "Dividend Income");
        }

        if (string.Equals(candidate.CandidateType, "EquityCorporateActionCandidate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.CandidateType, "CorporateActionEvent", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "CorporateActionCoverageInput",
                eventDate,
                Factor: null,
                CashAmount: candidate.Amount,
                PrincipalAmount: null,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Retained provider corporate-action evidence can support Security Master event review and downstream ledger valuation."
                    : candidate.Reason,
                JournalLines: []);
        }

        return null;
    }

    private static ProviderCorporateActionLedgerEffectDto BuildCashIncomeLedgerEffect(
        ProviderCorporateActionEvidenceCandidateDto candidate,
        DateOnly eventDate,
        string ledgerEffectKind,
        string incomeAccount)
    {
        var amount = candidate.Amount;
        var journalLines = candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched && amount.HasValue
            ? new[]
            {
                new ExpectedJournalPreviewLineDto("Cash", "Asset", null, Math.Abs(amount.Value), 0m),
                new ExpectedJournalPreviewLineDto(incomeAccount, "Revenue", candidate.Symbol, 0m, Math.Abs(amount.Value))
            }
            : [];
        return new ProviderCorporateActionLedgerEffectDto(
            candidate.CandidateId,
            candidate.CandidateType,
            candidate.Symbol,
            candidate.SecurityId,
            candidate.ProviderEventId,
            ledgerEffectKind,
            eventDate,
            Factor: null,
            CashAmount: amount,
            PrincipalAmount: null,
            IncomeAmount: amount,
            candidate.Currency,
            candidate.Status,
            candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                ? "Retained provider income cash activity has enough Security Master attribution to preview the expected cash/income journal support."
                : candidate.Reason,
            journalLines);
    }

    private static ProviderCorporateActionLedgerEffectDto BuildPrincipalCashLedgerEffect(
        ProviderCorporateActionEvidenceCandidateDto candidate,
        DateOnly eventDate)
    {
        var amount = candidate.Amount;
        var journalLines = candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched && amount.HasValue
            ? new[]
            {
                new ExpectedJournalPreviewLineDto("Cash", "Asset", null, Math.Abs(amount.Value), 0m),
                new ExpectedJournalPreviewLineDto("Investment Principal", "Asset", candidate.Symbol, 0m, Math.Abs(amount.Value))
            }
            : [];
        return new ProviderCorporateActionLedgerEffectDto(
            candidate.CandidateId,
            candidate.CandidateType,
            candidate.Symbol,
            candidate.SecurityId,
            candidate.ProviderEventId,
            "PrincipalReturnRecognition",
            eventDate,
            Factor: null,
            CashAmount: amount,
            PrincipalAmount: amount,
            IncomeAmount: null,
            candidate.Currency,
            candidate.Status,
            candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                ? "Retained provider principal, amortization, or paydown activity has enough Security Master attribution to preview the expected cash/principal journal support."
                : candidate.Reason,
            journalLines);
    }

    private static ProviderCorporateActionReadinessLineDto BuildCorporateActionReadinessLine(
        string dimension,
        string label,
        string requiredFeed,
        string evidenceSource,
        int count,
        int candidateCount,
        bool hasCapabilityEvidence,
        bool providerCorporateActionsRoutable,
        int unresolvedSecurityCount,
        string capabilityLabel,
        string reason)
    {
        if (count == 0)
        {
            return new ProviderCorporateActionReadinessLineDto(
                dimension,
                label,
                ProviderLedgerReconciliationCheckStatusDto.Matched,
                requiredFeed,
                evidenceSource,
                count,
                $"{reason} No matching provider records were present in this projection.");
        }

        if (!hasCapabilityEvidence)
        {
            return new ProviderCorporateActionReadinessLineDto(
                dimension,
                label,
                ProviderLedgerReconciliationCheckStatusDto.Blocked,
                requiredFeed,
                evidenceSource,
                count,
                $"{reason} Provider capability routing metadata is unavailable for {candidateCount} corporate-action-sensitive record(s).");
        }

        if (!providerCorporateActionsRoutable)
        {
            return new ProviderCorporateActionReadinessLineDto(
                dimension,
                label,
                ProviderLedgerReconciliationCheckStatusDto.Break,
                requiredFeed,
                evidenceSource,
                count,
                $"{reason} Provider {capabilityLabel} capability is not routable for this account.");
        }

        if (unresolvedSecurityCount > 0)
        {
            return new ProviderCorporateActionReadinessLineDto(
                dimension,
                label,
                ProviderLedgerReconciliationCheckStatusDto.Break,
                requiredFeed,
                evidenceSource,
                count,
                $"{reason} {unresolvedSecurityCount} position(s) need Security Master identity resolution before attribution is complete.");
        }

        return new ProviderCorporateActionReadinessLineDto(
            dimension,
            label,
            ProviderLedgerReconciliationCheckStatusDto.Matched,
            requiredFeed,
            evidenceSource,
            count,
            $"{reason} Provider capability and Security Master identity coverage are available.");
    }

    private static ProviderLedgerReconciliationCheckStatusDto BuildCorporateActionCandidateStatus(
        bool hasCapabilityEvidence,
        bool providerCorporateActionsRoutable,
        ProviderSecurityMasterPassportDto? passport,
        bool requiresSecurityIdentity)
    {
        if (!hasCapabilityEvidence)
        {
            return ProviderLedgerReconciliationCheckStatusDto.Blocked;
        }

        if (!providerCorporateActionsRoutable)
        {
            return ProviderLedgerReconciliationCheckStatusDto.Break;
        }

        return requiresSecurityIdentity && !IsResolvedSecurityPassport(passport)
            ? ProviderLedgerReconciliationCheckStatusDto.Break
            : ProviderLedgerReconciliationCheckStatusDto.Matched;
    }

    private static string BuildCorporateActionCandidateReason(
        string candidateType,
        bool hasCapabilityEvidence,
        bool providerCorporateActionsRoutable,
        ProviderSecurityMasterPassportDto? passport,
        bool requiresSecurityIdentity,
        string capabilityLabel)
    {
        if (!hasCapabilityEvidence)
        {
            return $"{candidateType} cannot be promoted until provider capability routing metadata is available.";
        }

        if (!providerCorporateActionsRoutable)
        {
            return $"{candidateType} requires controller review because provider {capabilityLabel} capability is not routable.";
        }

        if (passport is not null && IsResolvedSecurityPassport(passport))
        {
            return $"{candidateType} has provider evidence and resolved Security Master attribution.";
        }

        if (!requiresSecurityIdentity)
        {
            return $"{candidateType} has provider account-level income evidence and no provider symbol was supplied for Security Master attribution.";
        }

        return $"{candidateType} has provider evidence but needs Security Master identity attribution.";
    }

    private static bool IsResolvedSecurityPassport(ProviderSecurityMasterPassportDto? passport)
        => passport?.Status is ProviderSecurityMasterPassportStatusDto.Resolved or ProviderSecurityMasterPassportStatusDto.Inferred;

    private static string BuildCorporateActionCandidateId(
        string providerId,
        string externalAccountId,
        string candidateType,
        string? providerEventId,
        string? symbol)
        => string.Join(
            ":",
            "provider-corporate-action",
            NormalizeCandidateToken(providerId, "provider"),
            NormalizeCandidateToken(externalAccountId, "account"),
            NormalizeCandidateToken(candidateType, "candidate"),
            NormalizeCandidateToken(providerEventId, "event"),
            NormalizeCandidateToken(symbol, "symbol"));

    private static string NormalizeCandidateToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().Replace(' ', '-').ToLowerInvariant();
    }

    private static string NormalizeSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsIncomeTransaction(string transactionType)
    {
        if (string.IsNullOrWhiteSpace(transactionType))
        {
            return false;
        }

        return transactionType.Contains("dividend", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("interest", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("coupon", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("income", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("distribution", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrincipalReturnTransaction(string transactionType)
        => !string.IsNullOrWhiteSpace(transactionType) &&
           (transactionType.Contains("principal", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("paydown", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("amortization", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("amortisation", StringComparison.OrdinalIgnoreCase));

    private static bool IsDividendTransaction(string transactionType)
        => !string.IsNullOrWhiteSpace(transactionType) &&
           (transactionType.Contains("dividend", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("distribution", StringComparison.OrdinalIgnoreCase));

    private static bool IsInterestTransaction(string transactionType)
        => !string.IsNullOrWhiteSpace(transactionType) &&
           (transactionType.Contains("interest", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("coupon", StringComparison.OrdinalIgnoreCase));

    private static bool IsFactorScheduleEvent(string eventType)
        => !string.IsNullOrWhiteSpace(eventType) &&
           !IsLoanScheduleEvent(eventType) &&
           !IsAmortizationScheduleEvent(eventType) &&
           (eventType.Contains("factor", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("paydown", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("principal", StringComparison.OrdinalIgnoreCase));

    private static bool IsAmortizationScheduleEvent(string eventType)
        => !string.IsNullOrWhiteSpace(eventType) &&
           !IsLoanScheduleEvent(eventType) &&
           (eventType.Contains("amortization", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("amortisation", StringComparison.OrdinalIgnoreCase));

    private static bool IsLoanScheduleEvent(string eventType)
        => !string.IsNullOrWhiteSpace(eventType) &&
           eventType.Contains("loan", StringComparison.OrdinalIgnoreCase) &&
           eventType.Contains("schedule", StringComparison.OrdinalIgnoreCase);

    private static string ResolveCorporateActionEventCandidateType(string eventType)
    {
        if (IsLoanScheduleEvent(eventType))
        {
            return "LoanScheduleEvent";
        }

        if (IsAmortizationScheduleEvent(eventType))
        {
            return "AmortizationScheduleEvent";
        }

        if (IsFactorScheduleEvent(eventType))
        {
            return "FactorScheduleEvent";
        }

        if (!string.IsNullOrWhiteSpace(eventType) &&
            eventType.Contains("split", StringComparison.OrdinalIgnoreCase))
        {
            return "SplitCorporateActionEvent";
        }

        return IsDividendTransaction(eventType)
            ? "DividendCorporateActionEvent"
            : "ProviderCorporateActionEvent";
    }

    private static string ResolveCorporateActionEventRequiredFeed(string eventType)
    {
        if (IsLoanScheduleEvent(eventType))
        {
            return "loan-schedule,factor-schedule";
        }

        if (IsAmortizationScheduleEvent(eventType))
        {
            return "amortization-schedule,factor-schedule";
        }

        if (IsFactorScheduleEvent(eventType))
        {
            return "factor-schedule";
        }

        if (!string.IsNullOrWhiteSpace(eventType) &&
            eventType.Contains("split", StringComparison.OrdinalIgnoreCase))
        {
            return "splits";
        }

        return IsDividendTransaction(eventType)
            ? "dividends"
            : "provider-corporate-actions";
    }

    private static bool IsEquityAssetClass(string assetClass)
        => !string.IsNullOrWhiteSpace(assetClass) &&
           (assetClass.Contains("equity", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("stock", StringComparison.OrdinalIgnoreCase));

    private static bool IsFixedIncomeOrStructuredAssetClass(string assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            return false;
        }

        return assetClass.Contains("bond", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("fixed", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("treasury", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("mbs", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("abs", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("loan", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("structured", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("mortgage", StringComparison.OrdinalIgnoreCase);
    }
}

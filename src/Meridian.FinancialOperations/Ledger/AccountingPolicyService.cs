using System.Collections.Concurrent;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.FinancialOperations.Ledger;

public interface IAccountingPolicyService
{
    Task<AccountingPolicyDto> CreatePolicyAsync(CreateAccountingPolicyRequest request, CancellationToken ct = default);

    Task<AccountingPolicyDto> ResolvePolicyAsync(AccountingPolicyQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<AccountingPolicyDto>> ListPoliciesAsync(AccountingBasisKindDto? accountingBasis = null, CancellationToken ct = default);
}

public interface IAccountingBasisProjectionService
{
    Task<AccountingBasisProjectionResult> ProjectAsync(AccountingBasisProjectionRequest request, CancellationToken ct = default);
}

public sealed record AccountingBasisProjectionRequest(
    JournalEntry SourceEntry,
    Guid AggregateId,
    Guid PeriodId,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    DateOnly? EffectiveDate = null,
    Guid? CommandId = null,
    Guid? CorrelationId = null,
    Guid? SourceEventId = null,
    Guid? SourceJournalEntryId = null,
    string? FundProfileId = null,
    Guid? FundStructureNodeId = null,
    string? InstrumentId = null,
    string? PolicyId = null,
    string? RuleId = null,
    string? RuleVersion = null,
    LedgerPostingKindDto PostingKind = LedgerPostingKindDto.Originating,
    LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval = null,
    Guid? LedgerBookId = null,
    AccountingPostingCommandDto? PostingCommand = null);

public sealed record AccountingBasisProjectionResult(
    AccountingPolicyDto Policy,
    LedgerJournalEntryWrite Write);

public sealed class AccountingPolicyService : IAccountingPolicyService
{
    private readonly ConcurrentDictionary<string, AccountingPolicyDto> _policies = new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeProvider _timeProvider;

    public AccountingPolicyService()
        : this(TimeProvider.System)
    {
    }

    /// <param name="timeProvider">
    /// Supplies "now" when a query does not carry an explicit effective date. Injecting it is what
    /// makes the undated-query path testable at a period boundary rather than only on whatever day
    /// the suite happens to run.
    /// </param>
    public AccountingPolicyService(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;

        foreach (var policy in BuildDefaultPolicies())
        {
            _policies[Key(policy.AccountingBasis, policy.PolicyId, policy.Version)] = policy;
        }
    }

    public Task<AccountingPolicyDto> CreatePolicyAsync(CreateAccountingPolicyRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var policy = new AccountingPolicyDto(
            PolicyId: RequireText(request.PolicyId, nameof(request.PolicyId)),
            AccountingBasis: request.AccountingBasis,
            Version: RequireText(request.Version, nameof(request.Version)),
            DisplayName: RequireText(request.DisplayName, nameof(request.DisplayName)),
            EffectiveFrom: request.EffectiveFrom,
            EffectiveTo: request.EffectiveTo,
            IsDefault: request.IsDefault,
            RulesJson: string.IsNullOrWhiteSpace(request.RulesJson) ? "{}" : request.RulesJson.Trim(),
            CreatedAt: DateTimeOffset.UtcNow,
            FundProfileId: NormalizeOptional(request.FundProfileId),
            FundStructureNodeId: request.FundStructureNodeId,
            InstrumentId: NormalizeOptional(request.InstrumentId),
            SourceEventId: request.SourceEventId,
            RulePack: NormalizeRulePack(request.RulePack, request.PolicyId, request.Version));

        _policies[Key(policy.AccountingBasis, policy.PolicyId, policy.Version)] = policy;
        return Task.FromResult(policy);
    }

    public Task<AccountingPolicyDto> ResolvePolicyAsync(AccountingPolicyQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var effectiveDate = query.EffectiveDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var policies = _policies.Values
            .Where(policy => policy.AccountingBasis == query.AccountingBasis)
            .Where(policy => string.IsNullOrWhiteSpace(query.PolicyId)
                             || string.Equals(policy.PolicyId, query.PolicyId, StringComparison.OrdinalIgnoreCase))
            .Where(policy => policy.EffectiveFrom <= effectiveDate
                             && (policy.EffectiveTo is null || effectiveDate <= policy.EffectiveTo.Value))
            .Where(policy => MatchesOptionalTextScope(policy.FundProfileId, query.FundProfileId))
            .Where(policy => MatchesOptionalScope(policy.FundStructureNodeId, query.FundStructureNodeId))
            .Where(policy => MatchesOptionalTextScope(policy.InstrumentId, query.InstrumentId))
            .Where(policy => MatchesOptionalScope(policy.SourceEventId, query.SourceEventId))
            .OrderByDescending(ScopeSpecificity)
            .ThenByDescending(static policy => policy.IsDefault)
            .ThenByDescending(static policy => policy.EffectiveFrom)
            .ThenBy(static policy => policy.PolicyId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (policies.Length == 0)
        {
            throw new InvalidOperationException($"No accounting policy is configured for {query.AccountingBasis} basis.");
        }

        return Task.FromResult(policies[0]);
    }

    public Task<IReadOnlyList<AccountingPolicyDto>> ListPoliciesAsync(AccountingBasisKindDto? accountingBasis = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var results = _policies.Values
            .Where(policy => accountingBasis is null || policy.AccountingBasis == accountingBasis.Value)
            .OrderBy(static policy => policy.AccountingBasis)
            .ThenBy(static policy => policy.PolicyId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static policy => policy.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<AccountingPolicyDto>>(results);
    }

    private static IReadOnlyList<AccountingPolicyDto> BuildDefaultPolicies()
    {
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var start = new DateOnly(1900, 1, 1);
        return
        [
            Default(AccountingBasisKindDto.Primary, "legacy-v1", "legacy-v1", "Legacy primary ledger policy", AccountingTreatmentKindDto.General),
            Default(AccountingBasisKindDto.Gaap, "gaap-default-v1", "v1", "Default GAAP basis policy", AccountingTreatmentKindDto.Accrual),
            Default(AccountingBasisKindDto.Cash, "cash-default-v1", "v1", "Default cash basis policy", AccountingTreatmentKindDto.General),
            Default(AccountingBasisKindDto.Tax, "tax-default-v1", "v1", "Default tax basis policy", AccountingTreatmentKindDto.TaxLotRelief),
            Default(AccountingBasisKindDto.Statutory, "stat-default-v1", "v1", "Default statutory basis policy", AccountingTreatmentKindDto.General)
        ];

        AccountingPolicyDto Default(
            AccountingBasisKindDto basis,
            string policyId,
            string version,
            string displayName,
            AccountingTreatmentKindDto treatmentKind) =>
            new(
                policyId,
                basis,
                version,
                displayName,
                start,
                EffectiveTo: null,
                IsDefault: true,
                RulesJson: "{}",
                CreatedAt: createdAt,
                RulePack: BuildDefaultRulePack(policyId, version, treatmentKind));
    }

    private static string Key(AccountingBasisKindDto basis, string policyId, string version)
        => $"{basis}:{policyId}:{version}";

    private static bool MatchesOptionalScope(Guid? policyScope, Guid? queryScope)
        => policyScope is null || (queryScope.HasValue && policyScope.Value == queryScope.Value);

    private static bool MatchesOptionalTextScope(string? policyScope, string? queryScope)
        => string.IsNullOrWhiteSpace(policyScope)
           || (!string.IsNullOrWhiteSpace(queryScope)
               && string.Equals(policyScope, queryScope.Trim(), StringComparison.OrdinalIgnoreCase));

    private static int ScopeSpecificity(AccountingPolicyDto policy)
        => (string.IsNullOrWhiteSpace(policy.FundProfileId) ? 0 : 1)
           + (policy.FundStructureNodeId.HasValue ? 1 : 0)
           + (string.IsNullOrWhiteSpace(policy.InstrumentId) ? 0 : 1)
           + (policy.SourceEventId.HasValue ? 1 : 0);

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }

    internal static AccountingPolicyRulePackDto NormalizeRulePack(
        AccountingPolicyRulePackDto? rulePack,
        string policyId,
        string version)
    {
        var normalizedPolicyId = RequireText(policyId, nameof(policyId));
        var normalizedVersion = RequireText(version, nameof(version));
        if (rulePack is null)
        {
            return BuildDefaultRulePack(normalizedPolicyId, normalizedVersion, AccountingTreatmentKindDto.General);
        }

        var rulePackId = RequireText(rulePack.RulePackId, nameof(rulePack.RulePackId));
        var rulePackVersion = RequireText(rulePack.RulePackVersion, nameof(rulePack.RulePackVersion));
        var seenRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rules = new List<AccountingPolicyRuleDto>();
        foreach (var rule in rulePack.Rules ?? [])
        {
            ArgumentNullException.ThrowIfNull(rule);
            var ruleId = RequireText(rule.RuleId, nameof(rule.RuleId));
            if (!seenRuleIds.Add(ruleId))
            {
                throw new ArgumentException($"Accounting policy rule '{ruleId}' is duplicated.", nameof(rulePack));
            }

            rules.Add(rule with
            {
                RuleId = ruleId,
                RuleVersion = string.IsNullOrWhiteSpace(rule.RuleVersion) ? rulePackVersion : rule.RuleVersion.Trim(),
                SourceEventType = NormalizeOptional(rule.SourceEventType),
                JournalTemplateId = NormalizeOptional(rule.JournalTemplateId),
                Description = NormalizeOptional(rule.Description),
                Tags = rule.Tags?.Where(static tag => !string.IsNullOrWhiteSpace(tag)).Select(static tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            });
        }

        if (rules.Count == 0)
        {
            throw new ArgumentException("Accounting policy rule pack must contain at least one rule.", nameof(rulePack));
        }

        return new AccountingPolicyRulePackDto(
            rulePackId,
            rulePackVersion,
            rules,
            NormalizeOptional(rulePack.Description));
    }

    private static AccountingPolicyRulePackDto BuildDefaultRulePack(
        string policyId,
        string version,
        AccountingTreatmentKindDto treatmentKind)
    {
        var ruleId = $"{policyId}.default";
        return new AccountingPolicyRulePackDto(
            $"{policyId}.rules",
            version,
            [
                new AccountingPolicyRuleDto(
                    ruleId,
                    treatmentKind,
                    RuleVersion: version,
                    SourceEventType: "ManualJournalEntry",
                    RequiresEvidence: true,
                    RequiresApproval: true,
                    AllowsAutoPosting: false,
                    Description: "Default governed journal treatment. Legacy JSON rules remain available for compatibility.")
            ]);
    }
}

public sealed class AccountingBasisProjectionService(IAccountingPolicyService accountingPolicyService) : IAccountingBasisProjectionService
{
    private readonly IAccountingPolicyService _accountingPolicyService =
        accountingPolicyService ?? throw new ArgumentNullException(nameof(accountingPolicyService));

    public async Task<AccountingBasisProjectionResult> ProjectAsync(AccountingBasisProjectionRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceEntry);

        var policy = await _accountingPolicyService.ResolvePolicyAsync(
                new AccountingPolicyQuery(
                    request.AccountingBasis,
                    request.EffectiveDate,
                    request.PolicyId,
                    request.FundProfileId,
                    request.FundStructureNodeId,
                    request.InstrumentId,
                    request.SourceEventId),
                ct)
            .ConfigureAwait(false);

        var write = new LedgerJournalEntryWrite(
            request.SourceEntry,
            request.AggregateId,
            request.PeriodId,
            request.CommandId,
            request.CorrelationId,
            request.AccountingBasis,
            policy.PolicyId,
            policy.Version,
            request.RuleId,
            request.RuleVersion ?? policy.Version,
            request.SourceEventId,
            request.SourceJournalEntryId,
            request.PostingKind,
            request.AdjustmentApproval,
            request.PostingCommand,
            request.LedgerBookId);

        return new AccountingBasisProjectionResult(policy, write);
    }
}

using System.Globalization;
using System.Text;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Instruments.AssetOperations;

/// <summary>
/// Creates the immutable boundary object emitted by a promoted accounting-rule mapper. The hash
/// binds the exact corporate-action posting intent to the selected rule, scoped book, generated
/// lines, dimensions, and component reconciliation manifest. It is an integrity attestation, not
/// posting authority; approval and posting remain owned by Financial Operations and Ledger.
/// </summary>
public static class CorporateActionMappedAccountingEffectAttestor
{
    private const string MappingSchema = "corporate-action-effect-mapping/v1";

    public static CorporateActionMappedAccountingEffectDto Create(
        CorporateActionAccountingProjectionDto projection,
        AssetAccountingEventScopeDto scope,
        ProjectedAccountingEffectDto effect,
        AccountingRulePackReferenceDto accountingRulePack,
        IReadOnlyList<CorporateActionPostingComponentLineMappingDto> componentLineMappings)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(accountingRulePack);
        ArgumentNullException.ThrowIfNull(componentLineMappings);

        if (!Sha256Digest.IsCanonical(projection.PostingIntentHash))
        {
            throw new ArgumentException(
                "A canonical corporate-action posting-intent hash is required.",
                nameof(projection));
        }

        var normalizedMappings = componentLineMappings
            .OrderBy(static mapping => mapping.ComponentIndex)
            .Select(static mapping => mapping with
            {
                Allocations = mapping.Allocations
                    .OrderBy(static allocation => allocation.EffectLineIndex)
                    .ToArray(),
                MappingRole = mapping.MappingRole?.Trim()
            })
            .ToArray();

        if (!HasCompleteMapping(
                projection.PostingSet?.Components ?? [],
                effect.Lines,
                normalizedMappings))
        {
            throw new ArgumentException(
                "Every posting component and generated effect line must have an exact reconciliation mapping.",
                nameof(componentLineMappings));
        }

        return new CorporateActionMappedAccountingEffectDto(
            effect,
            accountingRulePack,
            projection.PostingIntentHash!,
            ComputeMappingHash(
                projection.PostingIntentHash!,
                scope,
                effect,
                accountingRulePack,
                normalizedMappings),
            normalizedMappings);
    }

    public static bool HasCompleteMapping(
        IReadOnlyList<CorporateActionPostingComponentDto> components,
        IReadOnlyList<ProjectedAccountingEffectLineDto> lines,
        IReadOnlyList<CorporateActionPostingComponentLineMappingDto> mappings)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(mappings);

        if (components.Count == 0 || lines.Count == 0 || mappings.Count != components.Count)
        {
            return false;
        }

        var mappedLines = new HashSet<int>();
        var mappedComponents = new HashSet<int>();
        var allocatedAmountsByLine = new decimal[lines.Count];
        foreach (var mapping in mappings)
        {
            if (mapping.ComponentIndex < 0 ||
                mapping.ComponentIndex >= components.Count ||
                !mappedComponents.Add(mapping.ComponentIndex) ||
                mapping.ComponentKind != components[mapping.ComponentIndex].Kind ||
                string.IsNullOrWhiteSpace(mapping.MappingRole) ||
                mapping.Allocations.Count == 0 ||
                mapping.Allocations.Select(static allocation => allocation.EffectLineIndex).Distinct().Count() !=
                    mapping.Allocations.Count ||
                mapping.Allocations.Any(allocation =>
                    allocation.EffectLineIndex < 0 ||
                    allocation.EffectLineIndex >= lines.Count ||
                    allocation.ComponentAmount <= 0m ||
                    allocation.ComponentAmount >
                    lines[allocation.EffectLineIndex].Debit + lines[allocation.EffectLineIndex].Credit) ||
                mapping.Allocations.Sum(static allocation => allocation.ComponentAmount) !=
                    components[mapping.ComponentIndex].Amount)
            {
                return false;
            }

            foreach (var allocation in mapping.Allocations)
            {
                mappedLines.Add(allocation.EffectLineIndex);
                allocatedAmountsByLine[allocation.EffectLineIndex] += allocation.ComponentAmount;
            }
        }

        if (mappedComponents.Count != components.Count || mappedLines.Count != lines.Count)
        {
            return false;
        }

        // Component reconciliation is two-dimensional: every component must be fully allocated,
        // and the combined allocations landing on each generated line must equal that line's
        // absolute amount. Without the second check, multiple components can over-allocate one
        // line while merely touching another, yet still produce an apparently complete manifest.
        return lines.Select((line, index) => (line, index)).All(item =>
            allocatedAmountsByLine[item.index] == item.line.Debit + item.line.Credit);
    }

    public static string ComputeMappingHash(
        string postingIntentHash,
        AssetAccountingEventScopeDto scope,
        ProjectedAccountingEffectDto effect,
        AccountingRulePackReferenceDto accountingRulePack,
        IReadOnlyList<CorporateActionPostingComponentLineMappingDto> componentLineMappings)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(accountingRulePack);
        ArgumentNullException.ThrowIfNull(componentLineMappings);

        var builder = new StringBuilder(2048);
        Append(builder, MappingSchema);
        Append(builder, postingIntentHash);
        Append(builder, scope.SecurityId.ToString("N"));
        Append(builder, Invariant(scope.ExpectedSecurityVersion));
        Append(builder, scope.BookPositionId.ToString("N"));
        Append(builder, Invariant(scope.ExpectedBookPositionVersion));
        Append(builder, scope.LedgerBookId.ToString("N"));
        Append(builder, scope.PeriodId.ToString("N"));
        Append(builder, scope.AccountingBasis.ToString());
        Append(builder, scope.FundProfileId?.Trim());
        Append(builder, scope.TenantId?.Trim());
        Append(builder, scope.CompanyId?.Trim());
        AppendDimensions(builder, scope.Dimensions);

        Append(builder, accountingRulePack.RulePackId?.Trim());
        Append(builder, accountingRulePack.RulePackVersion?.Trim());
        Append(builder, accountingRulePack.SelectedRuleId?.Trim());
        Append(builder, accountingRulePack.SelectedRuleVersion?.Trim());

        Append(builder, effect.ProjectionRunId.ToString("N"));
        Append(builder, effect.ModelKey?.Trim());
        Append(builder, effect.ModelVersion?.Trim());
        Append(builder, effect.ProjectionAsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(builder, Invariant(effect.TotalDebits));
        Append(builder, Invariant(effect.TotalCredits));
        Append(builder, effect.Currency?.Trim().ToUpperInvariant());
        for (var index = 0; index < effect.Lines.Count; index++)
        {
            var line = effect.Lines[index];
            Append(builder, Invariant(index));
            Append(builder, line.AccountId?.Trim());
            Append(builder, Invariant(line.Debit));
            Append(builder, Invariant(line.Credit));
            Append(builder, line.Currency?.Trim().ToUpperInvariant());
            Append(builder, line.Description?.Trim());
            AppendDimensions(builder, line.Dimensions);
        }

        foreach (var mapping in componentLineMappings
                     .OrderBy(static mapping => mapping.ComponentIndex)
                     .ThenBy(static mapping => mapping.ComponentKind)
                     .ThenBy(static mapping => mapping.MappingRole, StringComparer.Ordinal))
        {
            Append(builder, Invariant(mapping.ComponentIndex));
            Append(builder, mapping.ComponentKind.ToString());
            Append(builder, mapping.MappingRole?.Trim());
            foreach (var allocation in mapping.Allocations
                         .OrderBy(static allocation => allocation.EffectLineIndex))
            {
                Append(builder, Invariant(allocation.EffectLineIndex));
                Append(builder, Invariant(allocation.ComponentAmount));
            }
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    private static void AppendDimensions(StringBuilder builder, LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            Append(builder, null);
            return;
        }

        Append(builder, dimensions.FundId?.Trim());
        Append(builder, dimensions.EntityId?.Trim());
        Append(builder, dimensions.SleeveId?.Trim());
        Append(builder, dimensions.StrategyId?.Trim());
        Append(builder, dimensions.InvestorId?.Trim());
        Append(builder, dimensions.CapitalAccountId?.Trim());
        Append(builder, dimensions.InstrumentId?.ToString("N"));
        Append(builder, dimensions.TaxLotId?.Trim());
        Append(builder, dimensions.CostCenterId?.Trim());
        Append(builder, dimensions.CounterpartyId?.Trim());
        Append(builder, dimensions.OrganizationId?.Trim());
        Append(builder, dimensions.PortfolioId?.Trim());
        Append(builder, dimensions.BookId?.Trim());
        Append(builder, dimensions.AccountId?.Trim());
        Append(builder, dimensions.CustomerId?.Trim());
        Append(builder, dimensions.VendorId?.Trim());
        Append(builder, dimensions.ProjectId?.Trim());
        Append(builder, dimensions.PositionId?.ToString("N"));
        foreach (var pair in dimensions.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            Append(builder, pair.Key.Trim());
            Append(builder, pair.Value?.Trim());
        }
    }

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static void Append(StringBuilder builder, string? value)
    {
        var token = value ?? "<null>";
        builder.Append(token.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(token);
        builder.Append(';');
    }
}

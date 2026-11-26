using System.Collections.ObjectModel;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityMasterMapping
{
    private sealed record AssetPackDecodeContext(
        string AssetClass,
        JsonElement Json,
        JsonElement Terms,
        SchemaBoundTerms Root,
        SchemaBoundTerms Resolved,
        SecurityKindMappingMode Mode,
        SecurityVersionedTermDecoding VersionedTerms,
        SecurityEconomicScheduleValidation? EconomicSchedules);

    private sealed record AssetPackDecoderContribution(
        string AssetClass,
        Func<AssetPackDecodeContext, SecurityKind> Decode);

    // Generated contributions come from the pack's binding rows. Only the profile-owned document
    // and Bond's existing diagnostic-order hook need handwritten composition in this layer.
    private static readonly IReadOnlyDictionary<string, AssetPackDecoderContribution> AssetPackDecoders =
        BuildAssetPackDecoders();

    internal static IReadOnlyList<string> AssetPackDecodeClasses => AssetPackDecoders.Keys.ToArray();

    private static IReadOnlyDictionary<string, AssetPackDecoderContribution> BuildAssetPackDecoders()
    {
        var contributions = CreateGeneratedAssetPackDecoders()
            .Append(new AssetPackDecoderContribution("CustomAsset", context => ToCustomAssetKind(context.Root, context.Mode)))
            .ToArray();
        EnsureAssetPackCodecCoverage(contributions.Select(static contribution => contribution.AssetClass));

        var lookup = new Dictionary<string, AssetPackDecoderContribution>(StringComparer.Ordinal);
        foreach (var contribution in contributions)
        {
            lookup.Add(contribution.AssetClass, SecurityAssetClassCatalog.GetOrDefault(contribution.AssetClass).CarriesBondCouponStructure
                ? contribution with
                {
                    Decode = context => DecodeBond(context.Root, context.Mode, context.AssetClass,
                        context.Json, context.VersionedTerms, context.EconomicSchedules!)
                }
                : contribution);
        }

        return new ReadOnlyDictionary<string, AssetPackDecoderContribution>(lookup);
    }

    /// <summary>
    /// A closed composition: every implemented catalog class has exactly one decoder. Planned pack
    /// classes and aliases cannot accidentally become executable codecs, and no duplicate can win.
    /// </summary>
    internal static void EnsureAssetPackCodecCoverage(IEnumerable<string> contributedClasses)
    {
        var classes = contributedClasses.ToArray();
        var duplicate = classes.GroupBy(static name => name, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() != 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate Security Master codec contribution '{duplicate.Key}'.");

        var missing = SecurityAssetClassCatalog.AssetClasses.Except(classes, StringComparer.Ordinal).ToArray();
        var unknown = classes.Except(SecurityAssetClassCatalog.AssetClasses, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0 || unknown.Length != 0)
            throw new InvalidOperationException(
                $"Security Master codec contributions do not match the implemented catalog. Missing: [{string.Join(", ", missing)}]; unknown: [{string.Join(", ", unknown)}].");
    }
}

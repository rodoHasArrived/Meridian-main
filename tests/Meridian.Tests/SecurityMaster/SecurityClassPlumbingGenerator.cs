using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

/// <summary>One generated region of class plumbing in committed source.</summary>
/// <param name="Id">The region id, as written in its <c>&lt;generated:id&gt;</c> markers.</param>
/// <param name="TargetPath">The repository-relative file that holds the region.</param>
/// <param name="Lines">The region body, one line per entry, without the markers or line terminators.</param>
internal sealed record SecurityClassPlumbingRegion(string Id, string TargetPath, IReadOnlyList<string> Lines);

/// <summary>
/// Generates the per-class plumbing regions from <see cref="SecurityAssetClassDeclarations"/>, with
/// codec arm parameters from <see cref="SecurityAssetTermsCodecBindings"/> (<c>W11-PACK-002</c>,
/// <c>DEC-PACK-CODEGEN-001</c>). The regions are committed source between
/// <c>&lt;generated:id&gt;</c> markers; <see cref="SecurityClassPlumbingGenerationTests"/> holds
/// each one equal to what this produces and, with <c>MERIDIAN_ACCEPT_GENERATED_CODEC=1</c>, writes
/// it instead.
/// </summary>
internal static class SecurityClassPlumbingGenerator
{
    public const string SecurityMasterFs = "src/Meridian.FSharp/Domain/SecurityMaster.fs";
    public const string InteropFs = "src/Meridian.FSharp/Interop.SecurityMaster.fs";
    public const string MappingCs = "src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs";
    public const string CatalogCs = "src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs";

    public static IReadOnlyList<SecurityClassPlumbingRegion> GenerateAll()
        => GenerateAll(SecurityAssetClassDeclarations.All, SecurityAssetTermsCodecBindings.ByAssetClass);

    public static IReadOnlyList<SecurityClassPlumbingRegion> GenerateAll(
        IReadOnlyList<SecurityAssetClassDeclaration> classes,
        IReadOnlyDictionary<string, SecurityClassCodecBinding> bindings)
        =>
        [
            new("security-kind-cases", SecurityMasterFs, classes.SelectMany(UnionCase).ToArray()),
            new("asset-class-descriptors", SecurityMasterFs, DescriptorRows(classes)),
            new("asset-class-keys", SecurityMasterFs, classes.Select(KeyOfArm).ToArray()),
            new("flat-writer-dispatch", InteropFs,
                classes.Where(static c => !c.HandWrittenWriterArm).Select(c => WriterArm(c, Binding(bindings, c))).ToArray()),
            new("decode-dispatch", MappingCs,
                classes.SelectMany(c => DecodeArm(c, bindings.GetValueOrDefault(c.Name))).ToArray()),
            new("asset-class-names", CatalogCs, classes.Select(NameConstant).ToArray()),
        ];

    /// <summary>The catalog row heads the declaration implies; the rows themselves stay hand-written.</summary>
    public static IReadOnlyList<string> CatalogRowHeads(IReadOnlyList<SecurityAssetClassDeclaration> classes)
        => classes.Select(static c => $"            AssetClass: SecurityAssetClassNames.{c.Name},").ToArray();

    private static SecurityClassCodecBinding Binding(
        IReadOnlyDictionary<string, SecurityClassCodecBinding> bindings, SecurityAssetClassDeclaration declaration)
        => bindings.TryGetValue(declaration.Name, out var binding)
            ? binding
            : throw new InvalidOperationException(
                $"{declaration.Name} has no codec binding, so its writer arm must be declared hand-written.");

    private static IEnumerable<string> UnionCase(SecurityAssetClassDeclaration c)
    {
        foreach (var doc in c.CaseDoc ?? [])
        {
            yield return $"    /// {doc}";
        }

        yield return $"    | {c.Name} of {c.TermsTypeName}";
    }

    private static string KeyOfArm(SecurityAssetClassDeclaration c) => $"        | SecurityKind.{c.Name} _ -> \"{c.Name}\"";

    private static string NameConstant(SecurityAssetClassDeclaration c) => $"    public const string {c.Name} = \"{c.Name}\";";

    private static string[] DescriptorRows(IReadOnlyList<SecurityAssetClassDeclaration> classes)
    {
        var lines = new List<string>(classes.Count * 2);
        for (var i = 0; i < classes.Count; i++)
        {
            var c = classes[i];
            var open = i == 0 ? "        [ { " : "          { ";
            var close = i == classes.Count - 1 ? " } ]" : " }";
            lines.Add($"{open}AssetClassName = \"{c.Name}\"; AssetClass = AssetClass.{c.AssetClass}; Family = {FamilyExpression(c.Family)}");
            lines.Add($"            SubType = {SubTypeExpression(c.SubType)}; IssuerType = {StringOption(c.IssuerType)}; "
                + $"RiskCountry = {StringOption(c.RiskCountry)}; IsDerivative = {(c.IsDerivative ? "true" : "false")}{close}");
        }

        return lines.ToArray();
    }

    private static string WriterArm(SecurityAssetClassDeclaration c, SecurityClassCodecBinding binding)
    {
        var flags = "";
        if (binding.VersionedSchedules || binding.WritesConventionsFlag)
        {
            flags += " (writesScheduleConventions ())";
        }

        if (binding.VersionedSchedules)
        {
            flags += " (writesCallPutSchedules ())";
        }

        return $"        | SecurityKind.{c.Name} terms -> GeneratedFlatWriter.write{c.Name} schemaVersion{flags} terms";
    }

    private static IEnumerable<string> DecodeArm(SecurityAssetClassDeclaration c, SecurityClassCodecBinding? binding)
    {
        foreach (var note in c.DecodeNote ?? [])
        {
            yield return $"            // {note}";
        }

        if (c.DecodeExpression is not null)
        {
            yield return $"            \"{c.Name}\" => {c.DecodeExpression},";
            yield break;
        }

        if (binding is null)
        {
            throw new InvalidOperationException(
                $"{c.Name} has no codec binding, so its decode arm must be declared as an expression.");
        }

        // The same parameter rules SecurityFlatCodecGenerator uses for the generated method signature.
        var arguments = binding.ReadsResolvedTerms ? "boundTerms" : "bound";
        arguments += ", mode, assetClass, json";
        if (binding.VersionedSchedules || binding.Inputs.HasFlag(SecurityCodecInputs.VersionedTerms))
        {
            arguments += ", versionedTerms";
        }

        if (binding.Inputs.HasFlag(SecurityCodecInputs.EconomicSchedules))
        {
            arguments += ", economicSchedules!";
        }

        if (binding.Inputs.HasFlag(SecurityCodecInputs.ResolvedTermsDocument))
        {
            arguments += ", terms";
        }

        yield return $"            \"{c.Name}\" => {c.DecodeMethod ?? "DecodeGenerated" + c.Name}({arguments}),";
    }

    private static readonly HashSet<string> NullaryFamilies = NullaryCases(typeof(AssetFamily));
    private static readonly HashSet<string> NullarySubTypes = NullaryCases(typeof(SecuritySubType));

    private static string FamilyExpression(string? family)
        => family is null ? "None"
            : NullaryFamilies.Contains(family) ? $"Some AssetFamily.{family}"
            : $"Some (AssetFamily.OtherFamily \"{family}\")";

    private static string SubTypeExpression(string subType)
        => NullarySubTypes.Contains(subType) ? $"SecuritySubType.{subType}" : $"SecuritySubType.OtherSubType \"{subType}\"";

    private static string StringOption(string? value) => value is null ? "None" : $"Some \"{value}\"";

    private static HashSet<string> NullaryCases(Type union)
        => FSharpType.GetUnionCases(union, null)
            .Where(static unionCase => unionCase.GetFields().Length == 0)
            .Select(static unionCase => unionCase.Name)
            .ToHashSet(StringComparer.Ordinal);
}

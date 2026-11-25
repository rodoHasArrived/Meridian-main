using System.Text;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

/// <summary>One generated piece of class plumbing and the hand-written source it stands for.</summary>
/// <param name="Name">A short name for the site.</param>
/// <param name="TargetPath">The repository-relative file that holds the hand-written original.</param>
/// <param name="Lines">The generated lines, one per array entry, without line terminators.</param>
/// <param name="OrderSensitive">
/// Whether line order is part of the comparison. Two sites are compared as sets: the C# decode
/// switch, whose arms match distinct string constants so order does not change what it does, and
/// the catalog row heads, whose public order differs from the union's.
/// </param>
internal sealed record SecurityClassPlumbingFragment(
    string Name,
    string TargetPath,
    IReadOnlyList<string> Lines,
    bool OrderSensitive = true);

/// <summary>
/// Prototype generator for <c>W11-PACK-002</c>. It emits the mechanical per-class plumbing (the F#
/// <c>SecurityKind</c> cases, the <c>AssetClassRegistry</c> key arms and classification rows, the
/// F# flat-writer dispatch, the C# decode dispatch, the class-name constants and the catalog row
/// heads) from one declaration per class: <see cref="SecurityClassPlumbingDeclarations"/>, which
/// lists the <see cref="SecurityAssetClassCatalog"/> classes in F# union order, with codec
/// parameters from <see cref="SecurityAssetTermsCodecBindings"/>.
/// <para>
/// Nothing is written to <c>src/</c>. <see cref="SecurityClassPlumbingGenerationTests"/> holds the
/// output equal to the hand-written source, so the hand-written source remains the source of
/// truth. Switching to generated code is a separate decision recorded under W11-PACK-002.
/// </para>
/// </summary>
internal static class SecurityClassPlumbingGenerator
{
    public const string SecurityMasterFs = "src/Meridian.FSharp/Domain/SecurityMaster.fs";
    public const string InteropFs = "src/Meridian.FSharp/Interop.SecurityMaster.fs";
    public const string MappingCs = "src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs";
    public const string CatalogCs = "src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs";

    public static IReadOnlyList<SecurityClassPlumbingFragment> GenerateAll()
        => GenerateAll(SecurityClassPlumbingDeclarations.All, SecurityAssetTermsCodecBindings.ByAssetClass);

    public static IReadOnlyList<SecurityClassPlumbingFragment> GenerateAll(
        IReadOnlyList<SecurityClassDeclaration> classes,
        IReadOnlyDictionary<string, SecurityClassCodecBinding> bindings)
        =>
        [
            new("SecurityKind union cases", SecurityMasterFs, classes.Select(UnionCase).ToArray()),
            new("AssetClassRegistry.keyOf arms", SecurityMasterFs, classes.Select(KeyOfArm).ToArray()),
            new("AssetClassRegistry.descriptors rows", SecurityMasterFs, DescriptorRows(classes)),
            new("Flat writer dispatch arms", InteropFs,
                classes.Where(static c => !c.HandWrittenWriterArm).Select(c => WriterArm(c, Binding(bindings, c))).ToArray()),
            new("ToSecurityKind decode arms", MappingCs,
                classes.Select(c => DecodeArm(c, bindings.GetValueOrDefault(c.Name))).ToArray(), OrderSensitive: false),
            new("SecurityAssetClassNames constants", CatalogCs, classes.Select(NameConstant).ToArray()),
            // The catalog's rows are not in union order (CustomAsset follows OtherSecurity), and its
            // AssetClasses order is public, so the heads are compared as a set.
            new("SecurityAssetClassCatalog row heads", CatalogCs, classes.Select(CatalogRowHead).ToArray(), OrderSensitive: false),
        ];

    /// <summary>The whole output as one text, for review of what the generator would emit.</summary>
    public static string Render(IReadOnlyList<SecurityClassPlumbingFragment> fragments)
    {
        var text = new StringBuilder();
        foreach (var fragment in fragments)
        {
            text.Append("// ").Append(fragment.Name).Append(" -> ").Append(fragment.TargetPath).Append('\n');
            foreach (var line in fragment.Lines)
            {
                text.Append(line).Append('\n');
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static SecurityClassCodecBinding Binding(
        IReadOnlyDictionary<string, SecurityClassCodecBinding> bindings, SecurityClassDeclaration declaration)
        => bindings.TryGetValue(declaration.Name, out var binding)
            ? binding
            : throw new InvalidOperationException(
                $"{declaration.Name} has no codec binding, so its writer and decode arms must be declared as hand-written.");

    private static string UnionCase(SecurityClassDeclaration c) => $"    | {c.Name} of {c.TermsTypeName}";

    private static string KeyOfArm(SecurityClassDeclaration c) => $"        | SecurityKind.{c.Name} _ -> \"{c.Name}\"";

    private static string NameConstant(SecurityClassDeclaration c) => $"    public const string {c.Name} = \"{c.Name}\";";

    private static string CatalogRowHead(SecurityClassDeclaration c) => $"            AssetClass: SecurityAssetClassNames.{c.Name},";

    private static string[] DescriptorRows(IReadOnlyList<SecurityClassDeclaration> classes)
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

    private static string WriterArm(SecurityClassDeclaration c, SecurityClassCodecBinding binding)
    {
        var flags = new StringBuilder();
        if (binding.VersionedSchedules || binding.WritesConventionsFlag)
        {
            flags.Append(" (writesScheduleConventions ())");
        }

        if (binding.VersionedSchedules)
        {
            flags.Append(" (writesCallPutSchedules ())");
        }

        return $"        | SecurityKind.{c.Name} terms -> GeneratedFlatWriter.write{c.Name} schemaVersion{flags} terms";
    }

    private static string DecodeArm(SecurityClassDeclaration c, SecurityClassCodecBinding? binding)
    {
        if (c.DecodeExpression is not null)
        {
            return $"\"{c.Name}\" => {c.DecodeExpression},";
        }

        if (binding is null)
        {
            throw new InvalidOperationException(
                $"{c.Name} has no codec binding, so its decode arm must be declared as an expression.");
        }

        // The same parameter rules SecurityFlatCodecGenerator uses for the generated method signature.
        var arguments = new StringBuilder(binding.ReadsResolvedTerms ? "boundTerms" : "bound");
        arguments.Append(", mode, assetClass, json");
        if (binding.VersionedSchedules || binding.Inputs.HasFlag(SecurityCodecInputs.VersionedTerms))
        {
            arguments.Append(", versionedTerms");
        }

        if (binding.Inputs.HasFlag(SecurityCodecInputs.EconomicSchedules))
        {
            arguments.Append(", economicSchedules!");
        }

        if (binding.Inputs.HasFlag(SecurityCodecInputs.ResolvedTermsDocument))
        {
            arguments.Append(", terms");
        }

        return $"\"{c.Name}\" => {c.DecodeMethod ?? "DecodeGenerated" + c.Name}({arguments}),";
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

using System.Reflection;
using System.Text;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Generates both arms of the flat codec from <see cref="SecurityAssetTermsCodecBindings"/>: the
/// C# decode arms and the F# writer arms, for the classes the table lists. The record shapes are
/// read from the F# types and key requiredness from <see cref="SecurityAssetTermsSchema"/>, so the
/// table states only which key carries which member. The output is committed source;
/// <see cref="SecurityFlatCodecGenerationTests"/> holds it equal to what this produces.
/// </summary>
internal static class SecurityFlatCodecGenerator
{
    public const string DecodePath = "src/Meridian.Application/SecurityMaster/SecurityMasterMapping.GeneratedDecode.cs";
    public const string WriterPath = "src/Meridian.FSharp/Interop.SecurityMaster.GeneratedWriter.fs";

    private const string Banner =
        "Generated from SecurityAssetTermsCodecBindings by SecurityFlatCodecGenerator. Do not edit:\n" +
        "change the binding table and regenerate (MERIDIAN_ACCEPT_GENERATED_CODEC=1, then run\n" +
        "SecurityFlatCodecGenerationTests).";

    public static string GenerateDecode()
    {
        var source = new StringBuilder();
        foreach (var line in Banner.Split('\n'))
        {
            source.Append("// ").Append(line).Append('\n');
        }

        source.Append("using System.Text.Json;\n");
        source.Append("using Meridian.Contracts.SecurityMaster;\n");
        source.Append("using Meridian.FSharp.Domain;\n");
        source.Append("using Microsoft.FSharp.Core;\n\n");
        source.Append("namespace Meridian.Application.SecurityMaster;\n\n");
        source.Append("internal static partial class SecurityMasterMapping\n{\n");
        source.Append("    /// <summary>The classes whose decode arm is generated from the binding table.</summary>\n");
        source.Append("    internal static IReadOnlyList<string> GeneratedDecodeClasses { get; } =\n    [\n");
        foreach (var binding in SecurityAssetTermsCodecBindings.Classes)
        {
            source.Append("        \"").Append(binding.AssetClass).Append("\",\n");
        }

        source.Append("    ];\n");
        foreach (var binding in SecurityAssetTermsCodecBindings.Classes)
        {
            var shape = Shape(binding);
            source.Append('\n');
            source.Append("    private static SecurityKind DecodeGenerated").Append(binding.AssetClass)
                .Append("(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)\n");
            source.Append("        => SecurityKind.New").Append(binding.AssetClass).Append("(new ").Append(shape.TermsType.Name).Append("(\n");
            for (var f = 0; f < shape.Fields.Length; f++)
            {
                var field = shape.Fields[f];
                var last = f == shape.Fields.Length - 1;
                if (field.IsRateConvention)
                {
                    source.Append("            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))");
                }
                else
                {
                    source.Append("            new ").Append(field.ModuleType.Name).Append("(\n");
                    for (var m = 0; m < field.Members.Length; m++)
                    {
                        source.Append("                ").Append(DecodeExpression(binding, field, field.Members[m]));
                        source.Append(m == field.Members.Length - 1 ? ")" : ",\n");
                    }
                }

                source.Append(last ? "));\n" : ",\n");
            }
        }

        source.Append("}\n");
        return source.ToString();
    }

    public static string GenerateWriter()
    {
        var source = new StringBuilder();
        foreach (var line in Banner.Split('\n'))
        {
            source.Append("// ").Append(line).Append('\n');
        }

        source.Append("namespace Meridian.FSharp.SecurityMasterInterop\n\n");
        source.Append("open System.Text.Json\n");
        source.Append("open Meridian.FSharp.Domain\n\n");
        source.Append("/// The flat writer arms generated from the binding table: one function per class, each the\n");
        source.Append("/// class's unstamped asset-specific terms document.\n");
        source.Append("[<RequireQualifiedAccess>]\n");
        source.Append("module internal GeneratedFlatWriter =\n");
        foreach (var binding in SecurityAssetTermsCodecBindings.Classes)
        {
            var shape = Shape(binding);
            source.Append('\n');
            source.Append("    let write").Append(binding.AssetClass).Append(" (schemaVersion: int) (terms: ")
                .Append(shape.TermsType.Name).Append(") : string =\n");
            source.Append("        JsonSerializer.Serialize(\n");
            source.Append("            {| schemaVersion = schemaVersion");
            foreach (var row in binding.Members.Where(static row => row.Key is not null && !row.DecodeOnly))
            {
                source.Append("\n               ").Append(row.Key).Append(" = ").Append(WriteExpression(binding, shape, row));
            }

            source.Append(" |})\n");
        }

        return source.ToString();
    }

    private static string DecodeExpression(SecurityClassCodecBinding binding, FieldShape field, PropertyInfo member)
    {
        var path = $"{field.Name}.{member.Name}";
        var rows = binding.Members.Where(row => row.Path == path).ToArray();
        if (rows.Length > 1)
        {
            throw new InvalidOperationException($"{binding.AssetClass} binds {path} more than once.");
        }

        var isOption = IsOption(member.PropertyType);
        if (rows.Length == 0)
        {
            return isOption
                ? "null"
                : throw new InvalidOperationException($"{binding.AssetClass} leaves the non-optional member {path} unbound.");
        }

        var row = rows[0];
        if (row.DecodeExpression is not null)
        {
            return row.DecodeExpression;
        }

        var key = $"\"{row.Key}\"";
        var read = row.Read ?? throw new InvalidOperationException($"{binding.AssetClass} {path} names neither a read nor an expression.");
        RequireDeclared(binding, row, read);
        return read switch
        {
            SecurityTermRead.RequiredString => isOption
                ? $"FSharpOption<string>.Some(bound.RequiredString({key}))"
                : $"bound.RequiredString({key})",
            SecurityTermRead.OptionalString => Optional(isOption, path, $"ToOption(bound.OptionalString({key}))"),
            SecurityTermRead.ConventionString => Optional(isOption, path, $"ToOption(bound.OptionalConventionString({key}))"),
            SecurityTermRead.RequiredDecimal => isOption ? $"ToOption(bound.RequiredDecimal({key}))" : $"bound.RequiredDecimal({key})",
            SecurityTermRead.OptionalDecimal => Optional(isOption, path, $"ToOption(bound.OptionalDecimal({key}))"),
            SecurityTermRead.StrictDecimal => Optional(isOption, path, $"ToOption(bound.OptionalStrictDecimal({key}))"),
            SecurityTermRead.RequiredInt => isOption ? $"ToOption(bound.RequiredInt({key}))" : $"bound.RequiredInt({key})",
            SecurityTermRead.OptionalInt => Optional(isOption, path, $"ToOption(bound.OptionalInt({key}))"),
            SecurityTermRead.StrictInt => Optional(isOption, path, $"ToOption(bound.OptionalStrictInt({key}))"),
            SecurityTermRead.RequiredDate => isOption ? $"ToOption(bound.RequiredDateOnly({key}))" : $"bound.RequiredDateOnly({key})",
            SecurityTermRead.OptionalDate => Optional(isOption, path, $"ToOption(bound.OptionalDateOnly({key}))"),
            SecurityTermRead.StrictDate => Optional(isOption, path, $"ToOption(bound.OptionalStrictDateOnly({key}))"),
            SecurityTermRead.RequiredSecurityId => Optional(isOption, path, $"FSharpOption<SecurityId>.Some(SecurityId.NewSecurityId(bound.RequiredGuid({key})))"),
            SecurityTermRead.OptionalBoolean => Optional(isOption, path, $"ToOption(bound.OptionalBoolean({key}))"),
            SecurityTermRead.BooleanDefaultFalse => Optional(isOption, path, $"ToOption(bound.OptionalBoolean({key}) ?? false)"),
            SecurityTermRead.DayCount => Optional(isOption, path, row.Key == "dayCount"
                ? $"ToDayCountOption(bound.OptionalConventionString({key}), mode)"
                : $"ToDayCountOption(bound.OptionalConventionString({key}), mode, {key})"),
            _ => throw new InvalidOperationException($"Unhandled read {read}.")
        };
    }

    private static string Optional(bool isOption, string path, string expression)
        => isOption ? expression : throw new InvalidOperationException($"{path} is not optional but is bound to an optional read.");

    private static string WriteExpression(SecurityClassCodecBinding binding, ClassShape shape, SecurityTermBinding row)
    {
        if (row.WriteVia is not null)
        {
            return $"terms.{row.WriteVia}";
        }

        var parts = row.Path.Split('.');
        var field = shape.Fields.SingleOrDefault(candidate => candidate.Name == parts[0])
                    ?? throw new InvalidOperationException($"{binding.AssetClass} has no record field {parts[0]}.");
        var member = field.Members.SingleOrDefault(candidate => candidate.Name == parts[1])
                     ?? throw new InvalidOperationException($"{binding.AssetClass} {parts[0]} has no member {parts[1]}.");
        var isOption = IsOption(member.PropertyType);
        var value = $"terms.{row.Path}";
        return row.Read switch
        {
            SecurityTermRead.RequiredSecurityId => $"(let (SecurityId value) = ({value} |> Option.get) in value)",
            SecurityTermRead.BooleanDefaultFalse => $"({value} |> Option.defaultValue false)",
            SecurityTermRead.DayCount => $"{value} |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel",
            SecurityTermRead.RequiredString or SecurityTermRead.RequiredDecimal or SecurityTermRead.RequiredInt
                or SecurityTermRead.RequiredDate when isOption => $"({value} |> Option.get)",
            _ => value
        };
    }

    /// <summary>A row's read must agree with the schema on the key's existence and requiredness.</summary>
    private static void RequireDeclared(SecurityClassCodecBinding binding, SecurityTermBinding row, SecurityTermRead read)
    {
        var field = SecurityAssetTermsSchema.Field(binding.AssetClass, row.Key!)
                    ?? throw new InvalidOperationException($"{binding.AssetClass} binds undeclared key '{row.Key}'.");
        var readsRequired = read is SecurityTermRead.RequiredString or SecurityTermRead.RequiredDecimal
            or SecurityTermRead.RequiredInt or SecurityTermRead.RequiredDate or SecurityTermRead.RequiredSecurityId;
        if (readsRequired && !field.Required)
        {
            throw new InvalidOperationException($"{binding.AssetClass}.{row.Key} is read as required but the schema declares it optional.");
        }
    }

    private sealed record FieldShape(string Name, Type ModuleType, PropertyInfo[] Members, bool IsRateConvention);

    private sealed record ClassShape(Type TermsType, FieldShape[] Fields);

    private static ClassShape Shape(SecurityClassCodecBinding binding)
    {
        var unionCase = FSharpType.GetUnionCases(typeof(SecurityKind), null)
                            .SingleOrDefault(candidate => candidate.Name == binding.AssetClass)
                        ?? throw new InvalidOperationException($"SecurityKind has no case {binding.AssetClass}.");
        var termsType = unionCase.GetFields().Single().PropertyType;
        var fields = FSharpType.GetRecordFields(termsType, null)
            .Select(static field =>
            {
                var optional = IsOption(field.PropertyType);
                var moduleType = optional ? field.PropertyType.GetGenericArguments()[0] : field.PropertyType;
                var rateConvention = optional && moduleType == typeof(RateConventionTerms);
                return optional && !rateConvention
                    ? throw new InvalidOperationException($"{field.Name} is an optional module the generator has no rule for.")
                    : new FieldShape(field.Name, moduleType, FSharpType.GetRecordFields(moduleType, null), rateConvention);
            })
            .ToArray();
        foreach (var row in binding.Members)
        {
            var parts = row.Path.Split('.');
            if (parts.Length != 2
                || fields.SingleOrDefault(field => field.Name == parts[0]) is not { } owner
                || owner.Members.All(member => member.Name != parts[1]))
            {
                throw new InvalidOperationException($"{binding.AssetClass} binds {row.Path}, which the record does not have.");
            }
        }

        return new ClassShape(termsType, fields);
    }

    private static bool IsOption(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(FSharpOption<>);
}

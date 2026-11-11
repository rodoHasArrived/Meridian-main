using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Application.SecurityMaster;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Ratchet for ADR-027 stage 3 (S3-D): every catalog class decodes its scalar terms through the
/// schema-bound reader, and a read the schema cannot bind goes through the reader's ledgered view
/// (<see cref="SecurityMasterMapping.UnboundCompatibilityReads"/>), which refuses an unledgered
/// read at run time. The one way around both is a raw reader call on the terms document itself,
/// which no run-time check can see, so this guard holds the two places a document is in scope:
/// <list type="bullet">
/// <item>the decode switch, by source — its only raw scalar reads are the unrecognized-class
/// salvage, which has no schema to bind to;</item>
/// <item>the per-class decoders the switch delegates to, by signature — they take the bound view,
/// which never hands out the document, and no document beside it.</item>
/// </list>
/// The structural readers (schedules and their entries, the schedule-conventions block, swap
/// legs, covenants) are bespoke by design and outside this guard.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class SecurityMasterSchemaBoundDecodeRatchetTests
{
    private const string MappingSource = "src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs";
    private const string SwitchStart = "private static SecurityKind ToSecurityKind(";
    private const string SwitchEnd = "EnsureDeclaredVocabulariesOnWrite(assetClass, json, mode, versionedTerms);";

    // A class this node has no decoder for reads as OtherSecurity with the raw class as its
    // category. It is not a catalog class, so there is no schema entry to bind these reads to.
    private static readonly string[] UnrecognizedClassSalvageReads =
    [
        "GetOptionalString(json, \"subType\")",
        "GetOptionalDateOnly(json, \"maturity\")",
        "GetOptionalString(json, \"issuerName\")",
        "GetOptionalString(json, \"settlementType\")"
    ];

    private static readonly Regex RawScalarRead = new(
        @"\bGet(?:Optional|Required)(?:String|Decimal|Int|Boolean|DateOnly|Guid|DateTimeOffset)\([^)]*\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex RawPropertyRead = new(@"\.(?:Try)?GetProperty\(", RegexOptions.CultureInvariant);

    [Fact]
    public void DecodeSwitch_MakesNoRawScalarRead_ExceptTheUnrecognizedClassSalvage()
    {
        var body = DecodeSwitchBody();

        RawScalarRead.Matches(body).Select(static match => match.Value).Should().Equal(
            UnrecognizedClassSalvageReads,
            "a class arm reads scalars through the schema-bound view (bound / boundTerms), or through its Unbound "
            + "view when the read is ledgered in UnboundCompatibilityReads; a raw read of the document skips both");
        RawPropertyRead.IsMatch(body).Should().BeFalse(
            "a class arm must not reach into the terms document directly; read through the schema-bound view");
    }

    [Theory]
    [InlineData("ToBondTerms")]
    [InlineData("ToCustomAssetKind")]
    [InlineData("ToEquityClassificationOption")]
    [InlineData("ToPreferredTerms")]
    [InlineData("ToConvertibleTerms")]
    [InlineData("ToParticipationTermsOption")]
    [InlineData("ToLiquidationPreference")]
    public void ClassDecoder_TakesTheSchemaBoundView_AndNoDocument(string decoder)
    {
        var overloads = typeof(SecurityMasterMapping)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == decoder)
            .ToArray();

        overloads.Should().NotBeEmpty($"{decoder} was renamed or removed; keep this guard pointed at the per-class decoders");
        foreach (var parameters in overloads.Select(static method => method.GetParameters().Select(static parameter => parameter.ParameterType).ToArray()))
        {
            parameters.Select(static type => (Nullable.GetUnderlyingType(type) ?? type).Name).Should().Contain(
                "SchemaBoundTerms", $"{decoder} decodes class terms, so it reads them through the schema-bound view");
            parameters.Should().NotContain(
                static type => (Nullable.GetUnderlyingType(type) ?? type) == typeof(JsonElement),
                $"{decoder} must not take the terms document: a raw read of it is neither schema-bound nor ledgered");
        }
    }

    private static string DecodeSwitchBody()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), MappingSource));
        var start = source.IndexOf(SwitchStart, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{MappingSource} no longer declares '{SwitchStart}'; re-anchor this guard on the decode switch");
        source.IndexOf(SwitchStart, start + 1, StringComparison.Ordinal).Should().Be(-1, "the decode switch anchor must be unambiguous");

        var end = source.IndexOf(SwitchEnd, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"the decode switch no longer ends at '{SwitchEnd}'; re-anchor this guard");
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate Meridian repository root from test output directory.");
    }
}

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Reporting;

public sealed class ReportingIncomeComparisonSerializationTests
{
    private static readonly ReportingIncomeComparisonJsonContext Json = ReportingIncomeComparisonJsonContext.Default;
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Book = Guid.Parse("7ca6c1ef-81af-4b2a-8136-c3b3bfdba541");

    [Theory]
    [InlineData("9007199254740993.123456", "9007199254740993.123456")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("0.0000000000000000000000000001", "0.0000000000000000000000000001")]
    [InlineData("-0.0000000000000000000000000001", "-0.0000000000000000000000000001")]
    [InlineData("123.450000", "123.45")]
    [InlineData("-0.000000", "0")]
    public async Task HttpAmounts_AreCanonicalExactDecimalStrings(string input, string expected)
    {
        var amount = decimal.Parse(input, CultureInfo.InvariantCulture);
        var comparison = Envelope().Comparison;
        comparison = comparison with
        {
            BaselineAmount = amount,
            CurrentAmount = amount,
            Movement = amount,
            ExplainedAmount = amount,
            ResidualAmount = amount,
            Contributions = [comparison.Contributions[0] with { Amount = amount }]
        };
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await Results.Json(comparison, Json.ReportingIncomeComparisonDto).ExecuteAsync(context);

        using var document = JsonDocument.Parse(body.ToArray());
        foreach (var property in new[] { "baselineAmount", "currentAmount", "movement", "explainedAmount", "residualAmount" })
        {
            document.RootElement.GetProperty(property).ValueKind.Should().Be(JsonValueKind.String);
            document.RootElement.GetProperty(property).GetString().Should().Be(expected);
        }
        document.RootElement.GetProperty("contributions")[0].GetProperty("amount").GetString().Should().Be(expected);
        JsonSerializer.Deserialize(body.ToArray(), Json.ReportingIncomeComparisonDto).Should().BeEquivalentTo(comparison);
    }

    [Fact]
    public void NullableAmounts_RemainNullAndZeroRemainsAnExactString()
    {
        var comparison = Envelope().Comparison with
        {
            BaselineAmount = null,
            CurrentAmount = null,
            Movement = null,
            ResidualAmount = null,
            ExplainedAmount = 0m
        };
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(comparison, Json.ReportingIncomeComparisonDto));
        foreach (var name in new[] { "baselineAmount", "currentAmount", "movement", "residualAmount" })
            document.RootElement.GetProperty(name).ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("explainedAmount").GetString().Should().Be("0");
    }

    [Theory]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("1.12345678901234567890123456789")]
    [InlineData("79228162514264337593543950336")]
    public void NonRepresentableStringAmounts_AreRejectedInsteadOfRounded(string amount)
    {
        var node = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(Envelope().Comparison, Json.ReportingIncomeComparisonDto))!;
        node["movement"] = amount;
        var read = () => JsonSerializer.Deserialize(node.ToJsonString(), Json.ReportingIncomeComparisonDto);
        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void GeneratedManifestAndSupportGraph_PreservesDynamicRowsAndTypedDimensions()
    {
        var priorValues = Row("original", "original-line", "9007199254740993.123456");
        IReadOnlyDictionary<string, string> priorRow = new ReadOnlyDictionary<string, string>(priorValues);
        IReadOnlyDictionary<string, string> currentRow = new SortedDictionary<string, string>(Row("late", "late-line", "0.000001"))
        {
            ["transactionAmount"] = "9007199254740993.123456789012",
            ["unroundedAccrualRate"] = "0.0000000000000000000000000001"
        };
        var baseline = Manifest("original", [priorRow]);
        var current = Manifest("restated", [priorRow, currentRow]);
        var retained = ReportingIncomeComparisonEngine.Compare(baseline, current, "income", "income", retainedAtUtc: CapturedAt);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(retained, Json.RetainedReportingIncomeComparison);
        var restored = JsonSerializer.Deserialize(serialized, Json.RetainedReportingIncomeComparison)!;

        restored.Should().BeEquivalentTo(retained);
        restored.Comparison.Movement.Should().Be(0.000001m);
        restored.Comparison.BaselineAmount.Should().Be(9007199254740993.123456m);
        restored.Support.Single().CurrentRecords.Single()["transactionAmount"].Should().Be("9007199254740993.123456789012");
        restored.Support.Single().CurrentRecords.Single()["unroundedAccrualRate"].Should().Be("0.0000000000000000000000000001");
        restored.BaselineManifest.ResolvedParameters!.Scope.Dimensions!.ExternalGlDimensions["desk"].Should().Be("fixed-income");
        priorValues["netAmount"] = "0";
        restored.BaselineManifest.CertifiedDatasetRows.Single()["netAmount"].Should().Be("9007199254740993.123456");
    }

    [Fact]
    public async Task Service_LoadsLegacyNumericArtifactWithoutRewritingItsHashOrEvidence()
    {
        var original = Envelope();
        var node = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(original, Json.RetainedReportingIncomeComparison))!;
        var comparison = node["comparison"]!;
        foreach (var field in new[] { "baselineAmount", "currentAmount", "movement", "explainedAmount", "residualAmount" })
            comparison[field] = JsonValue.Create(decimal.Parse(comparison[field]!.GetValue<string>(), CultureInfo.InvariantCulture));
        foreach (var contribution in comparison["contributions"]!.AsArray())
            contribution!["amount"] = JsonValue.Create(decimal.Parse(contribution["amount"]!.GetValue<string>(), CultureInfo.InvariantCulture));
        foreach (var support in node["support"]!.AsArray())
            support!["contribution"]!["amount"] = JsonValue.Create(decimal.Parse(support["contribution"]!["amount"]!.GetValue<string>(), CultureInfo.InvariantCulture));
        var historicalBytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        var hash = Sha256Digest.Compute(historicalBytes);
        var runs = Substitute.For<IReportingRunStore>();
        var artifacts = Substitute.For<IReportingArtifactStore>();
        var governance = Substitute.For<IReportingGovernanceRepository>();
        artifacts.ReadAsync(new("tenant", hash), Arg.Any<CancellationToken>())
            .Returns(new ReportingArtifactReadResult(new("tenant", hash), historicalBytes.Length, CapturedAt, historicalBytes));
        var service = new ReportingIncomeComparisonService(runs, artifacts, governance);
        var access = new ReportAccessQueryContext("owner", CompanyId: "company", TenantId: "tenant", RequireBoundScope: true);

        var loaded = await service.GetAsync(hash, access);
        var supportingRecord = await service.GetSupportAsync(hash, loaded.Contributions.Single().ContributionId, access);

        loaded.Should().BeEquivalentTo(original.Comparison with { ComparisonId = hash });
        supportingRecord.Should().BeEquivalentTo(original.Support.Single() with { ComparisonId = hash });
        runs.DidNotReceiveWithAnyArgs().GetManifest(default!, default!);
        await artifacts.DidNotReceiveWithAnyArgs().StoreAsync(default!, default);
        using var response = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(loaded, Json.ReportingIncomeComparisonDto));
        response.RootElement.GetProperty("baselineAmount").GetString().Should().Be("9007199254740993.123456");
        Sha256Digest.Compute(historicalBytes).Should().Be(hash);
    }

    [Fact]
    public void GeneratedMethodologyDefinition_PreservesTheHistoricalEvidenceShape()
    {
        var definition = new ReportingIncomeMethodologyDefinition(ReportWriterGridKindDto.Pivot,
            new("income", "netAmount", "Sum"), null, [new("accountType", "Equals", "Revenue")]);
        var json = JsonSerializer.Serialize(definition, Json.ReportingIncomeMethodologyDefinition);

        json.Should().Be("{\"kind\":\"Pivot\",\"metric\":{\"name\":\"income\",\"sourceField\":\"netAmount\",\"function\":\"Sum\"},\"formula\":null,\"filters\":[{\"field\":\"accountType\",\"operator\":\"Equals\",\"value\":\"Revenue\",\"label\":null}]}");
        JsonSerializer.Deserialize(json, Json.ReportingIncomeMethodologyDefinition).Should().BeEquivalentTo(definition);
    }

    private static RetainedReportingIncomeComparison Envelope() => ReportingIncomeComparisonEngine.Compare(
        Manifest("original", [Row("original", "original-line", "9007199254740993.123456")]),
        Manifest("restated", [Row("original", "original-line", "9007199254740993.123456"), Row("late", "late-line", "0.000001")]),
        "income", "income", retainedAtUtc: CapturedAt);

    private static ReportingOutputManifest Manifest(string runId, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        var date = new DateOnly(2026, 9, 30);
        var grids = ReportWriterGridEngine.RenderGrids([
            new("income", "Investment income", ReportWriterGridKindDto.Pivot,
                RowFields: ["entityId"], Metrics: [new("income", "netAmount")])
        ], rows);
        return new(runId, "investment-income", date, ReportingRunStatus.Released, [], [], 1, ReportingRunTrigger.AdHoc,
            RenderedReportWriterGrids: grids.ToImmutableArray(),
            ResolvedParameters: new(
                new("fund", Dimensions: new LedgerDimensionSetDto(EntityId: "entity", ExternalGlDimensions: new Dictionary<string, string> { ["desk"] = "fixed-income" })),
                "period", date, new(Book), ReportingAccountingBasisDto.Gaap, "USD", ReportingConsolidationLevelDto.Fund,
                ReportingOutputFormatDto.Pdf, ReportingFinalityDto.Final, true, true),
            OperationalScope: new("tenant", "organization", "company", "fund", Book.ToString("D"), "period"),
            ImmutableAccessScope: new("policy", "1", ReportingGovernanceAccessMode.CompanyWide, "owner", true, [], Sha256Digest.ComputeUtf8("policy")),
            CertifiedDatasetRows: rows.ToImmutableArray());
    }

    private static Dictionary<string, string> Row(string journalId, string entryId, string amount) => new(StringComparer.Ordinal)
    {
        ["journalEntryId"] = journalId,
        ["entryId"] = entryId,
        ["netAmount"] = amount,
        ["fundId"] = "fund",
        ["entityId"] = "entity",
        ["currency"] = "USD",
        ["externalGl.desk"] = "fixed-income"
    };
}

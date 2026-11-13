using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

[Trait("Category", "Unit")]
public sealed class BondProviderIntakeServiceTests
{
    private const string Tenant = "tenant-a", Company = "company-a", DocumentId = "fred-binding";
    private const string VaultId = "ev-0123456789abcdef01234567", EvidenceUri = "evidence://evidence-vault/" + VaultId;
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly BondFredIntakeRequest Request = new(EvidenceUri, DocumentId, new(2026, 11, 9), new(2026, 11, 10), new(2026, 11, 12));
    private static readonly BondFredSeriesBindingDocument Binding = new(1, "bondFredSeriesBinding", "official-sofr",
        "SOFR", "SOFR", BondMarketObservationKind.Fixing, BondMarketValueUnit.Percent, "New York Fed via FRED", "USD");

    [Fact]
    public async Task Discovery_CallsBondLookupAndPreservesAmbiguityWithoutPublishingIdentity()
    {
        using var fixture = new Fixture();
        var result = await fixture.Service.DiscoverIdentifierAsync(new("US0000000001", "ISIN"));
        result.IsAmbiguous.Should().BeTrue();
        result.ResolvedFigi.Should().BeNull();
        result.Candidates.Select(row => row.Figi).Should().Equal("BOND1", "BOND2");
        fixture.LastBody.Should().Contain("ID_ISIN").And.NotContain("Equity").And.NotContain("marketSecDes");
        result.EvidenceStatus.Should().Be(BondProviderEvidenceStatus.LiveUntested);
        (await fixture.Market.QueryAsync(Tenant, new())).Should().BeEmpty();
    }

    [Fact]
    public async Task ReviewedBinding_PublishesTypedObservationsAndExplicitMissingDates_Idempotently()
    {
        using var fixture = new Fixture();
        var first = await fixture.Service.ImportFredAsync(Tenant, Company, "operator", Request);
        var retry = await fixture.Service.ImportFredAsync(Tenant, Company, "other-operator", Request);
        first.RetainedObservations.AppendedCount.Should().Be(1);
        retry.RetainedObservations.AppendedCount.Should().Be(0);
        retry.RetainedObservations.Observations.Should().Equal(first.RetainedObservations.Observations);
        first.MissingDates.Should().Equal(new DateOnly(2026, 11, 10));
        var observation = (await fixture.Market.QueryAsync(Tenant, new())).Should().ContainSingle().Which;
        observation.Input.Kind.Should().Be(BondMarketObservationKind.Fixing);
        observation.Input.Unit.Should().Be(BondMarketValueUnit.Percent);
        observation.Input.Value.Should().Be(-0.25m);
        observation.Input.SeriesKey.Should().Be("SOFR");
        observation.Input.ObservationKey.Should().Contain(":official-sofr:");
        observation.Input.PublishedAtUtc.Should().BeNull();
        observation.Input.EvidenceReference.Should().Contain(first.BindingEvidenceSha256).And.NotContain("synthetic-key");
        first.EvidenceStatus.Should().Be(BondProviderEvidenceStatus.LiveUntested);
        var source = await fixture.Service.GetProviderResponseAsync(Tenant, Company, first.ResponseSha256);
        source.Should().NotBeNull();
        source!.RawResponseJson.Should().Be(fixture.FredResponseJson);
        source.EvidenceReference.Should().Be(first.ResponseEvidenceReference);
        observation.Input.EvidenceReference.Should().Contain(first.ResponseSha256);
        (await fixture.Service.GetProviderResponseAsync("foreign-tenant", Company, first.ResponseSha256)).Should().BeNull();
    }

    [Fact]
    public async Task SameSourceRowFromDifferentResponseBytes_KeepsOriginalResponseForImmutableRevision()
    {
        using var fixture = new Fixture();
        var first = await fixture.Service.ImportFredAsync(Tenant, Company, "operator", Request);
        fixture.FredResponseJson = "\n" + fixture.FredResponseJson;
        var second = await fixture.Service.ImportFredAsync(Tenant, Company, "operator", Request);
        second.ResponseSha256.Should().NotBe(first.ResponseSha256);
        second.RetainedObservations.AppendedCount.Should().Be(0);
        second.RetainedObservations.Observations.Should().Equal(first.RetainedObservations.Observations);
        second.RetainedObservations.Observations[0].Input.EvidenceReference.Should().Contain(first.ResponseSha256);
        (await fixture.Service.GetProviderResponseAsync(Tenant, Company, second.ResponseSha256))!.RawResponseJson
            .Should().Be(fixture.FredResponseJson);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("vintage")]
    [InlineData("duplicate")]
    public async Task ManualSourceCollision_CannotPreserveArchiveThatDoesNotProveExactRow(string failure)
    {
        using var original = new Fixture();
        var first = await original.Service.ImportFredAsync(Tenant, Company, "operator", Request);
        var facts = first.RetainedObservations.Observations[0].Input;
        using var fixture = new Fixture();
        var date = "2026-11-09";
        var value = failure == "value" ? "99" : "-0.25";
        var vintage = failure == "vintage" ? "2026-11-11" : "2026-11-12";
        var rows = new List<object> { new { date, value, realtime_start = vintage } };
        if (failure == "duplicate")
            rows.Add(new { date, value, realtime_start = vintage });
        var wrong = await fixture.Responses.RetainAsync(Tenant, Company, "fred", JsonSerializer.Serialize(new { observations = rows }));
        var manual = facts with { EvidenceReference = facts.EvidenceReference.Replace(first.ResponseSha256, wrong.ContentSha256, StringComparison.Ordinal) };
        await fixture.Market.ImportAsync(Tenant, "manual-importer", new([manual]));
        Func<Task> intake = () => fixture.Service.ImportFredAsync(Tenant, Company, "operator", Request);
        await intake.Should().ThrowAsync<InvalidDataException>();
        (await fixture.Market.QueryAsync(Tenant, new())).Should().ContainSingle().Which.Input.Should().Be(manual);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("subject")]
    [InlineData("unreviewed")]
    [InlineData("same-reviewer")]
    [InlineData("hash")]
    public async Task UnverifiedBinding_CannotReachProviderOrPublish(string failure)
    {
        using var fixture = new Fixture();
        fixture.Set(failure);
        Func<Task> intake = () => fixture.Service.ImportFredAsync(Tenant, Company, "operator", Request);
        await intake.Should().ThrowAsync<Exception>();
        fixture.HttpCalls.Should().Be(0);
        (await fixture.Market.QueryAsync(Tenant, new())).Should().BeEmpty();
    }

    [Fact]
    public async Task BindingRevokedDuringProviderRequest_CannotPublishReturnedObservations()
    {
        using var fixture = new Fixture();
        fixture.AfterFetch = () => fixture.Set("unreviewed");
        Func<Task> intake = () => fixture.Service.ImportFredAsync(Tenant, Company, "operator", Request);
        await intake.Should().ThrowAsync<InvalidOperationException>();
        fixture.HttpCalls.Should().Be(1);
        (await fixture.Market.QueryAsync(Tenant, new())).Should().BeEmpty();
    }

    [Theory]
    [InlineData("observation", "kind")]
    [InlineData("observation", "unit")]
    [InlineData("quote", "category")]
    [InlineData("quote", "priceBasis")]
    [InlineData("binding", "observationKind")]
    [InlineData("binding", "unit")]
    public void MissingWireMeaning_RefusesInsteadOfInferringZeroEnum(string contract, string property)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        object value = contract switch
        {
            "observation" => new BondMarketObservationInput("row", 1, BondMarketObservationKind.Fixing,
                "fred", "SOFR", new(2026, 11, 9), BondMarketValueUnit.AnnualFraction, 0m,
                "source", new string('a', 64), "https://synthetic.invalid/source"),
            "quote" => new BondMarketQuoteDetails(BondQuoteCategory.EvaluatedValuation, BondQuotePriceBasis.Clean, new(2026, 11, 12)),
            _ => Binding
        };
        var json = JsonNode.Parse(JsonSerializer.Serialize(value, value.GetType(), options))!.AsObject();
        json.Remove(property).Should().BeTrue();
        Action read = () => JsonSerializer.Deserialize(json.ToJsonString(), value.GetType(), options);
        read.Should().Throw<JsonException>();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-bond-provider-intake-" + Guid.NewGuid().ToString("N"));
        private readonly IEvidenceArtifactStore _vault = Substitute.For<IEvidenceArtifactStore>();
        private readonly OpenFigiClient _figi;
        private readonly FredHistoricalDataProvider _fred;
        private EvidenceVaultIdentityDto? _identity;
        private byte[] _bytes = [];
        public BondMarketDataService Market { get; }
        public FileBondProviderResponseStore Responses { get; }
        public BondProviderIntakeService Service { get; }
        public int HttpCalls { get; private set; }
        public string? LastBody { get; private set; }
        public Action? AfterFetch { get; set; }
        public string FredResponseJson { get; set; } = """{"count":2,"observations":[{"date":"2026-11-09","value":"-0.25","realtime_start":"2026-11-12"},{"date":"2026-11-10","value":".","realtime_start":"2026-11-12"}]}""";

        public Fixture()
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                HttpCalls++;
                if (request.Method == HttpMethod.Post)
                {
                    LastBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return Json("""[{"data":[{"figi":"BOND1","marketSector":"Govt","securityType":"US GOVERNMENT"},{"figi":"BOND2","marketSector":"Corp","securityType":"CORP BOND"},{"figi":"EQUITY","marketSector":"Equity","securityType":"Common Stock"}]}]""");
                }
                AfterFetch?.Invoke();
                return Json(FredResponseJson);
            });
            _figi = new("synthetic-key", new HttpClient(handler, disposeHandler: false));
            _fred = new("synthetic-key", new HttpClient(handler));
            Market = new(new FileBondMarketDataStore(_root), new Clock());
            Responses = new(_root);
            Service = new(_figi, _fred, _vault, Market, Responses, new Clock());
            _vault.TryGetVaultIdentityAsync(VaultId, Tenant, Company, Arg.Any<CancellationToken>()).Returns(_ => _identity);
            _vault.TryOpenDocumentContentAsync(VaultId, DocumentId, Tenant, Company, Arg.Any<CancellationToken>())
                .Returns(_ => new EvidenceManifestFile(new MemoryStream(_bytes), "application/json", "fred-binding.json", Now.AddHours(-2)));
            Set();
        }

        public void Set(string? failure = null)
        {
            _bytes = JsonSerializer.SerializeToUtf8Bytes(Binding, BondProviderIntakeJsonContext.Default.BondFredSeriesBindingDocument);
            var hash = failure == "hash" ? new string('0', 64) : Sha256Digest.Compute(_bytes);
            var reviewer = failure == "same-reviewer" ? "uploader" : "reviewer";
            var reviewed = Now.AddHours(-1);
            var review = new EvidenceDocumentReviewStateDto(failure == "unreviewed" ? EvidenceDocumentReviewStatusDto.Unreviewed
                : EvidenceDocumentReviewStatusDto.Accepted, reviewer, reviewed)
            { ConfirmedFields = [new("sourceContentHashSha256", hash, reviewer, reviewed)] };
            var document = new EvidenceDocumentDto(DocumentId, "fred-binding.json", EvidenceDocumentClassificationDto.BankEvidence,
                hash, Now.AddHours(-2), "ManualUpload", "uploader", Tenant, Company,
                EvidenceExtractionStatusDto.Accepted, [], review, [new(reviewed, reviewer, "DocumentReviewRecorded", "Accepted exact binding bytes")]);
            _identity = new(VaultId, "provider-trust", failure == "subject" ? "other-provider" : "fred", "manifest.json",
                "/api/evidence", Now.AddHours(-2), hash, 1, "file-bundle")
            { TenantId = failure == "tenant" ? "other-tenant" : Tenant, Scope = failure == "company" ? "other-company" : Company, Documents = [document] };
        }

        public void Dispose()
        {
            _figi.Dispose();
            _fred.Dispose();
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}

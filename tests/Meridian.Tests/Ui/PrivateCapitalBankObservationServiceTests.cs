using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Banking;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class PrivateCapitalBankObservationServiceTests
{
    private static readonly Guid Entity = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly PrivateCapitalScopeDto Scope = new("tenant", "company", Entity.ToString("D"), "fund", Guid.NewGuid(), "USD");
    private const string VaultId = "ev-0123456789abcdef01234567";
    private const string EvidenceLink = "evidence://evidence-vault/" + VaultId;
    private const string DocumentId = "bank-observation";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcceptedSource_ImportsImmutableObservationAndExactReplayWithoutPosting()
    {
        var fixture = new Fixture();
        var source = Source() with { ObservedAtUtc = Now.AddHours(-1).AddTicks(7) };
        fixture.SetSource(source);

        var first = await fixture.ImportAsync("operator-a");
        var replay = await fixture.ImportAsync("operator-b");

        first.IsReplay.Should().BeFalse();
        replay.IsReplay.Should().BeTrue();
        replay.BankTransaction.Should().Be(first.BankTransaction);
        first.BankTransaction.RecordedBy.Should().Be("source-reviewer");
        first.BankTransaction.RecordedAt.Ticks.Should().Be(source.ObservedAtUtc.Ticks - 7);
        first.BankTransaction.TransactionType.Should().Be("CapitalCallFunding");
        first.BankTransaction.Amount.Should().Be(25_000m);
        first.BankTransaction.PendingPaymentId.Should().BeNull();
        first.BankTransaction.ExternalRef.Should().Contain(first.SourceHashSha256).And.Contain(DocumentId);
        fixture.Rows.Should().ContainSingle();
        await fixture.Store.Received(1).InsertBankTransactionAsync(Arg.Any<BankTransactionDto>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("outgoing")]
    [InlineData("foreign-entity")]
    [InlineData("foreign-currency")]
    [InlineData("negative")]
    [InlineData("unsettled")]
    [InlineData("effective-before-settlement")]
    [InlineData("observed-before-settlement")]
    [InlineData("observed-after-retained")]
    [InlineData("excess-precision")]
    [InlineData("excess-amount")]
    public async Task UnsupportedSource_CannotBecomeBankAuthority(string failure)
    {
        var source = Source();
        source = failure switch
        {
            "outgoing" => source with { Direction = "Debit" },
            "foreign-entity" => source with { EntityId = Guid.NewGuid() },
            "foreign-currency" => source with { Currency = "EUR" },
            "negative" => source with { Amount = -1m },
            "unsettled" => source with { EffectiveDate = new(2026, 11, 11), SettlementDate = new(2026, 11, 11) },
            "effective-before-settlement" => source with { EffectiveDate = new(2026, 11, 9) },
            "observed-before-settlement" => source with { ObservedAtUtc = Now.AddDays(-2) },
            "observed-after-retained" => source with { ObservedAtUtc = Now.AddMinutes(-5) },
            "excess-precision" => source with { Amount = 25_000.00001m },
            "excess-amount" => source with { Amount = 1_000_000_000_000_000m },
            _ => throw new InvalidOperationException()
        };
        var fixture = new Fixture();
        fixture.SetSource(source);

        await fixture.ImportAction.Should().ThrowAsync<InvalidOperationException>();

        fixture.Rows.Should().BeEmpty();
        await fixture.Store.DidNotReceiveWithAnyArgs().InsertBankTransactionAsync(default!, default);
    }

    [Theory]
    [InlineData("unreviewed")]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-company")]
    [InlineData("wrong-classification")]
    [InlineData("bad-content-hash")]
    public async Task UnverifiedSource_CannotBeImported(string failure)
    {
        var fixture = new Fixture();
        fixture.SetSource(Source(), failure);

        await fixture.ImportAction.Should().ThrowAsync<InvalidOperationException>();

        fixture.Rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("malformed")]
    public async Task JsonSource_RequiresCompleteUnambiguousTypedFields(string failure)
    {
        var json = JsonSerializer.Serialize(Source(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var node = JsonNode.Parse(json)!.AsObject();
        if (failure == "missing") node.Remove("direction");
        if (failure == "unknown") node["approveAutomatically"] = true;
        var bytes = Encoding.UTF8.GetBytes(failure switch
        {
            "duplicate" => "{\"amount\":1," + json[1..],
            "malformed" => "{unparseable",
            _ => node.ToJsonString()
        });
        var fixture = new Fixture();
        fixture.SetBytes(bytes);

        await fixture.ImportAction.Should().ThrowAsync<ArgumentException>();

        fixture.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task SameBankIdentityWithChangedSourceOrVoidedRecord_ConflictsWithoutOverwrite()
    {
        var fixture = new Fixture();
        var source = Source();
        fixture.SetSource(source);
        var original = await fixture.ImportAsync();
        fixture.SetSource(source with { StatementReference = "Changed reviewed source" });

        await fixture.ImportAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different economic or source*");

        fixture.Rows.Should().ContainSingle().Which.Should().Be(original.BankTransaction);
        fixture.SetSource(source);
        fixture.Rows[0] = original.BankTransaction with { IsVoided = true };
        await fixture.ImportAction.Should().ThrowAsync<InvalidOperationException>();
        fixture.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task ConflictingInsertRace_IsDetectedByRetainedReadAfterInsert()
    {
        var fixture = new Fixture();
        fixture.SetSource(Source());
        fixture.Store.InsertBankTransactionAsync(Arg.Any<BankTransactionDto>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            fixture.Rows.Add(call.Arg<BankTransactionDto>() with { Amount = 1m });
            return Task.CompletedTask;
        });

        await fixture.ImportAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different economic or source*");

        fixture.Rows.Should().ContainSingle().Which.Amount.Should().Be(1m);
    }

    [Theory]
    [InlineData("25000.0000000000000000000000000000000000000000000000001", false)]
    [InlineData("25000.0000000000000000000000000000000000000000000000001", true)]
    [InlineData("2.5e4", false)]
    [InlineData("+25000", true)]
    public async Task RawAmountPrecision_IsCheckedBeforeDecimalConversion(string rawAmount, bool quoted)
    {
        var fixture = new Fixture();
        var json = JsonSerializer.Serialize(Source(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var token = quoted ? JsonSerializer.Serialize(rawAmount) : rawAmount;
        json = json.Replace("\"amount\":25000,", "\"amount\":" + token + ",", StringComparison.Ordinal);
        fixture.SetBytes(Encoding.UTF8.GetBytes(json));

        await fixture.ImportAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*plain positive decimal*");

        fixture.Rows.Should().BeEmpty();
        await fixture.Store.DidNotReceiveWithAnyArgs().InsertBankTransactionAsync(default!, default);
    }

    [Fact]
    public async Task RawAmountInsignificantZeroAliases_PreserveTheExactSupportedValue()
    {
        var fixture = new Fixture();
        var node = JsonSerializer.SerializeToNode(Source(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        node["amount"] = "00025000.0000000000000000000000000000000000000000000000000";
        fixture.SetBytes(Encoding.UTF8.GetBytes(node.ToJsonString()));

        var result = await fixture.ImportAsync();

        result.BankTransaction.Amount.Should().Be(25_000m);
        fixture.Rows.Should().ContainSingle();
    }

    private static PrivateCapitalBankObservationSourceDto Source() => new(
        Guid.Parse("b2222222-2222-2222-2222-222222222222"), Entity, 25_000m, "USD", new(2026, 11, 10),
        new(2026, 11, 9), new(2026, 11, 10), "Credit", "Synthetic statement line 1", Now.AddHours(-1));

    private sealed class Fixture
    {
        public readonly IEvidenceArtifactStore Vault = Substitute.For<IEvidenceArtifactStore>();
        public readonly IBankingStore Store = Substitute.For<IBankingStore>();
        public readonly List<BankTransactionDto> Rows = [];

        public Fixture()
        {
            Store.GetBankTransactionsAsync(null, Arg.Any<CancellationToken>()).Returns(_ => Rows.ToArray());
            Store.InsertBankTransactionAsync(Arg.Any<BankTransactionDto>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Rows.Add(call.Arg<BankTransactionDto>());
                return Task.CompletedTask;
            });
        }

        public void SetSource(PrivateCapitalBankObservationSourceDto source, string? failure = null)
            => SetBytes(JsonSerializer.SerializeToUtf8Bytes(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)), failure);

        public void SetBytes(byte[] bytes, string? failure = null)
        {
            var hash = failure == "bad-content-hash" ? new string('0', 64) : Sha256Digest.Compute(bytes);
            var document = new EvidenceDocumentDto(DocumentId, "bank-observation.json",
                failure == "wrong-classification" ? EvidenceDocumentClassificationDto.Agreement : EvidenceDocumentClassificationDto.BankEvidence,
                hash, Now.AddMinutes(-10), "synthetic-upload", "source-operator", Scope.TenantId, Scope.CompanyId,
                EvidenceExtractionStatusDto.Accepted, [],
                new(failure == "unreviewed" ? EvidenceDocumentReviewStatusDto.Unreviewed : EvidenceDocumentReviewStatusDto.Accepted,
                    "source-reviewer", Now.AddMinutes(-5)), []) { ContentType = "application/json" };
            var identity = new EvidenceVaultIdentityDto(VaultId, "fund", Scope.FundProfileId, "retained.json",
                "/api/evidence", Now.AddMinutes(-10), hash, 1, "file-bundle")
            {
                TenantId = failure == "wrong-tenant" ? "foreign" : Scope.TenantId,
                Scope = failure == "wrong-company" ? "foreign" : Scope.CompanyId,
                Documents = [document]
            };
            Vault.TryGetVaultIdentityAsync(VaultId, Scope.TenantId, Scope.CompanyId, Arg.Any<CancellationToken>()).Returns(identity);
            Vault.VerifyRetainedContentAsync(VaultId, Scope.TenantId, Scope.CompanyId, Arg.Any<CancellationToken>()).Returns(true);
            Vault.TryOpenDocumentContentAsync(VaultId, DocumentId, Scope.TenantId, Scope.CompanyId, Arg.Any<CancellationToken>())
                .Returns(_ => new EvidenceManifestFile(new MemoryStream(bytes), "application/json", "bank-observation.json", identity.RetainedAt));
        }

        public Task<PrivateCapitalBankObservationImportResultDto> ImportAsync(string actor = "authenticated-operator")
            => new PrivateCapitalBankObservationService(Vault, Store, new FixedClock()).ImportAsync(Scope, new(EvidenceLink, DocumentId), actor);

        public Func<Task> ImportAction => async () => { await ImportAsync(); };
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

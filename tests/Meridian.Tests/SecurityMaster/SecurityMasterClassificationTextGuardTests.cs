using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Stored classification labels are decoded by their text (<see cref="ClassificationLabelReader"/>),
/// so new free-text labels that look like the retired F# rendering are refused on write. Otherwise a
/// newly written <c>None</c> or <c>Some(Equity)</c> subtype would be read back as no label or as
/// <c>Equity</c>.
/// </summary>
public sealed class SecurityMasterClassificationTextGuardTests
{
    [Theory]
    [InlineData("subType", "None")]
    [InlineData("subType", "Some(Equity)")]
    [InlineData("subType", "OtherSubType \"Spot\"")]
    [InlineData("subType", "\"Quoted\"")]
    [InlineData("category", "None")]
    public async Task CreateAsync_OtherSecurityTextThatLooksLikeARetiredRendering_IsRefusedBeforeAnyWrite(string key, string value)
    {
        var (service, events) = CreateService();
        var terms = new Dictionary<string, object?> { ["category"] = "CashEquivalent", ["subType"] = "TreasurySweep" };
        terms[key] = value;
        if (key == "category")
        {
            terms.Remove("subType");
        }

        await service.Invoking(s => s.CreateAsync(CreateRequest(JsonSerializer.SerializeToElement(terms))))
            .Should().ThrowAsync<SecurityMasterWriteRefusedException>()
            .WithMessage("*retired F# classification rendering*");
        await events.DidNotReceiveWithAnyArgs().AppendAsync(default, default, default!, default);
    }

    [Theory]
    [InlineData("TreasurySweep")]
    [InlineData("Some(Nested)")]
    [InlineData("Digital Asset")]
    public async Task CreateAsync_OtherSecurityTextTheReaderKeeps_IsAccepted(string subType)
    {
        var (service, _) = CreateService();
        var terms = JsonSerializer.SerializeToElement(new { category = "CashEquivalent", subType });

        var detail = await service.CreateAsync(CreateRequest(terms));

        detail.AssetClass.Should().Be("OtherSecurity");
        ClassificationLabelReader.Normalize(subType).Should().Be(subType, "accepted text reads back unchanged");
    }

    [Theory]
    [InlineData("None", true)]
    [InlineData("Some(PreferredEquity)", true)]
    [InlineData("OtherFamily \"Crypto\"", true)]
    [InlineData("Some(Nested)", false)]
    [InlineData("PreferredEquity", false)]
    [InlineData("  Spot  ", false)]
    [InlineData(null, false)]
    public void IsAmbiguousFreeText_FlagsExactlyTheTextTheDecoderWouldChange(string? value, bool ambiguous)
        => ClassificationLabelReader.IsAmbiguousFreeText(value).Should().Be(ambiguous);

    [Fact]
    public async Task AmendTermsAsync_OtherSecurityTextThatLooksLikeARetiredRendering_IsRefused()
    {
        var (service, events) = CreateService();
        var securityId = Guid.NewGuid();
        var create = CreateRequest(JsonSerializer.SerializeToElement(new { category = "CashEquivalent", subType = "TreasurySweep" }), securityId);
        var created = await service.CreateAsync(create);

        await service.Invoking(s => s.AmendTermsAsync(new AmendSecurityTermsRequest(
                SecurityId: securityId,
                ExpectedVersion: created.Version,
                CommonTerms: null,
                AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(new { category = "CashEquivalent", subType = "Some(Equity)" }),
                IdentifiersToAdd: [],
                IdentifiersToExpire: [],
                EffectiveFrom: DateTimeOffset.UtcNow,
                SourceSystem: "test",
                UpdatedBy: "codex",
                SourceRecordId: null,
                Reason: "ambiguous subtype")))
            .Should().ThrowAsync<SecurityMasterWriteRefusedException>();
    }

    private static (SecurityMasterService Service, ISecurityMasterEventStore Events) CreateService()
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        SecurityProjectionRecord? stored = null;
        store.UpsertProjectionAsync(Arg.Do<SecurityProjectionRecord>(projection => stored = projection), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        store.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => stored);
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var options = new SecurityMasterOptions { SnapshotIntervalVersions = 50, ResolveInactiveByDefault = true };
        return (new SecurityMasterService(eventStore, snapshotStore, store, rebuilder, options, NullLogger<SecurityMasterService>.Instance), eventStore);
    }

    private static CreateSecurityRequest CreateRequest(JsonElement assetSpecificTerms, Guid? securityId = null)
    {
        var id = securityId ?? Guid.NewGuid();
        return new CreateSecurityRequest(
            id,
            "OtherSecurity",
            JsonSerializer.SerializeToElement(new { displayName = "Other test security", currency = "USD", issuerName = "Meridian Treasury" }),
            assetSpecificTerms,
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"OTHER-{id:N}", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)],
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "classification text guard");
    }
}

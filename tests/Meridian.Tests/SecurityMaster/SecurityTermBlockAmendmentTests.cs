using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The generic term-block route replaces any nested object or array term the asset class declares,
/// so preferred and convertible equity terms are one case of a surface every class has.
/// </summary>
public sealed class SecurityTermBlockAmendmentTests
{
    [Fact]
    public void DeclaredBlocks_ComeFromTheRegistry()
    {
        SecurityTermBlocks.For("Equity").Select(static field => field.Key).Should().BeEquivalentTo(["preferredTerms", "convertibleTerms"]);
        SecurityTermBlocks.For("Swap").Select(static field => field.Key).Should().Contain("legs");
        SecurityTermBlocks.For("NotAClass").Should().BeEmpty();
    }

    [Fact]
    public void BuildPatch_ReplacesOnlyTheBlock_AndRemovesAliasedCopies()
    {
        var current = JsonSerializer.SerializeToElement(new
        {
            effectiveDate = "2026-01-15",
            maturityDate = "2031-01-15",
            swapLegs = new[] { new { legId = "old" } }
        });
        var legs = JsonSerializer.SerializeToElement(new[] { new { legId = "new-fixed" }, new { legId = "new-floating" } });

        var patch = SecurityTermBlocks.BuildPatch("Swap", current, "legs", legs);

        patch.TryGetProperty("swapLegs", out _).Should().BeFalse();
        patch.GetProperty("legs").GetArrayLength().Should().Be(2);
        patch.GetProperty("maturityDate").GetString().Should().Be("2031-01-15");
    }

    [Fact]
    public void BuildPatch_RejectsUndeclaredBlocksAndMismatchedKinds()
    {
        var current = JsonSerializer.SerializeToElement(new { shareClass = "A" });

        var undeclared = () => SecurityTermBlocks.BuildPatch("Equity", current, "callSchedule", JsonSerializer.SerializeToElement(new { }));
        var wrongKind = () => SecurityTermBlocks.BuildPatch("Equity", current, "preferredTerms", JsonSerializer.SerializeToElement(new[] { 1 }));

        undeclared.Should().Throw<ArgumentException>().WithMessage("*no term block 'callSchedule'*preferredTerms*");
        wrongKind.Should().Throw<ArgumentException>().WithMessage("*must be a JSON object*");
    }

    [Fact]
    public void BuildPatch_NullClearsAnOptionalBlock()
    {
        var current = JsonSerializer.SerializeToElement(new { classification = "Preferred", preferredTerms = new { dividendRate = 5m } });

        var patch = SecurityTermBlocks.BuildPatch("Equity", current, "preferredTerms", JsonSerializer.SerializeToElement<object?>(null));

        patch.TryGetProperty("preferredTerms", out _).Should().BeFalse();
        patch.GetProperty("classification").GetString().Should().Be("Preferred");
    }

    [Fact]
    public async Task AmendTermBlockAsync_ReplacesPreferredTerms_KeepsConvertibleTerms_AndRecordsTheGenericEvent()
    {
        var securityId = Guid.NewGuid();
        var underlyingSecurityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "ConvertiblePreferred", underlyingSecurityId));
        var service = CreateService(eventStore, snapshotStore, store);

        var detail = await service.AmendTermBlockAsync(securityId, "preferredTerms", new AmendSecurityTermBlockRequest(
            ExpectedVersion: 7,
            Block: JsonSerializer.SerializeToElement(new
            {
                dividendRate = 6.25m,
                dividendType = "Cumulative",
                redemptionPrice = 25.50m,
                callableDate = new DateOnly(2031, 1, 15),
                liquidationPreference = new { kind = "Pari" }
            }),
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "generic block replacement"));

        detail.Version.Should().Be(8);
        detail.AssetSpecificTerms.GetProperty("preferredTerms").GetProperty("dividendRate").GetDecimal().Should().Be(6.25m);
        detail.AssetSpecificTerms.GetProperty("convertibleTerms").GetProperty("underlyingSecurityId").GetGuid().Should().Be(underlyingSecurityId);
        await eventStore.Received(1).AppendAsync(
            securityId,
            7,
            Arg.Is<IReadOnlyList<SecurityMasterEventEnvelope>>(events =>
                events.Count == 1 && events[0].EventType == SecurityTermBlocks.AmendedEventType),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermBlockAsync_StaleExpectedVersion_RejectsBeforeBuildingThePatch()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "ConvertiblePreferred", Guid.NewGuid()));
        var service = CreateService(eventStore, snapshotStore, store);

        var act = () => service.AmendTermBlockAsync(securityId, "preferredTerms", new AmendSecurityTermBlockRequest(
            ExpectedVersion: 6,
            Block: JsonSerializer.SerializeToElement(new { dividendRate = 6.25m, dividendType = "Fixed" }),
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "edit based on an older version"));

        var thrown = await act.Should().ThrowAsync<SecurityMasterConcurrencyException>();
        thrown.Which.ExpectedVersion.Should().Be(6);
        thrown.Which.CurrentVersion.Should().Be(7);
        await eventStore.DidNotReceiveWithAnyArgs().AppendAsync(default, default, default!, default);
    }

    // ── Readable before writable (ADR-024) ─────────────────────────────────────────────────────

    [Fact]
    public void For_OffersOnlyTheBlocksTheNodeWrites()
    {
        SecurityTermBlocks.For("Bond").Select(static field => field.Key).Should().Equal("stepSchedule", "principalSchedule", "scheduleConventions", "callSchedule", "putSchedule");
        SecurityTermBlocks.For("CertificateOfDeposit").Select(static field => field.Key).Should().Equal("scheduleConventions", "callSchedule", "putSchedule");
        SecurityTermBlocks.For("StructuredCredit").Select(static field => field.Key).Should().Equal("factorScheduleEntries", "scheduleConventions", "callSchedule", "putSchedule");
        SecurityTermBlocks.For("Swap").Select(static field => field.Key).Should().Equal("legs");
        SecurityTermBlocks.TryDescribeReadableOnlyBlock("Swap", "scheduleConventions", out _).Should().BeFalse("the swap block belongs to a leg, not the root");
    }

    [Theory]
    [InlineData("Bond", "scheduleConventions", 2, 1, """{"stubPolicy":"Short","paymentCalendars":["USNY"]}""", "{}")]
    [InlineData("CertificateOfDeposit", "scheduleConventions", 2, 1, """{"stubPolicy":"Short","paymentCalendars":["USNY"]}""", "{}")]
    [InlineData("StructuredCredit", "scheduleConventions", 3, 2, """{"stubPolicy":"Short","paymentCalendars":["USNY"]}""", "{}")]
    [InlineData("Bond", "callSchedule", 3, 1, """[{"callDate":"2030-06-15","callPrice":101}]""", "[]")]
    [InlineData("Bond", "putSchedule", 3, 1, """[{"putDate":"2029-06-15"}]""", "[]")]
    [InlineData("CertificateOfDeposit", "callSchedule", 3, 1, """[{"callDate":"2026-09-30"}]""", "[]")]
    [InlineData("CertificateOfDeposit", "putSchedule", 3, 1, """[{"putDate":"2026-09-30"}]""", "[]")]
    [InlineData("StructuredCredit", "callSchedule", 4, 2, """[{"callDate":"2030-06-15","callPrice":101}]""", "[]")]
    [InlineData("StructuredCredit", "putSchedule", 4, 2, """[{"putDate":"2029-06-15"}]""", "[]")]
    public void BuildPatch_NewlyWritableBlock_AcceptsUnset_ButRefusesIncompleteSemanticDeclarations(
        string assetClass, string blockKey, int since, int writes, string populatedBlock, string emptyBlock)
    {
        var current = JsonSerializer.SerializeToElement(new { maturity = "2031-06-15" });
        SecurityTermBlocks.TryDescribeReadableOnlyBlock(assetClass, blockKey, out var described).Should().BeFalse();
        described.Should().BeNull();
        SecurityTermBlocks.For(assetClass).Should().Contain(field => field.Key == blockKey);
        foreach (var block in new[] { emptyBlock, "null" })
        {
            Action clear = () => SecurityTermBlocks.BuildPatch(assetClass, current, blockKey, JsonSerializer.Deserialize<JsonElement>(block));
            clear.Should().NotThrow("writable optional containers accept unset replacements");
        }
        Action populated = () => SecurityTermBlocks.BuildPatch(assetClass, current, blockKey, JsonSerializer.Deserialize<JsonElement>(populatedBlock));
        var refusal = populated.Should().Throw<ArgumentException>().Which;
        refusal.ParamName.Should().Be("block");
        if (blockKey == "scheduleConventions")
            refusal.Message.Should().Contain("issueDate");
        else
            refusal.Message.Should().Contain("exerciseStyle");
    }

    [Theory]
    // A case variant of the readable-only key, the swap leg's element field (not a block of the
    // class), a writable block, a scalar and a class that declares no such field all keep today's
    // undeclared-block text, or pass, rather than the readable-only reason.
    [InlineData("Bond", "ScheduleConventions")]
    [InlineData("Swap", "scheduleConventions")]
    [InlineData("Bond", "principalSchedule")]
    [InlineData("Bond", "maturity")]
    [InlineData("Equity", "scheduleConventions")]
    // The same holds for the call and put schedules: a case variant, and classes that declare neither.
    [InlineData("Bond", "CallSchedule")]
    [InlineData("CertificateOfDeposit", "PutSchedule")]
    [InlineData("StructuredCredit", "CALLSCHEDULE")]
    [InlineData("Swap", "callSchedule")]
    [InlineData("Swap", "putSchedule")]
    [InlineData("Equity", "callSchedule")]
    [InlineData("Equity", "putSchedule")]
    public void TryDescribeReadableOnlyBlock_IsFalse_OutsideAReadableOnlyBlockKey(string assetClass, string blockKey)
    {
        SecurityTermBlocks.TryDescribeReadableOnlyBlock(assetClass, blockKey, out var reason).Should().BeFalse();
        reason.Should().BeNull();
    }

    [Theory]
    [InlineData("Bond", "ScheduleConventions", "stepSchedule, principalSchedule")]
    [InlineData("Swap", "scheduleConventions", "legs")]
    [InlineData("CertificateOfDeposit", "sinkingFundSchedule", "none")]
    [InlineData("Bond", "CallSchedule", "stepSchedule, principalSchedule")]
    public void BuildPatch_UndeclaredBlock_KeepsTheDeclaredBlocksText_WithoutTheReadableOnlyBlock(
        string assetClass, string blockKey, string declaredBlocks)
    {
        var current = JsonSerializer.SerializeToElement(new { maturity = "2031-06-15" });

        var act = () => SecurityTermBlocks.BuildPatch(assetClass, current, blockKey, JsonSerializer.SerializeToElement(new { }));

        var offered = string.Join(", ", SecurityTermBlocks.For(assetClass).Select(field => field.Key));
        act.Should().Throw<ArgumentException>().WithMessage(
            $"Asset class '{assetClass}' declares no term block '{blockKey}'. Declared blocks: {(offered.Length == 0 ? "none" : offered)}. (Parameter 'blockKey')");
    }

    [Fact]
    public async Task AmendTermBlockAsync_DeclaredBlockWithoutIssueAnchor_IsRefusedBeforeAnythingIsAppended()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "Common", Guid.NewGuid()) with
            {
                AssetClass = "Bond",
                AssetSpecificTerms = JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    maturity = "2031-06-15",
                    isCallable = false
                })
            });
        var service = CreateService(eventStore, snapshotStore, store);

        var act = () => service.AmendTermBlockAsync(securityId, "scheduleConventions", new AmendSecurityTermBlockRequest(
            ExpectedVersion: 7,
            Block: JsonSerializer.SerializeToElement(new { stubPolicy = "Short" }),
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "set conventions ahead of the write version"));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*need the contractual issue date* (Parameter 'block')");
        await eventStore.DidNotReceiveWithAnyArgs().AppendAsync(default, default, default!, default);
    }

    private static SecurityMasterService CreateService(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterSnapshotStore snapshotStore,
        ISecurityMasterStore store)
    {
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        return new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);
    }

    private static SecurityProjectionRecord CreateProjection(Guid securityId, string classification, Guid underlyingSecurityId)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Meridian Preferred",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "MPFD",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Meridian Preferred",
                currency = "USD",
                exchange = "XNYS",
                lotSize = 100,
                tickSize = 0.01m
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "A",
                classification,
                preferredTerms = classification is "Preferred" or "ConvertiblePreferred"
                    ? new
                    {
                        dividendRate = 5.75m,
                        dividendType = "Fixed",
                        redemptionPrice = 25.00m,
                        callableDate = new DateOnly(2030, 1, 15),
                        liquidationPreference = new
                        {
                            kind = "Pari"
                        }
                    }
                    : null,
                convertibleTerms = classification == "ConvertiblePreferred"
                    ? new
                    {
                        underlyingSecurityId,
                        conversionRatio = 2.5m,
                        conversionPrice = 50.00m,
                        conversionStartDate = new DateOnly(2027, 1, 15),
                        conversionEndDate = new DateOnly(2031, 12, 31)
                    }
                    : null
            }),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "codex",
                asOf = DateTimeOffset.UtcNow
            }),
            Version: 7,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
            EffectiveTo: null,
            Identifiers: new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MPFD", true, DateTimeOffset.UtcNow.AddDays(-10), null, null)
            },
            Aliases: Array.Empty<SecurityAliasDto>());
}

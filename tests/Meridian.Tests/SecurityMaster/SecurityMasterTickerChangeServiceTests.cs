using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies that ticker changes are recorded as first-class amend events that close the old
/// identifier's validity interval and open the new one, so historical as-of resolution keeps
/// answering old-ticker lookups at pre-change dates.
/// </summary>
public sealed class SecurityMasterTickerChangeServiceTests
{
    private static readonly DateTimeOffset ChangeDate = new(2022, 6, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAsync_ExpiresOldTickerAndOpensNewIntervalThroughAmendPath()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ContractSecurityMasterQueryService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(MakeDetail(securityId, "FB", isPrimary: true));
        var commandService = Substitute.For<ISecurityMasterService>();
        AmendSecurityTermsRequest? captured = null;
        commandService.AmendTermsAsync(Arg.Do<AmendSecurityTermsRequest>(request => captured = request), Arg.Any<CancellationToken>())
            .Returns(MakeDetail(securityId, "META", isPrimary: true));
        var service = CreateService(queryService, commandService);

        await service.RecordAsync(new RecordTickerChangeRequest(
            securityId, "FB", "META", ChangeDate, "symbology-steward", "Corporate rebrand."));

        captured.Should().NotBeNull();
        captured!.SecurityId.Should().Be(securityId);
        captured.ExpectedVersion.Should().Be(7);
        captured.EffectiveFrom.Should().Be(ChangeDate);

        var expired = captured.IdentifiersToExpire.Should().ContainSingle().Subject;
        expired.Kind.Should().Be(SecurityIdentifierKind.Ticker);
        expired.Value.Should().Be("FB");
        expired.ValidTo.Should().Be(ChangeDate);

        var added = captured.IdentifiersToAdd.Should().ContainSingle().Subject;
        added.Kind.Should().Be(SecurityIdentifierKind.Ticker);
        added.Value.Should().Be("META");
        added.IsPrimary.Should().BeTrue("the replacement ticker inherits primacy from the old one");
        added.ValidFrom.Should().Be(ChangeDate);
        added.ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task RecordAsync_Throws_WhenSecurityIsUnknown()
    {
        var queryService = Substitute.For<ContractSecurityMasterQueryService>();
        queryService.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SecurityDetailDto?)null);
        var service = CreateService(queryService, Substitute.For<ISecurityMasterService>());

        var act = () => service.RecordAsync(new RecordTickerChangeRequest(
            Guid.NewGuid(), "FB", "META", ChangeDate, "steward", null));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*was not found*");
    }

    [Fact]
    public async Task RecordAsync_Throws_WhenOldTickerIsNotValidAtEffectiveDate()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ContractSecurityMasterQueryService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(MakeDetail(securityId, "FB", isPrimary: true, validFrom: ChangeDate.AddYears(1)));
        var service = CreateService(queryService, Substitute.For<ISecurityMasterService>());

        var act = () => service.RecordAsync(new RecordTickerChangeRequest(
            securityId, "FB", "META", ChangeDate, "steward", null));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no ticker identifier*");
    }

    [Fact]
    public async Task RecordAsync_Throws_WhenTickersAreIdentical()
    {
        var service = CreateService(
            Substitute.For<ContractSecurityMasterQueryService>(),
            Substitute.For<ISecurityMasterService>());

        var act = () => service.RecordAsync(new RecordTickerChangeRequest(
            Guid.NewGuid(), "META", "meta", ChangeDate, "steward", null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static SecurityMasterTickerChangeService CreateService(
        ContractSecurityMasterQueryService queryService,
        ISecurityMasterService commandService)
        => new(queryService, commandService, NullLogger<SecurityMasterTickerChangeService>.Instance);

    private static SecurityDetailDto MakeDetail(
        Guid securityId, string ticker, bool isPrimary, DateTimeOffset? validFrom = null)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Meta Platforms",
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Meta Platforms", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { schemaVersion = 1 }),
            Identifiers: new[]
            {
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker, ticker, isPrimary,
                    validFrom ?? ChangeDate.AddYears(-5), null, null)
            },
            Aliases: Array.Empty<SecurityAliasDto>(),
            Version: 7,
            EffectiveFrom: ChangeDate.AddYears(-5),
            EffectiveTo: null);
}

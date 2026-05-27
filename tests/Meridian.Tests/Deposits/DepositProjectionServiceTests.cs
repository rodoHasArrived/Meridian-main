using Meridian.Application.Deposits;
using Meridian.Contracts.Deposits;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using FluentAssertions;

namespace Meridian.Tests.Deposits;

public sealed class DepositProjectionServiceTests
{
    private readonly ISecurityMasterStore _smStore = Substitute.For<ISecurityMasterStore>();
    private readonly IDepositReferenceProjectionStore _projStore = Substitute.For<IDepositReferenceProjectionStore>();
    private DepositProjectionService CreateSut() => new(_smStore, _projStore);

    [Fact]
    public async Task GetReferenceAsync_ReturnsNull_WhenAssetClassMismatch()
    {
        _smStore.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MakeProjection(Guid.NewGuid(), "Equity"));

        var result = await CreateSut().GetReferenceAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReferenceAsync_ReturnsDto_WhenRowExists()
    {
        var id = Guid.NewGuid();
        _smStore.GetProjectionAsync(id, Arg.Any<CancellationToken>())
            .Returns(MakeProjection(id, "Deposit"));
        _projStore.GetDepositAsync(id, Arg.Any<CancellationToken>())
            .Returns(BuildRow(id));

        var result = await CreateSut().GetReferenceAsync(id);

        result.Should().NotBeNull();
        result!.InstitutionName.Should().Be("City Bank");
        result.IsCallable.Should().BeFalse();
    }

    [Fact]
    public async Task GetByInstitutionAsync_ReturnsEmpty_ForBlankInput()
    {
        var results = await CreateSut().GetByInstitutionAsync("   ");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMaturingBeforeAsync_DelegatesToStore()
    {
        var cutoff = new DateOnly(2027, 1, 1);
        _projStore.GetMaturingBeforeAsync(cutoff, Arg.Any<CancellationToken>())
            .Returns([BuildRow(Guid.NewGuid())]);

        var results = await CreateSut().GetMaturingBeforeAsync(cutoff);

        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByInstitutionAsync_DelegatesToStore()
    {
        _projStore.GetByInstitutionAsync("City Bank", Arg.Any<CancellationToken>())
            .Returns([BuildRow(Guid.NewGuid()), BuildRow(Guid.NewGuid())]);

        var results = await CreateSut().GetByInstitutionAsync("City Bank");

        results.Should().HaveCount(2);
    }

    private static SecurityProjectionRecord MakeProjection(Guid id, string assetClass)
    {
        var empty = System.Text.Json.JsonDocument.Parse("{}").RootElement;
        return new(id, assetClass, SecurityStatusDto.Active, "6M Deposit", "USD",
            "ISIN", "US9999", empty, empty, empty, 1, DateTimeOffset.UtcNow, null, [], []);
    }

    private static DepositProjectionRow BuildRow(Guid id)
        => new(id, "6M Deposit", "USD", "TimeDeposit", "City Bank",
               new DateOnly(2026, 12, 31), 0.045m, "Act/360", false, "US9999", 1);
}

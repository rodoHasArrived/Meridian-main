using FluentAssertions;
using Meridian.Application.CertificatesOfDeposit;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.CertificatesOfDeposit;

public sealed class CertificateOfDepositProjectionServiceTests
{
    private readonly ISecurityMasterStore _smStore = Substitute.For<ISecurityMasterStore>();
    private readonly ICertificateOfDepositReferenceProjectionStore _projectionStore = Substitute.For<ICertificateOfDepositReferenceProjectionStore>();

    private CertificateOfDepositProjectionService CreateSut() => new(_smStore, _projectionStore);

    [Fact]
    public async Task GetReferenceAsync_ReturnsNull_WhenAssetClassMismatch()
    {
        _smStore.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MakeProjection(Guid.NewGuid(), "Bond"));

        var result = await CreateSut().GetReferenceAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReferenceAsync_ReturnsDto_WhenRowExists()
    {
        var id = Guid.NewGuid();
        _smStore.GetProjectionAsync(id, Arg.Any<CancellationToken>())
            .Returns(MakeProjection(id, "CertificateOfDeposit"));
        _projectionStore.GetCertificateOfDepositAsync(id, Arg.Any<CancellationToken>())
            .Returns(BuildRow(id));

        var result = await CreateSut().GetReferenceAsync(id);

        result.Should().NotBeNull();
        result!.IssuerName.Should().Be("Meridian Bank");
        result.Maturity.Should().Be(new DateOnly(2027, 6, 30));
    }

    [Fact]
    public async Task GetByIssuerAsync_ReturnsEmpty_ForBlankInput()
    {
        var results = await CreateSut().GetByIssuerAsync(" ");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIssuerAsync_DelegatesToStore()
    {
        _projectionStore.GetByIssuerAsync("Meridian Bank", Arg.Any<CancellationToken>())
            .Returns([BuildRow(Guid.NewGuid()), BuildRow(Guid.NewGuid())]);

        var results = await CreateSut().GetByIssuerAsync("Meridian Bank");

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMaturingBeforeAsync_DelegatesToStore()
    {
        _projectionStore.GetMaturingBeforeAsync(new DateOnly(2027, 12, 31), Arg.Any<CancellationToken>())
            .Returns([BuildRow(Guid.NewGuid())]);

        var results = await CreateSut().GetMaturingBeforeAsync(new DateOnly(2027, 12, 31));

        results.Should().ContainSingle();
    }

    private static SecurityProjectionRecord MakeProjection(Guid id, string assetClass)
    {
        var empty = System.Text.Json.JsonDocument.Parse("{}").RootElement;
        return new(id, assetClass, SecurityStatusDto.Active, "12M CD", "USD",
            "ISIN", "US1234567890", empty, empty, empty, 1, DateTimeOffset.UtcNow, null, [], []);
    }

    private static CertificateOfDepositProjectionRow BuildRow(Guid id)
        => new(id, "12M CD", "USD", "Meridian Bank", new DateOnly(2027, 6, 30),
            0.051m, new DateOnly(2027, 1, 1), "30/360", "US1234567890", 1);
}

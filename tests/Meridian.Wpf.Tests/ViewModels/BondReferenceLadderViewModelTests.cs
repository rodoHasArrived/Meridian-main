#if WINDOWS
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FixedIncome;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.SecurityMaster;
using Moq;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class BondReferenceLadderViewModelTests
{
    private static BondReferenceDto Row(string id, string name, DateOnly maturity) => new(Guid.Parse(id), name, "USD", "ACME", null, $"ISIN-{name}",
        new BondLifecycleDto(Guid.Parse(id), default, null, null, maturity, false, 1), null, 1);

    private const string A = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string B = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string C = "cccccccc-cccc-cccc-cccc-cccccccccccc";

    [Fact]
    public async Task IssuerLadder_PagesByCursor_AppendsWithoutDuplicates_AndRestartsOnQueryChange()
    {
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client.Setup(c => c.GetBondIssuerLadderPageAsync("ACME", BondReferenceLadderViewModel.PageSize, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BondReferencePageDto>.Ok(new([Row(A, "ACME 5 2030", new(2030, 1, 31)), Row(B, "ACME 4 2031", new(2031, 1, 31))], "cursor-1")));
        client.Setup(c => c.GetBondIssuerLadderPageAsync("ACME", BondReferenceLadderViewModel.PageSize, "cursor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BondReferencePageDto>.Ok(new([Row(B, "ACME 4 2031", new(2031, 1, 31)), Row(C, "ACME 3 2032", new(2032, 1, 31))], null)));
        var view = new BondReferenceLadderViewModel(client.Object);

        view.LoadMoreCommand.CanExecute(null).Should().BeFalse();
        await view.LoadCommand.ExecuteAsync(null);
        view.StatusText.Should().Be("Enter an issuer name.");

        view.IssuerName = "ACME";
        await view.LoadCommand.ExecuteAsync(null);
        view.Rows.Should().HaveCount(2);
        view.HasMore.Should().BeTrue();

        await view.LoadMoreCommand.ExecuteAsync(null);
        view.Rows.Should().HaveCount(3, "the overlapping row is not duplicated");
        view.Rows[2].Maturity.Should().Be("2032-01-31");
        view.HasMore.Should().BeFalse();
        view.StatusText.Should().Contain("end of ladder");

        view.Mode = "Maturity window";
        view.Rows.Should().BeEmpty("a changed query never mixes pages");
        view.IsIssuerMode.Should().BeFalse();
    }

    [Fact]
    public async Task MaturityLadder_RequiresWindow_AndSurfacesRefusals()
    {
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client.Setup(c => c.GetBondMaturityLadderPageAsync(new DateOnly(2029, 1, 1), new DateOnly(2029, 12, 31), BondReferenceLadderViewModel.PageSize, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BondReferencePageDto>.Fail("Cursor does not belong to this query.", 400));
        var view = new BondReferenceLadderViewModel(client.Object) { Mode = "Maturity window" };

        await view.LoadCommand.ExecuteAsync(null);
        view.StatusText.Should().Be("Enter the maturity window start and end dates.");

        view.From = new DateTime(2029, 1, 1);
        view.To = new DateTime(2029, 12, 31);
        await view.LoadCommand.ExecuteAsync(null);
        view.StatusText.Should().Contain("Cursor does not belong to this query.");
        view.IsLoaded.Should().BeFalse();
    }
}
#endif

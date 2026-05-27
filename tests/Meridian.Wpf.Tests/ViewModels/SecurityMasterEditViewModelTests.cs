#if WINDOWS
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Moq;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SecurityMasterEditViewModelTests
{
    [Fact]
    public void CreateNew_UsesCatalogAssetClassesAndIdentifierPreferences()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.AssetClasses.Should().BeEquivalentTo(SecurityAssetClassCatalog.AssetClasses);

            viewModel.AssetClass = "Option";

            viewModel.IdentifierKinds.Should().Contain("OccOptionSymbol");
            viewModel.SupportsSelectedAssetClassCreateWorkflow.Should().BeFalse();

            viewModel.AssetClass = "Equity";

            viewModel.IdentifierKinds.Should().Contain("Ticker");
            viewModel.SupportsSelectedAssetClassCreateWorkflow.Should().BeTrue();
        });
    }

    [Fact]
    public void LoadForEdit_LoadsCommonTermsAndLocksUnsafeIdentityEdits()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.LoadForEdit(CreateSecurityDetail(
                assetClass: "Bond",
                identifierKind: SecurityIdentifierKind.Cusip,
                identifierValue: "037833100",
                commonTerms: new
                {
                    displayName = "Apple Senior Note",
                    currency = "USD",
                    exchange = "TRACE",
                    issuerName = "Apple Inc."
                },
                assetSpecificTerms: new
                {
                    schemaVersion = SecurityMasterSchemaVersions.LegacyAssetSpecificTerms,
                    maturity = "2030-01-15"
                }));

            viewModel.IsEditMode.Should().BeTrue();
            viewModel.Exchange.Should().Be("TRACE");
            viewModel.IssuerName.Should().Be("Apple Inc.");
            viewModel.PrimaryIdentifierKind.Should().Be("CUSIP");
            viewModel.CanChangeAssetClass.Should().BeFalse();
            viewModel.CanEditPrimaryIdentifier.Should().BeFalse();
            viewModel.AssetClassEditabilityHint.Should().Contain("fixed");
            viewModel.PrimaryIdentifierEditabilityHint.Should().Contain("read-only");
        });
    }

    [Fact]
    public void SavePageSource_BindsReadOnlyEditHints()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml"));

        xaml.Should().Contain("{Binding EditVm.CanChangeAssetClass}");
        xaml.Should().Contain("{Binding EditVm.AssetClassEditabilityHint}");
        xaml.Should().Contain("{Binding EditVm.CanEditPrimaryIdentifier}");
        xaml.Should().Contain("{Binding EditVm.PrimaryIdentifierEditabilityHint}");
    }

    [Fact]
    public void SaveAsync_WhenCreatingUnsupportedAssetClass_ShowsValidationErrorWithoutCallingService()
    {
        WpfTestThread.Run(async () =>
        {
            var service = new Mock<ISecurityMasterService>(MockBehavior.Strict);
            var viewModel = CreateViewModel(service);
            viewModel.AssetClass = "Bond";
            viewModel.DisplayName = "Apple Senior Note";
            viewModel.Currency = "USD";
            viewModel.PrimaryIdentifierValue = "037833100";

            await viewModel.SaveCommand.ExecuteAsync(null);

            viewModel.HasErrors.Should().BeTrue();
            viewModel.ValidationErrors.Should().ContainSingle();
            viewModel.ValidationErrors[0].Should().Contain("Bond");
            viewModel.StatusText.Should().Contain("Create blocked");
            service.Verify(
                mock => mock.CreateAsync(It.IsAny<CreateSecurityRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        });
    }

    [Fact]
    public void SaveAsync_WhenEditing_SendsCommonTermsAndAssetSpecificPatch()
    {
        WpfTestThread.Run(async () =>
        {
            AmendSecurityTermsRequest? capturedRequest = null;
            var detail = CreateSecurityDetail(
                assetClass: "Equity",
                identifierKind: SecurityIdentifierKind.Ticker,
                identifierValue: "AAPL",
                commonTerms: new
                {
                    displayName = "Apple Inc.",
                    currency = "USD",
                    exchange = "NASDAQ",
                    issuerName = "Apple Inc."
                },
                assetSpecificTerms: new
                {
                    schemaVersion = SecurityMasterSchemaVersions.LegacyAssetSpecificTerms,
                    classification = "Common"
                });
            var service = new Mock<ISecurityMasterService>(MockBehavior.Strict);
            service
                .Setup(mock => mock.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AmendSecurityTermsRequest, CancellationToken>((request, _) => capturedRequest = request)
                .ReturnsAsync(detail);

            var viewModel = CreateViewModel(service);
            viewModel.LoadForEdit(detail);
            viewModel.DisplayName = "Apple Inc. Class A";
            viewModel.Currency = "USD";
            viewModel.Exchange = "XNAS";
            viewModel.IssuerName = "Apple Issuer";

            await viewModel.SaveCommand.ExecuteAsync(null);

            capturedRequest.Should().NotBeNull();
            capturedRequest!.CommonTerms.Should().NotBeNull();
            capturedRequest.CommonTerms!.Value.GetProperty("displayName").GetString().Should().Be("Apple Inc. Class A");
            capturedRequest.CommonTerms!.Value.GetProperty("currency").GetString().Should().Be("USD");
            capturedRequest.CommonTerms!.Value.GetProperty("exchange").GetString().Should().Be("XNAS");
            capturedRequest.CommonTerms!.Value.GetProperty("issuerName").GetString().Should().Be("Apple Issuer");
            capturedRequest.AssetSpecificTermsPatch.Should().NotBeNull();
            capturedRequest.AssetSpecificTermsPatch!.Value.GetProperty("classification").GetString().Should().Be("Common");
            viewModel.StatusText.Should().Contain("saved successfully");
            service.VerifyAll();
        });
    }

    private static SecurityMasterEditViewModel CreateViewModel(Mock<ISecurityMasterService>? service = null)
        => SecurityMasterEditViewModel.CreateNew(
            WpfServices.LoggingService.Instance,
            NotificationService.Instance,
            (service ?? new Mock<ISecurityMasterService>(MockBehavior.Strict)).Object);

    private static SecurityDetailDto CreateSecurityDetail(
        string assetClass,
        SecurityIdentifierKind identifierKind,
        string identifierValue,
        object commonTerms,
        object assetSpecificTerms)
        => new(
            SecurityId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: "Security Detail",
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(commonTerms),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(assetSpecificTerms),
            Identifiers:
            [
                new SecurityIdentifierDto(
                    Kind: identifierKind,
                    Value: identifierValue,
                    IsPrimary: true,
                    ValidFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
            ],
            Aliases: [],
            Version: 3,
            EffectiveFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null);

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
#endif

#if WINDOWS
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Moq;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Deactivation reaches <see cref="ISecurityMasterService"/> in process, so this dialog's confirm
/// command is the last point where the ModifySecurityMaster grant the HTTP deactivate route
/// requires can be enforced.
/// </summary>
public sealed class SecurityMasterDeactivateViewModelTests
{
    [Fact]
    public void ConfirmAsync_WhenOperatorLacksModifySecurityMaster_RefusesWithoutCallingTheService()
    {
        WpfTestThread.Run(async () =>
        {
            // Strict with no setup: any call to the service fails the test.
            var service = new Mock<ISecurityMasterService>(MockBehavior.Strict);

            var viewModel = CreateViewModel(service, new StubMutationAuthorization(granted: false));

            await viewModel.ConfirmCommand.ExecuteAsync(null);

            viewModel.StatusText.Should().Contain("not permitted");
            service.VerifyNoOtherCalls();
        });
    }

    /// <summary>
    /// A dialog composed without an authorization seam has nobody who checked the write is allowed,
    /// so it refuses rather than defaulting open.
    /// </summary>
    [Fact]
    public void ConfirmAsync_WhenComposedWithoutAuthorizationSeam_RefusesWithoutCallingTheService()
    {
        WpfTestThread.Run(async () =>
        {
            var service = new Mock<ISecurityMasterService>(MockBehavior.Strict);

            var viewModel = new SecurityMasterDeactivateViewModel(
                WpfServices.LoggingService.Instance,
                WpfServices.NotificationService.Instance,
                service.Object);

            await viewModel.ConfirmCommand.ExecuteAsync(null);

            viewModel.StatusText.Should().Contain("not permitted");
            service.VerifyNoOtherCalls();
        });
    }

    [Fact]
    public void ConfirmAsync_WhenGranted_DeactivatesThroughTheService()
    {
        WpfTestThread.Run(async () =>
        {
            DeactivateSecurityRequest? capturedRequest = null;
            var service = new Mock<ISecurityMasterService>(MockBehavior.Strict);
            service
                .Setup(mock => mock.DeactivateAsync(It.IsAny<DeactivateSecurityRequest>(), It.IsAny<CancellationToken>()))
                .Callback<DeactivateSecurityRequest, CancellationToken>((request, _) => capturedRequest = request)
                .Returns(Task.CompletedTask);

            var viewModel = CreateViewModel(service, new StubMutationAuthorization(granted: true));

            await viewModel.ConfirmCommand.ExecuteAsync(null);

            capturedRequest.Should().NotBeNull();
            capturedRequest!.SecurityId.Should().Be(viewModel.SecurityId);
            capturedRequest.ExpectedVersion.Should().Be(viewModel.Version);
            viewModel.StatusText.Should().Contain("deactivated successfully");
            service.VerifyAll();
        });
    }

    private static SecurityMasterDeactivateViewModel CreateViewModel(
        Mock<ISecurityMasterService> service,
        WpfServices.IDesktopMutationAuthorization mutationAuthorization)
        => new(
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance,
            service.Object,
            mutationAuthorization)
        {
            SecurityName = "Apple Inc.",
            SecurityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Version = 3
        };

    /// <summary>
    /// Stands in for the desktop mutation gate. <c>granted: false</c> models an operator the HTTP
    /// lane would refuse — for example a signed-in viewer, or a credential-free host whose
    /// MDC_ANONYMOUS_ROLE names a read-only role.
    /// </summary>
    private sealed class StubMutationAuthorization(bool granted) : WpfServices.IDesktopMutationAuthorization
    {
        public bool IsGranted(UserPermission permission) => granted;
    }
}
#endif

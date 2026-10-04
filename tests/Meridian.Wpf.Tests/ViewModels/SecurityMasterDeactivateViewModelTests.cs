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
/// requires can be enforced. The gate has two halves: the host posture must permit the write at
/// all, and the active desktop session must name an authorized operator to record it against.
/// </summary>
public sealed class SecurityMasterDeactivateViewModelTests
{
    [Fact]
    public void ConfirmAsync_WhenAuthorized_RecordsTheAuthenticatedOperator()
    {
        WpfTestThread.Run(async () =>
        {
            DeactivateSecurityRequest? capturedRequest = null;
            var service = new Mock<ISecurityMasterService>(MockBehavior.Strict);
            service
                .Setup(mock => mock.DeactivateAsync(
                    It.IsAny<DeactivateSecurityRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<DeactivateSecurityRequest, CancellationToken>((request, _) => capturedRequest = request)
                .Returns(Task.CompletedTask);
            var operatorContext = new MutableAuthorizationSource("security.master.owner");
            var viewModel = CreateViewModel(service, operatorContext);

            viewModel.ConfirmCommand.CanExecute(null).Should().BeTrue();
            await viewModel.ConfirmCommand.ExecuteAsync(null);

            capturedRequest.Should().NotBeNull();
            capturedRequest!.UpdatedBy.Should().Be("security.master.owner");
            capturedRequest.SourceSystem.Should().Be("WPF-UI");
            service.VerifyAll();
        });
    }

    [Fact]
    public void ConfirmAsync_WhenOperatorIsViewOnly_DoesNotReachTheService()
    {
        WpfTestThread.Run(async () =>
        {
            var service = new Mock<ISecurityMasterService>(MockBehavior.Strict);
            var operatorContext = new MutableAuthorizationSource(
                "desktop.viewer",
                hasModifyPermission: false);
            var viewModel = CreateViewModel(service, operatorContext);

            viewModel.ConfirmCommand.CanExecute(null).Should().BeFalse();
            await viewModel.ConfirmCommand.ExecuteAsync(null);

            service.Verify(
                mock => mock.DeactivateAsync(
                    It.IsAny<DeactivateSecurityRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        });
    }

    [Fact]
    public void ConfirmAsync_WhenActorBecomesUnresolved_DoesNotReachTheService()
    {
        WpfTestThread.Run(async () =>
        {
            var service = new Mock<ISecurityMasterService>(MockBehavior.Strict);
            var operatorContext = new MutableAuthorizationSource("desktop.admin");
            var viewModel = CreateViewModel(service, operatorContext);

            viewModel.ConfirmCommand.CanExecute(null).Should().BeTrue();
            operatorContext.Actor = null;

            viewModel.ConfirmCommand.CanExecute(null).Should().BeFalse();
            await viewModel.ConfirmCommand.ExecuteAsync(null);

            service.Verify(
                mock => mock.DeactivateAsync(
                    It.IsAny<DeactivateSecurityRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        });
    }

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
    /// A dialog composed without any authorization seam has nobody who checked the write is
    /// allowed, so it refuses rather than defaulting open.
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

    /// <summary>
    /// A granted host posture is not an operator: a dialog composed with only the posture seam
    /// has no one to record the governed write against, so it refuses instead of fabricating
    /// attribution the audit trail would carry as fact.
    /// </summary>
    [Fact]
    public void ConfirmAsync_WhenComposedWithoutOperatorSeam_RefusesInsteadOfFabricatingAttribution()
    {
        WpfTestThread.Run(async () =>
        {
            // Strict with no setup: any call to the service fails the test.
            var service = new Mock<ISecurityMasterService>(MockBehavior.Strict);

            var viewModel = CreateViewModel(service, new StubMutationAuthorization(granted: true));

            viewModel.ConfirmCommand.CanExecute(null).Should().BeFalse();
            await viewModel.ConfirmCommand.ExecuteAsync(null);

            viewModel.StatusText.Should().Contain("Sign in");
            service.VerifyNoOtherCalls();
        });
    }

    private static SecurityMasterDeactivateViewModel CreateViewModel(
        Mock<ISecurityMasterService> service,
        IDesktopAuthorizationSource operatorContext)
        => new(
            LoggingService.Instance,
            NotificationService.Instance,
            service.Object,
            operatorContext)
        {
            SecurityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            SecurityName = "Apple Inc.",
            Version = 4,
            Reason = "Duplicate golden record"
        };

    private static SecurityMasterDeactivateViewModel CreateViewModel(
        Mock<ISecurityMasterService> service,
        WpfServices.IDesktopMutationAuthorization mutationAuthorization)
        => new(
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance,
            service.Object,
            mutationAuthorization: mutationAuthorization)
        {
            SecurityName = "Apple Inc.",
            SecurityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Version = 3
        };

    private sealed class MutableAuthorizationSource(
        string? actor,
        bool hasModifyPermission = true) : IDesktopAuthorizationSource
    {
        public string? Actor { get; set; } = actor;

        public bool TryAuthorize(UserPermission permission, out string resolved)
        {
            resolved = Actor ?? string.Empty;
            return permission == UserPermission.ModifySecurityMaster
                   && hasModifyPermission
                   && Actor is { Length: > 0 };
        }

        public bool TryGetAuthenticatedActor(out string resolved)
            => TryAuthorize(UserPermission.ModifySecurityMaster, out resolved);
    }

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

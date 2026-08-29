#if WINDOWS
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Moq;

namespace Meridian.Wpf.Tests.ViewModels;

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
}
#endif

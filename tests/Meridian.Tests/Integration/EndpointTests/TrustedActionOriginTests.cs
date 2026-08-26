using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Covers the two entry points that decide the origin the human-operator governance gate judges:
/// <see cref="EndpointAuthorization.ResolveTrustedActionOrigin"/> and
/// <see cref="EndpointAuthorization.DeriveActionOriginFromPrincipal"/>.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint suites already prove that a <i>declared</i> automation origin is refused. What they
/// cannot show is the hole #2673 actually reported: that <b>omitting</b> the field used to grant
/// human standing to a caller that does not have it, because the DTO default is the permissive
/// <see cref="OperationsActionOriginDto.HumanOperator"/>. These tests pin both directions, and the
/// monotonicity property that keeps them from trading off against each other.
/// </para>
/// <para>
/// The two entry points exist because two route families have different trust contracts. On the
/// governance-gated material commands the declaration is meaningful and binding when it narrows.
/// On the reconciliation casework adapters the body is legacy browser-supplied input the server is
/// authoritative over — those replace <c>Actor</c> outright, and the origin goes with it. What both
/// must guarantee, and what the last test here pins, is that neither lets a non-interactive
/// principal reach <see cref="OperationsActionOriginDto.HumanOperator"/>.
/// </para>
/// </remarks>
public sealed class TrustedActionOriginTests
{
    private static DefaultHttpContext InteractiveSession()
    {
        var context = new DefaultHttpContext();
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator";
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewTrades;
        return context;
    }

    private static DefaultHttpContext ApiKeyPrincipal()
    {
        var context = new DefaultHttpContext();
        context.Items[LoginSessionMiddleware.CurrentUserKey] = ApiKeyMiddleware.ApiKeyActor;
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.AdminMaintenance;
        context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = true;
        return context;
    }

    [Fact]
    public void OmittedOriginFromAnApiKeyPrincipal_DoesNotBuyHumanStanding()
    {
        // The reported hole: ActionOrigin defaults to HumanOperator, so a service credential
        // satisfied the gate by simply not sending the field.
        var resolved = EndpointAuthorization.ResolveTrustedActionOrigin(
            ApiKeyPrincipal(),
            OperationsActionOriginDto.HumanOperator);

        resolved.Should().Be(OperationsActionOriginDto.AutomationAssistant);
        OperationsOriginGuard.IsHumanOperator(resolved).Should().BeFalse();
    }

    [Fact]
    public void OmittedOriginFromAnAnonymousPrincipal_DoesNotBuyHumanStanding()
    {
        var context = new DefaultHttpContext();
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "anonymous";
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewTrades;
        context.Items[LoginSessionMiddleware.AnonymousPrincipalKey] = true;

        var resolved = EndpointAuthorization.ResolveTrustedActionOrigin(
            context,
            OperationsActionOriginDto.HumanOperator);

        resolved.Should().Be(OperationsActionOriginDto.AutomationAssistant);
    }

    [Fact]
    public void AnUnrecognisedPrincipal_FailsClosed()
    {
        // No actor and no permission snapshot: treated as automation rather than waved through.
        var resolved = EndpointAuthorization.ResolveTrustedActionOrigin(
            new DefaultHttpContext(),
            OperationsActionOriginDto.HumanOperator);

        resolved.Should().Be(OperationsActionOriginDto.AutomationAssistant);
    }

    [Fact]
    public void AnInteractiveSessionKeepsHumanStanding()
    {
        var resolved = EndpointAuthorization.ResolveTrustedActionOrigin(
            InteractiveSession(),
            OperationsActionOriginDto.HumanOperator);

        resolved.Should().Be(OperationsActionOriginDto.HumanOperator);
    }

    [Theory]
    [InlineData(OperationsActionOriginDto.AutomationSuggestion)]
    [InlineData(OperationsActionOriginDto.AssistantDraft)]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    public void DeclaredAutomationSurvivesAnInteractiveSession(OperationsActionOriginDto declared)
    {
        // The capability the gate exists for: automation acting through an operator's session
        // declares itself and must still be refused. Re-deriving from the principal alone would
        // rewrite this to HumanOperator and let the material action through.
        var resolved = EndpointAuthorization.ResolveTrustedActionOrigin(InteractiveSession(), declared);

        resolved.Should().Be(declared, "a declared origin is preserved, including its kind");
        OperationsOriginGuard.IsHumanOperator(resolved).Should().BeFalse();
    }

    [Theory]
    [InlineData(OperationsActionOriginDto.HumanOperator)]
    [InlineData(OperationsActionOriginDto.AutomationSuggestion)]
    [InlineData(OperationsActionOriginDto.AssistantDraft)]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    public void TheClaimCanOnlyNarrowPrivilege_NeverWidenIt(OperationsActionOriginDto declared)
    {
        // Monotonicity is what makes both properties above hold at once: whatever the caller
        // declares, the resolved origin is never more privileged than the principal allows.
        var fromKey = EndpointAuthorization.ResolveTrustedActionOrigin(ApiKeyPrincipal(), declared);
        var fromSession = EndpointAuthorization.ResolveTrustedActionOrigin(InteractiveSession(), declared);

        OperationsOriginGuard.IsHumanOperator(fromKey).Should().BeFalse(
            "a non-interactive principal can never reach human standing");

        if (declared != OperationsActionOriginDto.HumanOperator)
        {
            OperationsOriginGuard.IsHumanOperator(fromSession).Should().BeFalse(
                "declaring automation is binding even on a session");
        }
    }

    [Fact]
    public void DerivingFromThePrincipalIgnoresTheBody_ButStillRefusesAnApiKey()
    {
        // The casework adapters' contract: the browser does not get to label the decision, in
        // either direction. What #2673 required of them is the second assertion -- the constant
        // HumanOperator they used to stamp is gone.
        EndpointAuthorization.DeriveActionOriginFromPrincipal(InteractiveSession())
            .Should().Be(OperationsActionOriginDto.HumanOperator);

        EndpointAuthorization.DeriveActionOriginFromPrincipal(ApiKeyPrincipal())
            .Should().Be(OperationsActionOriginDto.AutomationAssistant);
    }

    [Fact]
    public void DerivingFromAnUnrecognisedPrincipal_FailsClosed()
    {
        EndpointAuthorization.DeriveActionOriginFromPrincipal(new DefaultHttpContext())
            .Should().Be(OperationsActionOriginDto.AutomationAssistant);
    }

    [Theory]
    [InlineData(OperationsActionOriginDto.HumanOperator)]
    [InlineData(OperationsActionOriginDto.AutomationSuggestion)]
    [InlineData(OperationsActionOriginDto.AssistantDraft)]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    public void NeitherEntryPointLetsANonInteractivePrincipalReachHumanStanding(
        OperationsActionOriginDto declared)
    {
        // The property that has to hold across both route families, or #2673 is only half closed.
        // Stated over both entry points together so a future third one cannot quietly opt out.
        OperationsOriginGuard.IsHumanOperator(
                EndpointAuthorization.ResolveTrustedActionOrigin(ApiKeyPrincipal(), declared))
            .Should().BeFalse();

        OperationsOriginGuard.IsHumanOperator(
                EndpointAuthorization.DeriveActionOriginFromPrincipal(ApiKeyPrincipal()))
            .Should().BeFalse();
    }
}

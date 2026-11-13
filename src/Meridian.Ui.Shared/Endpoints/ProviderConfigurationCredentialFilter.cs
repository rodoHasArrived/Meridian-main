using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Maps refused configuration secret persistence to an actionable, secret-free response.</summary>
internal sealed class ProviderConfigurationCredentialFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (ProviderConfigurationCredentialException exception)
        {
            return Results.BadRequest(new { code = "OwnedProviderCredentialsRequired", error = exception.Message });
        }
    }
}

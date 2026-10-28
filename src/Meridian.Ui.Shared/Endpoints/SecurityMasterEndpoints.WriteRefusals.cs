using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using AppSecurityMaster = Meridian.Application.SecurityMaster;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    /// <summary>
    /// Answers a refused create or term amendment with 400 and the reason as the Problem Details
    /// detail (the term-block route's shape) and declares the 400. Only
    /// <see cref="AppSecurityMaster.SecurityMasterWriteRefusedException"/> is translated; it is applied
    /// per route, never to the group, so read routes and deactivate are unaffected.
    /// </summary>
    private static RouteHandlerBuilder AnswerWriteRefusalsWith400(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter(static async (invocation, next) =>
        {
            try
            {
                return await next(invocation).ConfigureAwait(false);
            }
            catch (AppSecurityMaster.SecurityMasterWriteRefusedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        return builder.Produces(StatusCodes.Status400BadRequest);
    }
}

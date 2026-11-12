using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using AppSecurityMaster = Meridian.Application.SecurityMaster;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    /// <summary>
    /// Answers a refused create or term amendment with 400 and the reason as the Problem Details
    /// detail (the term-block route's shape) and declares the 400. A direct amendment the service
    /// refuses under <c>RequireGovernedTermAmendments</c>
    /// (<see cref="AppSecurityMaster.GovernedTermAmendmentRequiredException"/>) answers 403 with the same
    /// title and guidance as the routes' own early check. Only these two exceptions are translated; the
    /// filter is applied per route, never to the group, so read routes and deactivate are unaffected.
    /// </summary>
    private static RouteHandlerBuilder AnswerWriteRefusalsWith400(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter(static async (invocation, next) =>
        {
            try
            {
                return await next(invocation).ConfigureAwait(false);
            }
            catch (AppSecurityMaster.GovernedTermAmendmentRequiredException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: ex.Title,
                    detail: ex.Detail);
            }
            catch (AppSecurityMaster.SecurityMasterWriteRefusedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        return builder.Produces(StatusCodes.Status400BadRequest);
    }
}

namespace Meridian.Ui.Services;

/// <summary>
/// Bridging helpers for callers migrating off the legacy null-returning
/// <see cref="ApiClientService.GetAsync{T}"/>/<see cref="ApiClientService.PostAsync{T}"/> seam
/// (audit finding P7). <see cref="DataOrLoggedNull{T}"/> unwraps an <see cref="ApiResponse{T}"/>
/// to the legacy nullable result while keeping the legacy path's diagnostics: non-404 failures
/// are logged with the operation context before null is returned. Call sites converted with
/// this helper are transport-migrated and ratchet-clean; their public contracts move to
/// <see cref="ApiResponse{T}"/> end-to-end in the follow-on consumer batches.
/// </summary>
public static class ApiResponseExtensions
{
    public static T? DataOrLoggedNull<T>(this ApiResponse<T> response, string operation) where T : class
    {
        if (response.Success)
        {
            return response.Data;
        }

        if (response.StatusCode != 404)
        {
            LoggingService.Instance.LogWarning(
                "{Operation} failed with {Failure}{Detail}; returning null.",
                null,
                ("Operation", operation),
                ("Failure", response.IsConnectionError ? "a connection error" : "HTTP " + response.StatusCode),
                ("Detail", string.IsNullOrWhiteSpace(response.ErrorMessage) ? string.Empty : ": " + response.ErrorMessage));
        }

        return null;
    }
}

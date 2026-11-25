using System.Text.Json;

namespace Meridian.Ui.Services.Services.SecurityMaster;

/// <summary>
/// A parsed Security Master workbench or review-queue failure. The server answers governed-write
/// refusals with a JSON body whose <c>error</c> is a stable code (<c>version-conflict</c>,
/// <c>field-already-staged</c>, <c>revision-state-conflict</c>, <c>workflow-required</c>, ...) and, for the
/// queue routes, a plain sentence. This mirrors the browser <c>classifyWorkbenchWriteError</c> so both
/// workstations name the same recovery.
/// </summary>
public sealed record SecurityMasterWorkbenchFailure(
    int StatusCode,
    string? Code,
    string? Message,
    long? CurrentVersion,
    string? StagedBy,
    Guid? StagedRevisionId,
    string? FieldPath)
{
    public const string FieldAlreadyStaged = "field-already-staged";
    public const string VersionConflict = "version-conflict";
    public const string WorkflowRequired = "workflow-required";

    public bool IsFieldAlreadyStaged => StatusCode == 409 && Code == FieldAlreadyStaged;

    public static SecurityMasterWorkbenchFailure Parse(int statusCode, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new(statusCode, null, null, null, null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new(statusCode, null, body, null, null, null, null);
            }

            var error = String(root, "error");
            var message = String(root, "message") ?? String(root, "detail");
            // Queue routes answer {"error": "<sentence>"}; workbench routes answer {"error": "<code>"}.
            // A code never contains a space, so a spaced error is the operator-facing sentence.
            string? code = error;
            if (error is not null && error.Contains(' ', StringComparison.Ordinal))
            {
                code = null;
                message ??= error;
            }

            message ??= String(root, "title");
            long? currentVersion = root.TryGetProperty("currentVersion", out var version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt64(out var parsedVersion)
                ? parsedVersion
                : null;
            Guid? revisionId = Guid.TryParse(String(root, "revisionId"), out var parsedRevision) ? parsedRevision : null;
            return new(statusCode, code, message, currentVersion, String(root, "stagedBy"), revisionId, String(root, "fieldPath"));
        }
        catch (JsonException)
        {
            return new(statusCode, null, body, null, null, null, null);
        }
    }

    /// <summary>The operator-facing sentence for a passport governed write.</summary>
    public string DescribeWorkbenchWrite() => StatusCode switch
    {
        401 => "Your session is no longer authenticated. Sign in again to continue editing.",
        403 => "You don't have permission to edit this security passport.",
        409 when Code == VersionConflict => CurrentVersion is { } v
            ? $"This passport changed (now v{v}). Reload before retrying so your edit is not lost."
            : "This passport changed. Reload before retrying so your edit is not lost.",
        409 when Code == FieldAlreadyStaged => DescribeFieldAlreadyStaged(),
        409 => Message ?? "This revision is no longer in a state that allows the action. Reload the revision.",
        422 when Code == WorkflowRequired => "Select an approval workflow and an independent reviewer before submitting.",
        422 => Message ?? "The request was understood but could not be processed. Check the required fields.",
        0 => Message ?? "The workbench could not be reached.",
        _ => Message ?? $"The request failed ({StatusCode})."
    };

    /// <summary>The operator-facing sentence for a review-queue read or action.</summary>
    public string DescribeQueueAction(string action) => StatusCode switch
    {
        401 => "Your session is no longer authenticated. Sign in again to continue.",
        403 => $"You don't have permission to {action}.",
        404 => Message ?? "The item no longer exists. Refresh the queue.",
        409 when Code == FieldAlreadyStaged => DescribeFieldAlreadyStaged(),
        409 => Message ?? "The item changed since the queue was loaded. Refresh the queue.",
        501 or 503 => Message ?? "This queue is not available in this host.",
        0 => Message ?? "The server could not be reached.",
        _ => Message ?? $"The request failed ({StatusCode})."
    };

    /// <summary>Names the first stager, matching the browser "Field already staged" banner.</summary>
    private string DescribeFieldAlreadyStaged()
    {
        var field = string.IsNullOrWhiteSpace(FieldPath) ? "this field" : FieldPath;
        return !string.IsNullOrWhiteSpace(StagedBy)
            ? $"{StagedBy} already has an open revision on {field}. Publish or discard it before staging another value."
            : Message ?? "Another open revision already stages this field. Publish or discard it first.";
    }

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}

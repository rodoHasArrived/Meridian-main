using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

// Principal-schedule validation of the workbench command service: a staged schedule must fit the effective
// par/issue/maturity overlay, plus the invariant parsing helpers that read staged overrides and schedule rows.
public sealed partial class SecurityMasterWorkbenchCommandService
{
    /// <summary>
    /// A whole principalSchedule replacement passes the static schema validator on ROW-LOCAL shape
    /// alone (parseable payment dates, positive amounts), but the canonical Bond contract also
    /// binds the schedule to the record it overlays: instalments must fall inside the issue/maturity
    /// window and must not sum past the principal face. The window and par are resolved from the
    /// EFFECTIVE overlay — a staged override of par/issueDate/maturity is what the record reads
    /// after approval, so validating against superseded canonical values would let a staged
    /// <c>par=50</c> coexist with a schedule totaling 80. The check is RECIPROCAL: editing par,
    /// issueDate, or maturity revalidates the effective principal schedule against the proposed
    /// value, so neither staging order slips an inconsistent overlay through. Fails CLOSED when
    /// the retained terms cannot be loaded, matching the reserved namespace's
    /// validated-writes-only contract.
    /// </summary>
    private async Task EnsurePrincipalScheduleFitsEffectiveTermsAsync(
        UpdateSecurityFieldRequest request, string canonicalFieldPath, CancellationToken ct)
    {
        var editsSchedule = string.Equals(canonicalFieldPath, PrincipalSchedulePath, StringComparison.OrdinalIgnoreCase);
        var editsPar = string.Equals(canonicalFieldPath, ParTermPath, StringComparison.OrdinalIgnoreCase);
        var editsIssueDate = string.Equals(canonicalFieldPath, IssueDateTermPath, StringComparison.OrdinalIgnoreCase);
        var editsMaturity = string.Equals(canonicalFieldPath, MaturityTermPath, StringComparison.OrdinalIgnoreCase);
        if (_projectionStore is null
            || (!editsSchedule && !editsPar && !editsIssueDate && !editsMaturity))
        {
            return;
        }

        // A CLEAR of a bound term participates too: removing a staged par/issueDate/maturity (or
        // the schedule itself) reverts that term to the remaining staged/canonical layering, and
        // the effective schedule must still fit the reverted value — otherwise staging par=100,
        // staging a schedule totaling 80, then clearing the par override leaves an approvable
        // overlay whose schedule exceeds the canonical par it now reads against. For a clear the
        // edited term resolves by SKIPPING both the proposed value and its staged override.
        var isClear = string.IsNullOrWhiteSpace(request.NewValue);

        SecurityProjectionRecord? projection = null;
        try
        {
            projection = await _projectionStore.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Loading the projection for {SecurityId} failed while validating field edit {FieldPath} against the principal schedule.",
                request.SecurityId, SanitizeForLog(canonicalFieldPath));
        }

        if (projection is null)
        {
            throw new InvalidOperationException(
                $"The retained terms for security '{request.SecurityId:D}' could not be loaded, so the edit cannot be " +
                "validated against the record's principal schedule, issue/maturity window, and principal face. Retry " +
                "once the projection is available; the namespace only accepts validated writes.");
        }

        var terms = StructuredCashFlowTermsResolver.Resolve(SecurityMasterMapping.ToDetail(projection));
        var stagedOverrides = await TryGetStagedOverrideValuesAsync(request.SecurityId, ct).ConfigureAwait(false);

        // The EFFECTIVE schedule: the proposed replacement when this edit IS one, else a staged
        // schedule override, else the canonical schedule. Same layering for each window term —
        // the proposed value wins for the term being edited.
        var rows = new List<(DateOnly PaymentDate, decimal Amount)>();
        var scheduleJson = editsSchedule
            ? (isClear ? null : request.NewValue)
            : TryGetStagedOverrideValue(stagedOverrides, PrincipalSchedulePath);
        if (scheduleJson is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(scheduleJson);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in document.RootElement.EnumerateArray())
                    {
                        if (TryReadScheduleRowDate(row, out var paymentDate) && TryReadScheduleRowAmount(row, out var amount))
                        {
                            rows.Add((paymentDate, amount));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // The proposed replacement was already shape-validated; a malformed STAGED
                // override cannot supply the effective schedule and is left to its own guards.
            }
        }
        else if (terms.HasPrincipalSchedule)
        {
            foreach (var entry in terms.PrincipalSchedule!)
            {
                rows.Add((entry.PaymentDate, entry.Amount));
            }
        }

        if (rows.Count == 0)
        {
            // No effective contractual schedule — there is nothing for the window or par to bind.
            return;
        }

        var effectivePar = editsPar
            ? (isClear ? terms.PrincipalFace : ParseInvariantDecimal(request.NewValue))
            : ParseInvariantDecimal(TryGetStagedOverrideValue(stagedOverrides, ParTermPath)) ?? terms.PrincipalFace;
        var effectiveIssueDate = editsIssueDate
            ? (isClear ? terms.IssueDate : ParseInvariantDate(request.NewValue))
            : ParseInvariantDate(TryGetStagedOverrideValue(stagedOverrides, IssueDateTermPath)) ?? terms.IssueDate;
        var effectiveMaturity = editsMaturity
            ? (isClear ? terms.MaturityDate : ParseInvariantDate(request.NewValue))
            : ParseInvariantDate(TryGetStagedOverrideValue(stagedOverrides, MaturityTermPath)) ?? terms.MaturityDate;

        var subject = editsSchedule && !isClear
            ? "principalSchedule replacement"
            : isClear
                ? "the effective principal schedule after clearing this override"
                : "the effective principal schedule after this edit";
        var scheduledTotal = 0m;
        foreach (var (paymentDate, amount) in rows)
        {
            scheduledTotal += amount;
            if (effectiveIssueDate is DateOnly issueDate && paymentDate < issueDate)
            {
                throw new ArgumentException(
                    $"{subject} pays principal on {paymentDate:yyyy-MM-dd}, before the record's effective " +
                    $"retained issue date {issueDate:yyyy-MM-dd}.",
                    nameof(request));
            }

            if (effectiveMaturity is DateOnly maturityDate && paymentDate > maturityDate)
            {
                throw new ArgumentException(
                    $"{subject} pays principal on {paymentDate:yyyy-MM-dd}, after the record's effective " +
                    $"retained maturity date {maturityDate:yyyy-MM-dd}.",
                    nameof(request));
            }
        }

        if (effectivePar is > 0m && scheduledTotal > effectivePar.Value)
        {
            throw new ArgumentException(
                $"{subject} instalments total {scheduledTotal} and exceed the record's effective retained " +
                $"principal face {effectivePar.Value}.",
                nameof(request));
        }
    }

    /// <summary>The staged override value at <paramref name="path"/>, matched case-insensitively; null when absent or blank.</summary>
    private static string? TryGetStagedOverrideValue(IReadOnlyDictionary<string, string>? stagedOverrides, string path)
    {
        if (stagedOverrides is null)
        {
            return null;
        }

        foreach (var (overridePath, overrideValue) in stagedOverrides)
        {
            if (string.Equals(overridePath, path, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(overrideValue))
            {
                return overrideValue;
            }
        }

        return null;
    }

    private static decimal? ParseInvariantDecimal(string? value)
        => value is not null
           && decimal.TryParse(value.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateOnly? ParseInvariantDate(string? value)
        => value is not null
           && DateOnly.TryParse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool TryReadScheduleRowDate(JsonElement row, out DateOnly value)
    {
        value = default;
        if (row.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in row.EnumerateObject())
        {
            if (string.Equals(property.Name, "paymentDate", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(property.Value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadScheduleRowAmount(JsonElement row, out decimal value)
    {
        value = default;
        if (row.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in row.EnumerateObject())
        {
            if (string.Equals(property.Name, "amount", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetDecimal(out value))
            {
                return true;
            }
        }

        return false;
    }
}

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical corporate-action lifecycle vocabulary. Write-side states are limited to
/// <see cref="Announced"/>, <see cref="Confirmed"/>, and <see cref="Cancelled"/>; the
/// <see cref="Ex"/> and <see cref="Paid"/> phases are calendar facts derived at read time
/// from ExDate/PayDate rather than provider assertions. A null stored state reads as
/// <see cref="Confirmed"/> so events written before lifecycle support remain valid.
/// Amendments and cancellations are superseding events (via
/// <c>CorporateActionDto.SupersedesCorpActId</c>), never in-place mutations.
/// </summary>
public static class CorporateActionLifecycleStates
{
    public const string Announced = "Announced";
    public const string Confirmed = "Confirmed";
    public const string Ex = "Ex";
    public const string Paid = "Paid";
    public const string Cancelled = "Cancelled";

    /// <summary>States accepted on appended events; Ex/Paid are derived, never written.</summary>
    public static bool IsWriteable(string? state)
        => state is null or Announced or Confirmed or Cancelled;

    /// <summary>
    /// Monotonic ordering for supersede checks: a superseding event may not move the
    /// lifecycle backwards, except to <see cref="Cancelled"/> (which ranks above all
    /// progress states). Unknown states rank as -1 and are rejected by validation.
    /// </summary>
    public static int Rank(string? state) => state switch
    {
        null or "" => 1,
        Announced => 0,
        Confirmed => 1,
        Ex => 2,
        Paid => 3,
        Cancelled => int.MaxValue,
        _ => -1
    };

    /// <summary>Resolves the effective display state, deriving Ex/Paid from dates.</summary>
    public static string Resolve(string? storedState, DateOnly exDate, DateOnly? payDate, DateTimeOffset asOf)
    {
        if (storedState == Cancelled)
        {
            return Cancelled;
        }

        var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime);
        var derived = payDate.HasValue && payDate.Value <= asOfDate
            ? Paid
            : exDate <= asOfDate
                ? Ex
                : storedState ?? Confirmed;

        return Rank(derived) >= Rank(storedState ?? Confirmed) ? derived : storedState ?? Confirmed;
    }
}

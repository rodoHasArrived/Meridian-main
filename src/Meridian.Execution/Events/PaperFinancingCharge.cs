using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Execution.Events;

/// <summary>An immutable cash-paid financing assessment. SourceReference identifies the
/// broker charge or scheduled accrual, independently of its amount, so retries detect conflicts.</summary>
public sealed record PaperFinancingCharge(
    string SessionId,
    string AccountId,
    DateOnly AccrualDate,
    decimal Amount,
    string SourceReference,
    string Rationale,
    string ActorId)
{
    public DateTimeOffset OccurredAt => new(AccrualDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public Guid SourceEventId => Identity("source", SessionId, AccountId,
        AccrualDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), SourceReference);

    public PaperFinancingCharge Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(AccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(Rationale);
        ArgumentException.ThrowIfNullOrWhiteSpace(ActorId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Amount);
        if (AccrualDate == DateOnly.MinValue)
            throw new ArgumentException("An accrual date is required.", nameof(AccrualDate));
        return this;
    }

    internal static Guid Identity(params string[] parts)
    {
        // Length prefixes avoid separator collisions in externally supplied identifiers.
        var canonical = string.Concat(parts.Select(static part =>
            part.Length.ToString(CultureInfo.InvariantCulture) + ":" + part));
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)).AsSpan(0, 16));
    }
}

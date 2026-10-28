namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical open lot for commitment-denominated holdings: private fund interests, unfunded
/// commitments and guarantees, whose <see cref="SecurityAssetClassDescriptor.LotUnit"/> is
/// <see cref="SecurityLotUnitKind.Commitment"/>. The lot's size is the amount committed; how much of
/// it has been called is a separate fact (<see cref="FundedAmount"/>), so funded and unfunded are
/// never inferred from a cost basis. Distributions the manager may recall
/// (<see cref="RecallableDistributions"/>) restore unfunded commitment without changing the
/// commitment itself.
/// </summary>
public sealed record CommitmentLot : IUnitizedLot
{
    public CommitmentLot(
        string lotId,
        Guid securityId,
        DateOnly committedDate,
        decimal totalCommitment,
        decimal fundedAmount = 0m,
        decimal recallableDistributions = 0m)
    {
        if (string.IsNullOrWhiteSpace(lotId))
            throw new ArgumentException("Commitment lot identifier must not be null or whitespace.", nameof(lotId));
        if (securityId == Guid.Empty)
            throw new ArgumentException("Commitment lots must be linked to a Security Master identity.", nameof(securityId));
        if (totalCommitment <= 0m)
            throw new ArgumentOutOfRangeException(nameof(totalCommitment), totalCommitment, "Total commitment must be positive.");
        if (fundedAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(fundedAmount), fundedAmount, "Funded amount cannot be negative.");
        if (recallableDistributions < 0m)
            throw new ArgumentOutOfRangeException(nameof(recallableDistributions), recallableDistributions, "Recallable distributions cannot be negative.");
        if (fundedAmount > totalCommitment + recallableDistributions)
            throw new ArgumentOutOfRangeException(nameof(fundedAmount), fundedAmount, "Funded amount cannot exceed the commitment plus recallable distributions.");

        LotId = lotId.Trim();
        SecurityId = securityId;
        CommittedDate = committedDate;
        TotalCommitment = totalCommitment;
        FundedAmount = fundedAmount;
        RecallableDistributions = recallableDistributions;
    }

    public string LotId { get; }

    /// <summary>A commitment lot is the commitment-denominated case of the shared lot shape: its size is the amount committed.</summary>
    decimal IUnitizedLot.Quantity => TotalCommitment;

    SecurityLotUnitKind IUnitizedLot.UnitKind => SecurityLotUnitKind.Commitment;

    decimal IUnitizedLot.Multiplier => 1m;

    /// <summary>Security Master identity the lot's reference data (sponsor, vintage, fee terms) resolves from.</summary>
    public Guid SecurityId { get; }

    /// <summary>Date the commitment became binding.</summary>
    public DateOnly CommittedDate { get; }

    /// <summary>Total amount committed, in the commitment currency.</summary>
    public decimal TotalCommitment { get; }

    /// <summary>Cumulative capital contributed against the commitment.</summary>
    public decimal FundedAmount { get; }

    /// <summary>Distributions returned to the holder that the manager may call again.</summary>
    public decimal RecallableDistributions { get; }

    /// <summary>Capital still callable: the commitment, less contributions, plus recallable distributions.</summary>
    public decimal UnfundedCommitment => TotalCommitment - FundedAmount + RecallableDistributions;

    /// <summary>Contributions as a fraction of the commitment; exceeds one once recalled distributions are re-contributed.</summary>
    public decimal FundedFraction => FundedAmount / TotalCommitment;

    public bool IsFullyFunded => UnfundedCommitment == 0m;

    /// <summary>Records a capital call being met: <paramref name="amount"/> moves from unfunded to funded.</summary>
    public CommitmentLot Draw(decimal amount)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A drawdown must be positive.");
        if (amount > UnfundedCommitment)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, $"A drawdown cannot exceed the unfunded commitment of {UnfundedCommitment}.");

        return new CommitmentLot(LotId, SecurityId, CommittedDate, TotalCommitment, FundedAmount + amount, RecallableDistributions);
    }

    /// <summary>Records a distribution the manager may recall, which restores unfunded commitment.</summary>
    public CommitmentLot RecordRecallableDistribution(decimal amount)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A recallable distribution must be positive.");

        return new CommitmentLot(LotId, SecurityId, CommittedDate, TotalCommitment, FundedAmount, RecallableDistributions + amount);
    }
}

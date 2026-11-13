namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Adopted book targets match the durable journal's ten decimal places; model inputs stay raw.</summary>
public static class BondIncomeBookedPrecision
{
    public const string PolicyVersion = "bond-booked-target-10dp-v1";
    public const int DecimalPlaces = 10;
    public const decimal Quantum = 0.0000000001m;

    public static decimal RoundTarget(decimal value) => decimal.Round(value, DecimalPlaces, MidpointRounding.ToEven);

    public static void RequireRepresentable(decimal value)
    {
        if (RoundTarget(value) != value)
            throw new ArgumentException("Adopted opening basis and cash must be exactly representable at the ten-decimal journal boundary; retain reviewed rounded opening facts before posting.");
    }

    public static BondIncomeBookedTargetDto Targets(decimal rawTransaction, decimal rawFunctional)
    {
        var transaction = RoundTarget(rawTransaction);
        var functional = RoundTarget(rawFunctional);
        return new(PolicyVersion, DecimalPlaces, rawTransaction, rawFunctional, transaction, functional,
            rawTransaction - transaction, rawFunctional - functional);
    }
}

public sealed record BondIncomeBookedTargetDto(string PolicyVersion, int DecimalPlaces,
    decimal RawTransactionTarget, decimal RawFunctionalTarget, decimal BookedTransactionTarget,
    decimal BookedFunctionalTarget, decimal TransactionRoundingResidual, decimal FunctionalRoundingResidual);

namespace Meridian.Contracts.SecurityMaster;

/// <summary>A calculation capability refusal, separate from the validity of the retained terms.</summary>
public sealed record SecurityCashFlowBlockerDto(string Code, string Reason);

/// <summary>Shared refusal for readers whose cash and posting models cannot represent signed interest.</summary>
public static class SecurityInterestSupport
{
    public const string NegativeInterestUnsupported = "NegativeInterestUnsupported";
    public const string NegativeInterestReason = "Negative interest requires a supported signed-interest model; no cash flows or accruals were generated.";

    public static SecurityCashFlowBlockerDto NegativeInterestBlocker { get; } =
        new(NegativeInterestUnsupported, NegativeInterestReason);
}

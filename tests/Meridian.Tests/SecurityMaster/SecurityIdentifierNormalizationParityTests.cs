using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using FsIdentifierKind = Meridian.FSharp.Domain.IdentifierKind;
using FsSecurityIdentifier = Meridian.FSharp.Domain.SecurityIdentifier;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The F# duplicate/expiry checks and the C# lookup boundary must give one raw identifier value one
/// identity. The F# normalizer delegates to <see cref="SecurityIdentifierNormalizer.NormalizeValue"/>
/// through an exhaustive kind mapping; this pins that every C# kind has an F# counterpart that maps
/// back to itself and normalizes identically.
/// </summary>
public sealed class SecurityIdentifierNormalizationParityTests
{
    public static TheoryData<string> RawValues => new()
    {
        "US0378331005",
        " us 0378-3310 05 ",
        "CH-1.234.567",
        "BRK.B",
        "aapl:xnas",
        "  ",
        "LX 123456",
        "a/b_c=d#e^f",
        "ÉCU0378331005",
        " meridiansynth0000183-trade.0001 ",
        "qz-rbg6 ztks42",
        "2h6677 aa2"
    };

    [Theory]
    [MemberData(nameof(RawValues))]
    public void FSharpNormalizer_AgreesWithTheCSharpNormalizer_ForEverySharedKind(string raw)
    {
        var compared = 0;
        foreach (var kind in Enum.GetValues<SecurityIdentifierKind>())
        {
            var fsKind = typeof(FsIdentifierKind).GetProperty(kind.ToString(), BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as FsIdentifierKind;
            if (fsKind is null)
            {
                continue; // Unknown and ProviderSymbol have no nullary F# counterpart.
            }

            FsSecurityIdentifier.normalizeIdentifierValue(fsKind, raw)
                .Should().Be(SecurityIdentifierNormalizer.NormalizeValue(kind, raw), $"{kind} must normalize identically for '{raw}'");
            compared++;
        }

        // Every kind except Unknown (read tolerance) and ProviderSymbol (carries its provider in the
        // F# case, so it is not nullary) is compared.
        compared.Should().Be(Enum.GetValues<SecurityIdentifierKind>().Length - 2, "every C# kind needs an F# counterpart");
    }

    [Fact]
    public void FSharpKindMapping_RoundTripsEveryContractKind()
    {
        foreach (var kind in Enum.GetValues<SecurityIdentifierKind>())
        {
            if (kind is SecurityIdentifierKind.Unknown)
            {
                continue;
            }

            var fsKind = kind == SecurityIdentifierKind.ProviderSymbol
                ? FsIdentifierKind.NewProviderSymbol("POLYGON")
                : typeof(FsIdentifierKind).GetProperty(kind.ToString(), BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as FsIdentifierKind;

            fsKind.Should().NotBeNull($"{kind} must have an F# IdentifierKind case");
            FsSecurityIdentifier.toContractKind(fsKind!).Should().Be(kind);
        }
    }
}

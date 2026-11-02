using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using FsIdentifierKind = Meridian.FSharp.Domain.IdentifierKind;
using FsSecurityIdentifier = Meridian.FSharp.Domain.SecurityIdentifier;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The F# duplicate/expiry checks and the C# lookup boundary must give one raw identifier value one
/// identity. The F# domain cannot reference Contracts, so its kind-aware normalizer mirrors
/// <see cref="SecurityIdentifierNormalizer.NormalizeValue"/>; this pins the two together for every
/// kind both sides define.
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
        "ÉCU0378331005"
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

        compared.Should().BeGreaterThan(10, "the reflection mapping must actually cover the shared kinds");
    }
}

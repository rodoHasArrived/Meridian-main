using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using FsIdentifierKind = Meridian.FSharp.Domain.IdentifierKind;
using FsSecurityIdentifier = Meridian.FSharp.Domain.SecurityIdentifier;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The F# duplicate/expiry checks and the C# lookup boundary must give one raw identifier value one
/// identity. Meridian.FSharp references Contracts, and its kind-aware normalizer delegates to
/// <see cref="SecurityIdentifierNormalizer.NormalizeValue"/> through
/// <c>SecurityIdentifier.toContractKind</c>; this pins the two together for every kind both sides
/// define, so a mapping slip in the F# kind translation is caught.
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

    [Fact]
    public void FSharpKindTranslation_NamesTheContractsKindOfTheSameName()
    {
        var compared = 0;
        foreach (var kind in Enum.GetValues<SecurityIdentifierKind>())
        {
            if (typeof(FsIdentifierKind).GetProperty(kind.ToString(), BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) is not FsIdentifierKind fsKind)
            {
                continue; // Unknown and ProviderSymbol have no nullary F# counterpart.
            }

            FsSecurityIdentifier.toContractKind(fsKind).Should().Be(kind);
            compared++;
        }

        FsSecurityIdentifier.toContractKind(FsIdentifierKind.NewProviderSymbol("XNAS"))
            .Should().Be(SecurityIdentifierKind.ProviderSymbol);
        compared.Should().BeGreaterThan(10);
    }

    [Theory]
    [InlineData("US0378331005")]
    [InlineData("US0378331006")]
    [InlineData("037833100")]
    [InlineData("037833101")]
    [InlineData("529900T8BM49AURSDO55")]
    [InlineData("529900T8BM49AURSDO56")]
    [InlineData("")]
    public void FSharpCheckDigitValidators_AgreeWithTryValidateFormat_ForCanonicalValues(string value)
    {
        FsSecurityIdentifier.validateIsin(value).Should().Be(SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Isin, value, out _));
        FsSecurityIdentifier.validateCusip(value).Should().Be(SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Cusip, value, out _));
        FsSecurityIdentifier.validateLei(value).Should().Be(SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Lei, value, out _));
    }
}

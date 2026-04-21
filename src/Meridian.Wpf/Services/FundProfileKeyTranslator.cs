using System.Security.Cryptography;
using System.Text;

namespace Meridian.Wpf.Services;

internal static class FundProfileKeyTranslator
{
    public static Guid ToFundId(string fundProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId.Trim()));
        return new Guid(bytes);
    }
}

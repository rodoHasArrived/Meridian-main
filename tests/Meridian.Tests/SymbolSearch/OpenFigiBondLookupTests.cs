using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.SymbolSearch;

[Trait("Category", "Unit")]
public sealed class OpenFigiBondLookupTests
{
    [Fact]
    public async Task BondIdentifierLookup_OmitsEquityDefaultAndRetainsCompetingBondIdentities()
    {
        string? body = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                [{"data":[
                  {"figi":"BOND1","marketSector":"Govt","securityType":"US GOVERNMENT","ticker":"UST"},
                  {"figi":"BOND2","marketSector":"Corp","securityType":"CORP BOND","ticker":"CORP"},
                  {"figi":"EQUITY","marketSector":"Equity","securityType":"Common Stock","ticker":"ABC"}
                ]}]
                """, Encoding.UTF8, "application/json") };
        });
        using var client = new OpenFigiClient("synthetic-key", new HttpClient(handler));
        var result = await client.LookupBondIdentifierAsync("US0000000001", "ISIN");
        body.Should().Contain("ID_ISIN").And.NotContain("Equity").And.NotContain("marketSecDes");
        result.Candidates.Should().HaveCount(2);
        result.IsAmbiguous.Should().BeTrue();
        result.Resolved.Should().BeNull();
    }
}

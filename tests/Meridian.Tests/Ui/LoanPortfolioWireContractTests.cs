using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.DirectLending;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Tests.Ui;

public sealed class LoanPortfolioWireContractTests
{
    [Fact]
    public void HostSerializedPortfolio_ShouldMatchBrowserRegressionFixture()
    {
        var borrowerId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        LoanSummaryDto Loan(string id, string facility, LoanStatus status, CurrencyCode currency, decimal principal) =>
            new(Guid.Parse(id), facility, borrowerId, "Harbour Logistics", status, currency,
                1_000_000m, principal, 0m, 0m, 1_000_000m - principal,
                new DateOnly(2026, 3, 22), new DateOnly(2029, 3, 22), null, null);
        var portfolio = new LoanPortfolioSummaryDto(2, 1, 1, 0, 0,
            null, null, null, null, null, null,
            [Loan("00000000-0000-0000-0000-000000000001", "Harbour USD", LoanStatus.Active, CurrencyCode.USD, 100_000m),
             Loan("00000000-0000-0000-0000-000000000002", "Harbour EUR", LoanStatus.Defaulted, CurrencyCode.EUR, 200_000m)],
            [new(CurrencyCode.USD, 1_000_000m, 100_000m, 0m, 0m, 900_000m, 400_000m),
             new(CurrencyCode.EUR, 1_000_000m, 200_000m, 0m, 0m, 800_000m, 500_000m)]);

        // Use the same options factory as MapUiEndpoints, not hand-authored string-enum
        // test settings: this fixture also drives the actual React screen regression.
        var serialized = JsonSerializer.Serialize(portfolio, UiEndpoints.CreateEndpointJsonOptions());
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
            root = root.Parent;
        root.Should().NotBeNull("the browser fixture belongs to this repository");
        var fixture = File.ReadAllText(Path.Combine(root!.FullName,
            "src", "Meridian.Ui", "dashboard", "src", "test", "fixtures", "loan-portfolio.host.json"));
        JsonNode.DeepEquals(JsonNode.Parse(serialized), JsonNode.Parse(fixture)).Should().BeTrue();
    }
}

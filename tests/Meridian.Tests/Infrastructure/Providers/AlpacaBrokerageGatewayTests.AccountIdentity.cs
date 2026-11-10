using System.Net;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// The real <c>/v2/account</c> response carries two identifiers: the account number an operator
/// sees in Alpaca's dashboard and on statements, and an internal UUID that appears nowhere else.
/// The gateway reports the account number as the account's identity and accepts the UUID as a
/// second name for it. Fixtures that supplied only one of the two hid the difference.
/// </summary>
public sealed partial class AlpacaBrokerageGatewayTests
{
    private const string AccountNumber = "PA3ALPACA01";
    private const string ProviderAccountUuid = "d23b8f95-7654-4c6e-8bf8-b0d4ed1796c2";

    private static StringContent BuildRealAccountResponse(string? accountNumber = AccountNumber, string? id = ProviderAccountUuid) =>
        BuildJson(new
        {
            id,
            account_number = accountNumber,
            equity = "100000",
            cash = "100000",
            buying_power = "400000",
            multiplier = "4",
            trading_blocked = false,
            transfers_blocked = false,
            account_blocked = false,
            currency = "USD",
            status = "ACTIVE"
        });

    private static AlpacaBrokerageGateway CreateAccountIdentitySut(string? accountNumber = AccountNumber, string? id = ProviderAccountUuid) =>
        CreateSut(new CapturingStubHandler(_ => { }, request => request.RequestUri!.AbsolutePath == "/v2/account"
            ? BuildRealAccountResponse(accountNumber, id)
            : BuildJson(Array.Empty<object>())));

    [Fact]
    public async Task AccountIdentity_IsTheOperatorVisibleAccountNumber()
    {
        await using var sut = CreateAccountIdentitySut();

        await sut.ConnectAsync();

        (await sut.GetAccountInfoAsync()).AccountId.Should().Be(AccountNumber);
        var listed = (await sut.GetAccountsAsync()).Should().ContainSingle().Subject;
        listed.AccountId.Should().Be(AccountNumber);
        listed.Metadata![BrokerageExternalAccountMetadata.AccountIdAliases].Should().Be(ProviderAccountUuid,
            "discovery must still match a link retained under the UUID");
    }

    [Fact]
    public async Task ScopeIdentity_StaysOnTheImmutableUuidSoRetainedJournalsStillBind()
    {
        await using var sut = CreateAccountIdentitySut();
        await using var withoutUuid = CreateAccountIdentitySut(id: null);

        await sut.ConnectAsync();
        await withoutUuid.ConnectAsync();

        // The OMS recovery journal refuses any scope string but the one it was written under,
        // and that was the UUID before the account number became the account's name.
        sut.ScopeIdentity.Should().Be($"paper:{ProviderAccountUuid}");
        withoutUuid.ScopeIdentity.Should().Be($"paper:{AccountNumber}");
        (await withoutUuid.GetAccountsAsync()).Single().Metadata!
            .Should().NotContainKey(BrokerageExternalAccountMetadata.AccountIdAliases);
    }

    [Fact]
    public async Task AccountIdentity_FallsBackToTheUuidWhenNoAccountNumberIsReported()
    {
        await using var sut = CreateAccountIdentitySut(accountNumber: null);

        (await sut.GetAccountInfoAsync()).AccountId.Should().Be(ProviderAccountUuid);
    }

    [Theory]
    [InlineData(AccountNumber)]
    [InlineData("pa3alpaca01")]
    [InlineData(ProviderAccountUuid)]
    public async Task PortfolioAndActivity_AcceptEitherNameAndAnswerUnderTheOneRequested(string requested)
    {
        await using var sut = CreateAccountIdentitySut();
        await sut.ConnectAsync();

        var portfolio = await sut.GetPortfolioSnapshotAsync(requested);
        var activity = await sut.GetActivitySnapshotAsync(requested);

        // A retained link compares the evidence with the identifier it holds, so the answer
        // carries that identifier: the account number by default, the UUID only when asked by it.
        var expected = string.Equals(requested, ProviderAccountUuid, StringComparison.Ordinal) ? ProviderAccountUuid : AccountNumber;
        portfolio.Account.AccountId.Should().Be(expected);
        portfolio.AccountSnapshot!.AccountId.Should().Be(expected);
        activity.AccountId.Should().Be(expected);
        sut.IdentifiesConnectedAccount(requested).Should().BeTrue();
    }

    [Fact]
    public async Task PortfolioAndActivity_RefuseAnAccountTheCredentialsDoNotResolve()
    {
        await using var sut = CreateAccountIdentitySut();
        await sut.ConnectAsync();

        var portfolio = () => sut.GetPortfolioSnapshotAsync("PA9SOMEONEELSE");
        var activity = () => sut.GetActivitySnapshotAsync("unknown");

        await portfolio.Should().ThrowAsync<InvalidOperationException>();
        await activity.Should().ThrowAsync<InvalidOperationException>("a missing identity must never match a literal placeholder");
        sut.IdentifiesConnectedAccount("PA9SOMEONEELSE").Should().BeFalse();
        sut.IdentifiesConnectedAccount(" ").Should().BeFalse();
    }

    [Fact]
    public async Task IdentifiesConnectedAccount_IsFalseUntilConnected()
    {
        await using var sut = CreateAccountIdentitySut();

        sut.IdentifiesConnectedAccount(AccountNumber).Should().BeFalse();
        await sut.ConnectAsync();
        sut.IdentifiesConnectedAccount(AccountNumber).Should().BeTrue();
        await sut.DisconnectAsync();
        sut.IdentifiesConnectedAccount(AccountNumber).Should().BeFalse("a disconnect revokes the verified scope");
    }
}

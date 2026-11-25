using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.TestSupport;

/// <summary>
/// June month-end for an approved intercompany loan profile between a lender and a borrower book, with another
/// agreement's rows in the same books. Shared by the confirmation service and endpoint tests; synthetic only.
/// </summary>
internal sealed class IntercompanyBalanceConfirmationFixture : IDisposable
{
    public const string Tenant = "tenant-a";
    public const string Company = "company-a";
    public static readonly Guid Lender = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    public static readonly Guid Borrower = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    public static readonly Guid LenderBook = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    public static readonly Guid BorrowerBook = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    public static readonly Guid Profile = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherProfile = Guid.Parse("c9999999-9999-9999-9999-999999999999");
    public static readonly DateOnly MonthEnd = new(2026, 6, 30);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-confirmation-" + Guid.NewGuid().ToString("N"));

    private IntercompanyBalanceConfirmationFixture() { }

    public IntercompanyBalanceConfirmationService Service { get; private set; } = null!;
    public Schedule Balances { get; } = new();
    public Dictionary<string, FundProfileOwnership> Owners { get; } = new(StringComparer.Ordinal)
    {
        ["lender-fund"] = new("lender-fund", Tenant, Company),
        ["borrower-fund"] = new("borrower-fund", Tenant, Company)
    };
    public Mock<ILedgerJournalStore> Journals { get; } = new();
    public Mock<IFundProfileTenancyRegistry> Registry { get; } = new();

    public static async Task<IntercompanyBalanceConfirmationFixture> CreateAsync()
    {
        var fixture = new IntercompanyBalanceConfirmationFixture();
        await fixture.InitializeAsync();
        return fixture;
    }

    private async Task InitializeAsync()
    {
        var start = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var resolved = IntercompanyArrangementTestData.Create(Profile, Guid.NewGuid(), IntercompanyAccountingTemplateKindDto.Loan, Lender, Borrower, "USD",
            [new(Lender, LenderBook, "a-recv", "a-pay"), new(Borrower, BorrowerBook, "b-recv", "b-pay")], Guid.NewGuid(), start);
        var arrangements = new InMemoryIntercompanyAccountingArrangementStore();
        var approved = resolved.Arrangement;
        var draft = approved with { Version = 1, Status = IntercompanyAccountingArrangementStatusDto.Draft, ApprovedBy = null, ApprovedAt = null };
        await arrangements.AppendAsync(Tenant, 0, new(Guid.NewGuid(), new string('a', 64), IntercompanyAccountingArrangementActionDto.Create, "preparer", start, null, draft));
        await arrangements.AppendAsync(Tenant, 1, new(Guid.NewGuid(), new string('b', 64), IntercompanyAccountingArrangementActionDto.Submit, "preparer", start, null,
            draft with { Version = 2, Status = IntercompanyAccountingArrangementStatusDto.Submitted }));
        await arrangements.AppendAsync(Tenant, 2, new(Guid.NewGuid(), new string('c', 64), IntercompanyAccountingArrangementActionDto.Approve, "reviewer", start, null, approved));
        Registry.Setup(item => item.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string fund, CancellationToken _) => Owners.GetValueOrDefault(fund));
        Journals.Setup(item => item.GetLedgerBookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid book, CancellationToken _) => book == LenderBook
                ? new LedgerBookRecord(LenderBook, "lender-fund", Lender, FundStructureNodeKindDto.Entity, "Lender", "USD", start, start)
                : book == BorrowerBook ? new LedgerBookRecord(BorrowerBook, "borrower-fund", Borrower, FundStructureNodeKindDto.Entity, "Borrower", "USD", start, start) : null);
        Service = new IntercompanyBalanceConfirmationService(
            new FileIntercompanyBalanceConfirmationStore(Path.Combine(_directory, "confirmations.json"), new Accessor()), Balances,
            IntercompanyArrangementTestData.Service(arrangements, resolved), arrangements, Journals.Object, Registry.Object);
    }

    public static IntercompanyBalanceConfirmationActionRequest Confirm(IntercompanyBalanceConfirmationDto current, IntercompanyBalanceConfirmationPartyDto party)
        => new(Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Confirm, party, current.Version, current.BalanceDigest, null);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class Accessor : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => Tenant;
    }

    /// <summary>Posted due-to/due-from per book, including an unrelated agreement's row in each book.</summary>
    public sealed class Schedule : IIntercompanyBalanceScheduleSource
    {
        public decimal Lender { get; set; } = 1_000_000m;
        public decimal Borrower { get; set; } = -1_000_000m;
        public bool Available { get; set; } = true;

        public Task<IntercompanyBalanceScheduleDto> GetAsync(string? tenantId, string? companyId, string? fundProfileId, Guid? ledgerBookId, DateOnly asOf, CancellationToken ct = default)
        {
            var lender = ledgerBookId == LenderBook;
            var (entity, counterparty, amount) = lender ? (IntercompanyBalanceConfirmationFixture.Lender, IntercompanyBalanceConfirmationFixture.Borrower, Lender)
                : (IntercompanyBalanceConfirmationFixture.Borrower, IntercompanyBalanceConfirmationFixture.Lender, Borrower);
            IntercompanyBalanceScheduleRowDto Row(Guid profile, decimal value) => new(ledgerBookId!.Value, entity, counterparty, profile, Guid.NewGuid(), "Loan",
                "node", lender ? IntercompanyAccountRoleDto.Receivable : IntercompanyAccountRoleDto.Payable, "USD", "USD", value, value, true, [Journal(ledgerBookId!.Value, profile, value)], []);
            return Task.FromResult(new IntercompanyBalanceScheduleDto(tenantId!, companyId, null, ledgerBookId, asOf, Available,
                Available ? [Row(Profile, amount), Row(OtherProfile, lender ? 250m : -250m)] : [], [], $"hash-{amount}"));
        }

        // The same posted journal is reported for the same book, profile and amount; a new posting is a new journal.
        private static Guid Journal(Guid book, Guid profile, decimal amount)
            => new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{book:D}|{profile:D}|{amount}"))[..16]);

        public Task<IntercompanyBalanceScheduleDto> GetForCloseAsync(string? tenant, string? company, string? fund, Guid? book, string? periodId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

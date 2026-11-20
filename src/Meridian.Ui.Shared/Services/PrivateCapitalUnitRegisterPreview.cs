using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Reconciles an accepted inception-to-cutoff unit register to the immutable hard-close journal.</summary>
internal static class PrivateCapitalUnitRegisterPreview
{
    public const string PolicyVersion = "single-class-no-fees-v1";
    private const int MaxSourceBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> UnsupportedFeeAccounts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Management Fee Expense", "Management Fee Payable", "Performance Fee Expense", "Performance Fee Payable"
    };
    private static readonly JsonSerializerOptions SourceJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    internal static async Task<PrivateCapitalUnitRegisterPreviewDto> BuildAsync(
        IEvidenceArtifactStore vault, ILedgerJournalStore journalStore, IAtomicLedgerPeriodCloseStore closeStore,
        PrivateCapitalScopeDto scope, PreviewPrivateCapitalUnitRegisterRequest request, CancellationToken ct)
    {
        if (request.PeriodId == Guid.Empty || string.IsNullOrWhiteSpace(request.DocumentId)
            || request.DocumentId.Length > 200 || request.DocumentId.Any(char.IsControl)
            || !EvidenceVaultReference.TryParseCanonical(request.EvidenceLink, out var vaultId))
            throw new ArgumentException("Select an exact accounting period and a reviewed Evidence Vault document.");

        var book = await journalStore.GetLedgerBookAsync(scope.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The ledger book is unavailable.");
        if (book.LedgerBookId != scope.LedgerBookId || !Equal(book.FundProfileId, scope.FundProfileId)
            || !Equal(book.FundStructureNodeId.ToString("D"), scope.EntityId) || !Equal(book.BaseCurrency, scope.Currency))
            throw new InvalidOperationException("The retained ledger book no longer matches the selected unit register scope.");
        var period = await journalStore.GetPeriodAsync(request.PeriodId, ct).ConfigureAwait(false);
        if (period is null || period.LedgerBookId != scope.LedgerBookId || period.Status != "HardClosed")
            throw new InvalidOperationException("Select a hard-closed period in this ledger book.");
        LedgerPeriodCloseSnapshot snapshot;
        LedgerPeriodCloseSnapshotPayload retained;
        try
        {
            snapshot = await closeStore.GetCloseSnapshotAsync(period.PeriodId, period.Version, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The selected period has no retained close snapshot.");
            retained = snapshot.VerifyIntegrity();
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException("The retained close snapshot failed its integrity check.", exception);
        }
        if (snapshot.LedgerBookId != scope.LedgerBookId || snapshot.PeriodVersion != period.Version
            || retained.Period.EndDate != period.EndDate || retained.Period.PeriodId != period.PeriodId)
            throw new InvalidOperationException("The retained close snapshot does not match the selected period.");

        var identity = await vault.TryGetVaultIdentityAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
        var documentId = request.DocumentId.Trim();
        var document = identity?.Documents.SingleOrDefault(item => item.DocumentId == documentId);
        if (identity is null || identity.VaultId != vaultId || !Equal(identity.TenantId, scope.TenantId)
            || !Equal(identity.Scope, scope.CompanyId) || document is null
            || !Equal(document.TenantId, scope.TenantId) || !Equal(document.Scope, scope.CompanyId)
            || document.Classification is not (EvidenceDocumentClassificationDto.AdminPackage or EvidenceDocumentClassificationDto.Statement)
            || document.ReviewerState.Status != EvidenceDocumentReviewStatusDto.Accepted
            || document.ReviewerState.ReviewedAt is null || string.IsNullOrWhiteSpace(document.ReviewerState.Reviewer))
            throw new InvalidOperationException("The exact source register requires accepted Evidence Vault review in this tenant and company.");

        if (!await vault.VerifyRetainedContentAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("The retained unit register source could not be verified.");
        var file = await vault.TryOpenDocumentContentAsync(vaultId, documentId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
        if (file is null)
            throw new InvalidOperationException("The reviewed unit register source bytes are unavailable.");
        await using var content = file.Content;
        var buffer = new byte[MaxSourceBytes + 1];
        var length = await content.ReadAtLeastAsync(buffer, buffer.Length, false, ct).ConfigureAwait(false);
        if (length is 0 or > MaxSourceBytes)
            throw new ArgumentException("The unit register JSON must contain between one byte and two MiB.");
        var bytes = buffer.AsMemory(0, length);
        var sourceHash = Sha256Digest.Compute(bytes.Span);
        if (!Sha256Digest.FixedEquals(sourceHash, document.SourceHashSha256))
            throw new InvalidOperationException("The register bytes do not match the accepted document hash.");

        try
        {
            return Build(scope, book.AccountingBasis, period, retained.Entries, ParseSource(bytes),
                EvidenceVaultReference.CanonicalPrefix + vaultId, documentId, sourceHash, snapshot.ContentHashSha256);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("The source register exceeds supported decimal precision.", exception);
        }
    }

    internal static PrivateCapitalUnitRegisterPreviewDto Build(
        PrivateCapitalScopeDto scope, AccountingBasisKindDto basis, LedgerAccountingPeriod period,
        IReadOnlyList<LedgerJournalEntryRecord> journals, PrivateCapitalUnitRegisterSourceDto source,
        string evidenceLink, string documentId, string sourceHash, string snapshotHash)
    {
        if (source.LedgerBookId != scope.LedgerBookId || source.PeriodId != period.PeriodId
            || source.AsOf != period.EndDate || !Equal(source.EntityId.ToString("D"), scope.EntityId)
            || !Equal(source.FundProfileId, scope.FundProfileId) || source.Currency != scope.Currency)
            throw new ArgumentException("The source register must identify the selected book, legal entity, period cutoff and functional currency.");
        if (source.PolicyVersion != PolicyVersion || string.IsNullOrWhiteSpace(source.ShareClassId)
            || source.InceptionDate == default || source.InceptionDate > source.AsOf || source.InceptionNavPerUnit <= 0
            || source.ClassNav < 0 || source.Movements is null)
            throw new ArgumentException("The register must describe one whole-book class from inception under the single-class no-fees policy.");
        if (source.Movements.Any(movement => movement is null || movement.JournalEntryId == Guid.Empty)
            || source.Movements.GroupBy(movement => movement.JournalEntryId).Any(group => group.Count() != 1))
            throw new ArgumentException("Every source movement requires a unique posted journal identity.");

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        void AddIssue(string code, string message, string target) =>
            issues.Add(new($"private-capital.unit-register.{code}", AccountingConfigurationValidationSeverityDto.Critical,
                message, target, "Review the retained register and governed ledger evidence."));

        var sourceById = source.Movements.ToDictionary(movement => movement.JournalEntryId);
        var accepted = new Dictionary<string, PrivateCapitalUnitRegisterSourceMovementDto>(StringComparer.Ordinal);
        var transactions = new List<UnitTransaction>();
        var sequence = 0;
        var wholeBookNav = 0m;
        foreach (var record in journals.OrderBy(item => item.GlobalSequence).ThenBy(item => item.Entry.JournalEntryId))
        {
            var journal = record.Entry;
            var date = journal.Metadata.EffectiveDate ?? DateOnly.FromDateTime(journal.Timestamp.UtcDateTime);
            var id = journal.JournalEntryId;
            if (date < source.InceptionDate || date > source.AsOf || record.AccountingBasis != basis
                || !journal.IsBalanced || journal.Lines.Any(line => !MatchesLineScope(line, scope)))
                throw new InvalidOperationException("Retained accounting history is outside the declared whole-book inception, cutoff, basis or scope.");

            wholeBookNav += journal.Lines.Where(line => line.Account.AccountType is LedgerAccountType.Asset or LedgerAccountType.Liability)
                .Sum(line => line.Debit - line.Credit);
            if (HasUnsupportedFee(journal))
                AddIssue("unsupported-fees", "A posted management or performance fee requires approved fee state outside this unit register.", id.ToString("D"));
            var type = MovementType(journal.Metadata.FundEventType);
            if (type is null)
            {
                if (LooksLikeUnsupportedCapitalMovement(journal))
                    AddIssue("unsupported-capital", "A retained capital movement requires a policy outside this unit register.", id.ToString("D"));
                continue;
            }
            if (!sourceById.Remove(id, out var movement))
            {
                AddIssue("missing-movement", "A posted subscription or redemption is absent from the reviewed register.", id.ToString("D"));
                continue;
            }
            if (!MatchesSettlement(record, movement, type.Value, date, scope.Currency))
            {
                AddIssue("unsettled-or-mismatched", "The reviewed dealing does not tie to a directly settled investor capital journal.", id.ToString("D"));
                continue;
            }

            // The existing projector sorts by date and transaction ID. Use retained posting order to distinguish
            // same-day movements, then return the exact source journal ID in the response.
            var orderedId = FormattableString.Invariant($"{sequence++:D10}:{id:D}");
            accepted.Add(orderedId, movement);
            transactions.Add(new(orderedId, source.ShareClassId, movement.InvestorId, date, type.Value,
                movement.NavPerUnit, movement.Amount, movement.Units));
        }
        foreach (var extra in sourceById.Keys.Order())
            AddIssue("unposted-movement", "A reviewed dealing has no matching posted journal at this cutoff.", extra.ToString("D"));
        if (wholeBookNav != source.ClassNav || wholeBookNav < 0)
            AddIssue("net-assets", "The reviewed class NAV does not equal whole-book net assets at the retained close.", source.ShareClassId);

        var shareClass = new ShareClass(source.ShareClassId, scope.FundProfileId, source.ShareClassId,
            scope.Currency, 0m, 0m, source.InceptionNavPerUnit, source.InceptionDate);
        var register = ShareClassUnitRegisterProjector.Project(shareClass, transactions, source.AsOf, wholeBookNav);
        foreach (var issue in register.ValidationIssues)
            issues.Add(issue.TargetId is not null && accepted.TryGetValue(issue.TargetId, out var movement)
                ? issue with { TargetId = movement.JournalEntryId.ToString("D") } : issue);
        var holdings = register.Holdings.Select(item => new PrivateCapitalUnitRegisterHoldingDto(item.InvestorId,
            item.Units, item.SubscribedAmount, item.RedeemedAmount, item.EqualisationCredit, item.ContingentRedemption,
            LedgerCurrencyRounding.RoundCurrency(item.Units * register.NavPerUnit, scope.Currency))).ToArray();
        var movements = register.Movements.Select(item =>
        {
            var movement = accepted[item.TransactionId];
            return new PrivateCapitalUnitRegisterMovementDto(movement.JournalEntryId, item.InvestorId,
                movement.CapitalAccountId, item.DealingDate, item.Type.ToString(), item.NavPerUnit, item.Amount,
                item.Units, item.EqualisationCredit, item.ContingentRedemption);
        }).ToArray();
        var residual = wholeBookNav - holdings.Sum(holding => holding.Value);
        var precision = CurrencyMinorUnits.GetPrecision(scope.Currency);
        var minorUnit = 1m;
        for (var place = 0; place < precision; place++)
            minorUnit /= 10m;
        var roundingBound = holdings.Count(holding => holding.Units != 0) * minorUnit / 2m
            + Math.Abs(register.UnitsOutstanding) * 0.0000005m;
        if (Math.Abs(residual) > roundingBound || register.UnitsOutstanding == 0 && wholeBookNav != 0)
            AddIssue("valuation-residual", "The investor holding values do not reconcile to class NAV within the stated rounding policy.", source.ShareClassId);

        return new(scope, period.PeriodId, source.AsOf, source.ShareClassId, scope.Currency, source.PolicyVersion,
            register.UnitsOutstanding, wholeBookNav, register.NavPerUnit, register.HighWaterNavPerUnit, residual,
            holdings, movements, issues, evidenceLink, documentId, sourceHash, snapshotHash, issues.Count == 0);
    }

    private static PrivateCapitalUnitRegisterSourceDto ParseSource(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var parsed = JsonDocument.Parse(bytes);
            EnsureUniqueFields(parsed.RootElement);
            return parsed.Deserialize<PrivateCapitalUnitRegisterSourceDto>(SourceJson)
                ?? throw new JsonException("The source register is empty.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The accepted document must contain one complete, unambiguous unit register JSON object.", exception);
        }
    }

    private static void EnsureUniqueFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                EnsureUniqueFields(item);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            if (!names.Add(field.Name))
                throw new JsonException("Duplicate register fields are ambiguous.");
            if (field.Name is "inceptionNavPerUnit" or "classNav" or "navPerUnit" or "amount" or "units")
            {
                var token = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() : field.Value.GetRawText();
                var parts = token?.Split('.');
                if (parts is null || parts.Length is < 1 or > 2 || parts.Any(part => part.Length == 0
                    || part.Any(ch => ch is < '0' or > '9')) || parts.Sum(part => part.Length) > 28)
                    throw new JsonException("Register amounts must use exact unsigned plain decimals of up to 28 digits.");
            }
            EnsureUniqueFields(field.Value);
        }
    }

    private static bool MatchesSettlement(LedgerJournalEntryRecord record, PrivateCapitalUnitRegisterSourceMovementDto movement,
        UnitTransactionType type, DateOnly date, string currency)
    {
        var journal = record.Entry;
        if (record.PostingKind != LedgerPostingKindDto.Originating || record.SourceJournalEntryId is not null
            || string.IsNullOrWhiteSpace(journal.Metadata.FundEventId)
            || !Equal(journal.Metadata.InvestorId, movement.InvestorId)
            || !Equal(journal.Metadata.CapitalAccountId, movement.CapitalAccountId)
            || string.IsNullOrWhiteSpace(movement.InvestorId) || string.IsNullOrWhiteSpace(movement.CapitalAccountId)
            || movement.Type != type.ToString() || movement.DealingDate != date || movement.NavPerUnit <= 0
            || movement.Units <= 0 || movement.Amount <= 0 || NavPerUnitCalculator.RoundUnits(movement.Units) != movement.Units
            || LedgerCurrencyRounding.RoundCurrency(movement.Amount, currency) != movement.Amount
            || journal.Lines.Count != 2)
            return false;
        var cash = journal.Lines.FirstOrDefault(line => IsSettlementCash(line, currency));
        var equity = journal.Lines.FirstOrDefault(line => line.Account.AccountType == LedgerAccountType.Equity);
        if (cash is null || equity is null
            || equity.Dimensions?.InvestorId is { } investor && !Equal(investor, movement.InvestorId)
            || equity.Dimensions?.CapitalAccountId is { } account && !Equal(account, movement.CapitalAccountId))
            return false;
        var signed = type == UnitTransactionType.Subscription ? movement.Amount : -movement.Amount;
        return cash.Debit - cash.Credit == signed && equity.Credit - equity.Debit == signed
            && (type == UnitTransactionType.Subscription
                ? NavPerUnitCalculator.UnitsForSubscription(movement.Amount, movement.NavPerUnit) == movement.Units
                : NavPerUnitCalculator.CashForRedemption(movement.Units, movement.NavPerUnit, currency) == movement.Amount);
    }

    private static bool MatchesLineScope(LedgerEntry line, PrivateCapitalScopeDto scope) =>
        (line.Dimensions?.BookId is not { } book || Equal(book, scope.LedgerBookId.ToString("D")))
        && (line.Dimensions?.EntityId is not { } entity || Equal(entity, scope.EntityId))
        && (line.Dimensions?.FundId is not { } fund || Equal(fund, scope.FundProfileId))
        && (line.Currency is null || line.Currency.FunctionalCurrency == scope.Currency);

    private static bool LooksLikeUnsupportedCapitalMovement(JournalEntry journal) =>
        (journal.Metadata.InvestorId is not null || journal.Lines.Any(line => line.Account.AccountType == LedgerAccountType.Asset
            && line.Account.Name.StartsWith("Cash", StringComparison.OrdinalIgnoreCase)))
        && journal.Lines.Any(line => line.Account.AccountType == LedgerAccountType.Equity)
        && !Equal(journal.Metadata.FundEventType, "ClosingEntry");

    private static bool HasUnsupportedFee(JournalEntry journal) =>
        Equal(journal.Metadata.FundEventType, "ManagementFee")
        || Equal(journal.Metadata.FundEventType, "PerformanceFee")
        || Equal(journal.Metadata.ActivityType, nameof(AutomatedJournalEventKind.ManagementFeeAccrued))
        || Equal(journal.Metadata.ActivityType, nameof(AutomatedJournalEventKind.PerformanceFeeAccrued))
        || journal.Metadata.Tags?.GetValueOrDefault("recurring.calculation") is
            ManagementFeeAccrualCalculation.CalculationKind or PerformanceFeeAccrualCalculation.CalculationKind
        || journal.Lines.Any(line => UnsupportedFeeAccounts.Contains(line.Account.Name));

    private static bool IsSettlementCash(LedgerEntry line, string currency) =>
        line.Account.AccountType == LedgerAccountType.Asset
        && (Equal(line.Account.Name, LedgerAccounts.Cash.Name) || Equal(line.Account.Name, $"Cash ({currency})"))
        && (line.Currency is null || line.Currency.TransactionCurrency == currency && line.Currency.FunctionalCurrency == currency);

    private static UnitTransactionType? MovementType(string? label) => label?.Trim().ToLowerInvariant() switch
    {
        "subscription" => UnitTransactionType.Subscription,
        "redemption" => UnitTransactionType.Redemption,
        _ => null
    };

    private static bool Equal(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

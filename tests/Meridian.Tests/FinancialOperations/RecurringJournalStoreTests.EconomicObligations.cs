using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations;

public sealed partial class RecurringJournalStoreTests
{
    private static readonly Guid SecurityId = Guid.Parse("25860d11-b2b8-4181-94bb-9805d2625869");
    private static readonly Guid PositionId = Guid.Parse("e575fd71-aa69-45f5-a32e-630a02df817c");

    private static string Obligation(string lot = "lot-a", DateOnly? day = null)
        => RecurringJournalIdentity.FixedIncomeInterestObligationKey(Scope, SecurityId, PositionId, lot, day ?? Date);

    [Fact]
    public async Task EconomicObligations_RestartRetainsWriteOnceOwnershipAndIdempotentSetOrder()
    {
        await SeedAsync();
        var keys = new[] { Obligation(), Obligation("lot-b") };
        RecurringOccurrenceRecord pinned;
        await using (var session = await Store().OpenSessionAsync())
        {
            var claim = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
            pinned = await session.PinEconomicObligationsAsync(claim.OccurrenceKey, keys);
            await session.SetOutcomeAsync(claim.OccurrenceKey, RecurringOccurrenceState.Blocked,
                "Intake interrupted after ownership retention.", "2026-10", null, null, At);
        }

        await using var restarted = await Store().OpenSessionAsync();
        var recovered = restarted.Occurrences.Should().ContainSingle().Subject;
        recovered.EconomicObligationKeys.Should().Equal(keys.Order(StringComparer.Ordinal));
        var repeated = await restarted.PinEconomicObligationsAsync(pinned.OccurrenceKey, keys.Reverse().ToArray());
        repeated.EconomicObligationKeys.Should().Equal(recovered.EconomicObligationKeys!);
        Func<Task> change = async () => await restarted.PinEconomicObligationsAsync(pinned.OccurrenceKey, [Obligation("replacement")]);
        await change.Should().ThrowAsync<InvalidOperationException>().WithMessage("*differ*this occurrence*retained ownership*");
        restarted.Occurrences.Single().EconomicObligationKeys.Should().Equal(recovered.EconomicObligationKeys!);
    }

    [Fact]
    public async Task EconomicObligations_ASecondScheduleCannotOwnAnOverlappingLotDay()
    {
        await SeedTwoSchedulesAsync();
        await using (var session = await Store().OpenSessionAsync())
        {
            var first = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
            await session.PinEconomicObligationsAsync(first.OccurrenceKey, [Obligation(), Obligation("lot-b")]);
            await session.SetOutcomeAsync(first.OccurrenceKey, RecurringOccurrenceState.Blocked,
                "Recover the original intake.", "2026-10", null, null, At);
            var second = await session.ClaimAsync("schedule-b", Date, 1, 1, At);

            Func<Task> duplicate = async () => await session.PinEconomicObligationsAsync(second.OccurrenceKey,
                [Obligation("lot-c"), Obligation("lot-b")]);
            await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already owned*another retained recurring occurrence*");
            session.Occurrences.Single(item => item.OccurrenceKey == second.OccurrenceKey).EconomicObligationKeys.Should().BeNull();
        }
        await using var restarted = await Store().OpenSessionAsync();
        restarted.Occurrences.Should().HaveCount(2);
        restarted.Occurrences.Count(item => item.EconomicObligationKeys is not null).Should().Be(1);
    }

    [Fact]
    public async Task EconomicObligations_IndependentStoreLeasesPermitOneOwner()
    {
        await SeedTwoSchedulesAsync();
        var owners = await Task.WhenAll(new[] { "schedule-a", "schedule-b" }.Select(async schedule =>
        {
            await using var session = await new FileRecurringJournalStore(_directory).OpenSessionAsync();
            var claim = await session.ClaimAsync(schedule, Date, 1, 1, At);
            try
            {
                await session.PinEconomicObligationsAsync(claim.OccurrenceKey, [Obligation()]);
                return true;
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("already owned", StringComparison.Ordinal))
            {
                return false;
            }
        }));

        owners.Count(static success => success).Should().Be(1);
        await using var restarted = await Store().OpenSessionAsync();
        restarted.Occurrences.SelectMany(item => item.EconomicObligationKeys ?? []).Should().ContainSingle().Which.Should().Be(Obligation());
    }

    [Theory]
    [InlineData("overlap")]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    public async Task EconomicObligations_LoadRejectsInvalidOwnershipEvenWithAValidEnvelopeDigest(string fault)
    {
        await SeedTwoSchedulesAsync();
        await using (var session = await Store().OpenSessionAsync())
        {
            await session.ClaimAsync("schedule-a", Date, 1, 1, At);
            await session.ClaimAsync("schedule-b", Date, 1, 1, At);
        }
        var path = Path.Combine(_directory, "recurring-journals.json");
        var envelope = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(path), RecurringJournalJsonContext.Default.RecurringJournalEnvelope)!;
        var first = envelope.Document.Occurrences[0] with
        {
            EconomicObligationKeys = fault switch
            {
                "malformed" => ["unverifiable"],
                "duplicate" => [Obligation(), Obligation()],
                _ => [Obligation()]
            }
        };
        var second = envelope.Document.Occurrences[1] with { EconomicObligationKeys = fault == "overlap" ? [Obligation()] : null };
        var document = envelope.Document with { Occurrences = [first, second] };
        var tampered = new RecurringJournalEnvelope(Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(document,
            RecurringJournalJsonContext.Default.RecurringJournalDocument)), document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(tampered, RecurringJournalJsonContext.Default.RecurringJournalEnvelope);
        await File.WriteAllBytesAsync(path, bytes);

        Func<Task> load = async () => await Store().OpenSessionAsync();
        await load.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
    }

    [Fact]
    public async Task LegacyOccurrenceWireShape_OmitsUnselectedObligationAuthority()
    {
        await SeedAsync();
        await using (var session = await Store().OpenSessionAsync())
            await session.ClaimAsync("schedule-a", Date, 1, 1, At);
        var original = await File.ReadAllBytesAsync(Path.Combine(_directory, "recurring-journals.json"));
        var envelope = JsonSerializer.Deserialize(original, RecurringJournalJsonContext.Default.RecurringJournalEnvelope)!;

        envelope.Document.Occurrences.Single().EconomicObligationKeys.Should().BeNull();
        System.Text.Encoding.UTF8.GetString(original).Should().NotContain("economicObligationKeys");
        JsonSerializer.SerializeToUtf8Bytes(envelope, RecurringJournalJsonContext.Default.RecurringJournalEnvelope).Should().Equal(original);
        await using var reopened = await Store().OpenSessionAsync();
        reopened.Occurrences.Should().ContainSingle();
    }

    [Fact]
    public async Task EconomicObligations_UnsupportedRegistryFailsClosed()
    {
        await SeedAsync();
        await using IRecurringJournalSession session = new LegacySession(await Store().OpenSessionAsync());
        var claim = await session.ClaimAsync("schedule-a", Date, 1, 1, At);

        Func<Task> pin = async () => await session.PinEconomicObligationsAsync(claim.OccurrenceKey, [Obligation()]);
        await pin.Should().ThrowAsync<NotSupportedException>().WithMessage("*cannot retain economic obligation authority*");
        session.Occurrences.Single().EconomicObligationKeys.Should().BeNull();
    }

    [Fact]
    public void EconomicObligationIdentity_IsStableAcrossPresentationAndSensitiveToEveryEconomicIdentity()
    {
        var original = Obligation();
        // Independent Python hashlib vector pins the ownership domain and length-prefixed bytes.
        // Updating a descriptor, template or calculation must never manufacture a new obligation.
        original.Should().Be("64879ffb7b7e9ab926359f1250b63d3cdde3cba608d8db3353c88417fa9c71e6");
        RecurringJournalIdentity.FixedIncomeInterestObligationKey(Scope with { TenantId = " TENANT-A ", CompanyId = "COMPANY-A" },
            SecurityId, PositionId, "lot-a", Date).Should().Be(original);
        string[] alternatives =
        [
            Obligation("lot-b"), Obligation(day: Date.AddDays(1)),
            RecurringJournalIdentity.FixedIncomeInterestObligationKey(Scope with { LedgerBookId = Guid.NewGuid() }, SecurityId, PositionId, "lot-a", Date),
            RecurringJournalIdentity.FixedIncomeInterestObligationKey(Scope with { TenantId = "tenant-b" }, SecurityId, PositionId, "lot-a", Date),
            RecurringJournalIdentity.FixedIncomeInterestObligationKey(Scope with { CompanyId = "company-b" }, SecurityId, PositionId, "lot-a", Date),
            RecurringJournalIdentity.FixedIncomeInterestObligationKey(Scope, Guid.NewGuid(), PositionId, "lot-a", Date),
            RecurringJournalIdentity.FixedIncomeInterestObligationKey(Scope, SecurityId, Guid.NewGuid(), "lot-a", Date)
        ];
        alternatives.Should().OnlyContain(identity => identity != original);
        var keys = new[] { original, Obligation("lot-b") };
        RecurringJournalIdentity.EconomicObligationDigest(keys).Should().Be(RecurringJournalIdentity.EconomicObligationDigest(keys.Reverse().ToArray()));
        Action duplicate = () => RecurringJournalIdentity.EconomicObligationDigest([original, original]);
        duplicate.Should().Throw<ArgumentException>();
        Action empty = () => RecurringJournalIdentity.ValidateEconomicObligationKeys([]);
        empty.Should().Throw<ArgumentException>();
    }

    private async Task SeedTwoSchedulesAsync()
    {
        await SeedAsync();
        await using var session = await Store().OpenSessionAsync();
        await session.RegisterScheduleAsync(new("schedule-b", "fee", new LedgerBookKey("fund-a", "Fund"),
            RecurringJournalCadence.Monthly, Date, "controller", At, new Dictionary<string, decimal> { ["fee"] = 100m }),
            Scope, [Evidence()], "controller", 0, At);
    }

    // Models a pre-extension registry: the interface's default economic pin must refuse authority.
    private sealed class LegacySession(IRecurringJournalSession inner) : IRecurringJournalSession
    {
        public IReadOnlyList<RecurringTemplateDefinition> Templates => inner.Templates;
        public IReadOnlyList<RecurringScheduleDefinition> Schedules => inner.Schedules;
        public IReadOnlyList<RecurringScheduleDefinition> CurrentSchedules => inner.CurrentSchedules;
        public IReadOnlyList<RecurringOccurrenceRecord> Occurrences => inner.Occurrences;
        public IReadOnlyList<RecurringDefinitionActivation> Activations => inner.Activations;
        public RecurringScheduleDefinition GetCurrentSchedule(string scheduleId) => inner.GetCurrentSchedule(scheduleId);
        public RecurringTemplateDefinition GetCurrentTemplate(string templateId) => inner.GetCurrentTemplate(templateId);
        public Task ActivateDefinitionsAsync(string scheduleId, int scheduleVersion, int templateVersion, string actor, string reason, DateTimeOffset now, CancellationToken ct = default)
            => inner.ActivateDefinitionsAsync(scheduleId, scheduleVersion, templateVersion, actor, reason, now, ct);
        public Task<RecurringScheduleDefinition> ConfigureAsync(RecurringJournalSchedule schedule, JournalTemplate template, RecurringJournalScope scope,
            IReadOnlyList<JournalEvidenceReference> evidence, string actor, int expectedScheduleVersion, int expectedTemplateVersion, DateTimeOffset now, CancellationToken ct = default)
            => inner.ConfigureAsync(schedule, template, scope, evidence, actor, expectedScheduleVersion, expectedTemplateVersion, now, ct);
        public Task<RecurringTemplateDefinition> RegisterTemplateAsync(JournalTemplate template, string actor, int expectedVersion, DateTimeOffset now,
            CancellationToken ct = default, RecurringJournalScope? scope = null)
            => inner.RegisterTemplateAsync(template, actor, expectedVersion, now, ct, scope);
        public Task<RecurringScheduleDefinition> RegisterScheduleAsync(RecurringJournalSchedule schedule, RecurringJournalScope scope,
            IReadOnlyList<JournalEvidenceReference> evidence, string actor, int expectedVersion, DateTimeOffset now, CancellationToken ct = default)
            => inner.RegisterScheduleAsync(schedule, scope, evidence, actor, expectedVersion, now, ct);
        public Task<RecurringOccurrenceRecord> ClaimAsync(string scheduleId, DateOnly date, int expectedScheduleVersion,
            int expectedTemplateVersion, DateTimeOffset now, CancellationToken ct = default)
            => inner.ClaimAsync(scheduleId, date, expectedScheduleVersion, expectedTemplateVersion, now, ct);
        public Task<RecurringOccurrenceRecord> SetOutcomeAsync(string key, RecurringOccurrenceState state, string? reason,
            string? periodId, string? lockOwner, string? reopenPath, DateTimeOffset now, CancellationToken ct = default)
            => inner.SetOutcomeAsync(key, state, reason, periodId, lockOwner, reopenPath, now, ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

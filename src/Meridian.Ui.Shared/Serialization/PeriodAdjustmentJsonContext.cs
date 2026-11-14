using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Ui.Shared.Serialization;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PriorPeriodAdjustmentSourceDto))]
[JsonSerializable(typeof(PreparePeriodAdjustmentRequest))]
[JsonSerializable(typeof(AutomatedJournalDraftIntakeResult))]
[JsonSerializable(typeof(AutomatedJournalScheduleWorkItem))]
internal sealed partial class PeriodAdjustmentJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(FileAutomatedJournalScheduleStore.AutomatedJournalScheduleSnapshot))]
internal sealed partial class AutomatedJournalScheduleJsonContext : JsonSerializerContext;

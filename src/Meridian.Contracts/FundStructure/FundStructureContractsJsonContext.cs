using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AccountDetailsDto))]
[JsonSerializable(typeof(CustodianAccountDetailsEnvelopeDto))]
[JsonSerializable(typeof(BankAccountDetailsEnvelopeDto))]
[JsonSerializable(typeof(CustodianAccountDetailsDto))]
[JsonSerializable(typeof(BankAccountDetailsDto))]
[JsonSerializable(typeof(AccountBalanceSnapshotDto))]
[JsonSerializable(typeof(IReadOnlyList<AccountBalanceSnapshotDto>))]
[JsonSerializable(typeof(CustodianPositionLineDto))]
[JsonSerializable(typeof(IReadOnlyList<CustodianPositionLineDto>))]
[JsonSerializable(typeof(BankStatementLineDto))]
[JsonSerializable(typeof(IReadOnlyList<BankStatementLineDto>))]
[JsonSerializable(typeof(AccountReconciliationRunDto))]
[JsonSerializable(typeof(AccountReconciliationResultDto))]
[JsonSerializable(typeof(IReadOnlyList<AccountReconciliationResultDto>))]
[JsonSerializable(typeof(CustodianStatementBatchDto))]
[JsonSerializable(typeof(BankStatementBatchDto))]
[JsonSerializable(typeof(RecordAccountBalanceSnapshotRequest))]
[JsonSerializable(typeof(IngestCustodianStatementRequest))]
[JsonSerializable(typeof(IngestBankStatementRequest))]
[JsonSerializable(typeof(CreateAccountRequest))]
[JsonSerializable(typeof(UpdateCustodianAccountDetailsRequest))]
[JsonSerializable(typeof(UpdateBankAccountDetailsRequest))]
[JsonSerializable(typeof(ReconcileAccountRequest))]
[JsonSerializable(typeof(FundAccountsDto))]
[JsonSerializable(typeof(AccountSummaryDto))]
[JsonSerializable(typeof(IReadOnlyList<AccountSummaryDto>))]
public sealed partial class FundStructureContractsJsonContext : JsonSerializerContext;

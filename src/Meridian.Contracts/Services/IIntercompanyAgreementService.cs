using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Services;

public interface IIntercompanyAgreementService
{
    Task<IntercompanyAgreementListDto> ListAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementQueryDto query, CancellationToken ct = default);
    Task<IntercompanyAgreementDetailDto?> GetAsync(IntercompanyAgreementCallContext context, Guid agreementId, DateOnly? asOf = null, DateTimeOffset? knownAtUtc = null, CancellationToken ct = default);
    Task<IntercompanyAgreementCommandResultDto> CreateAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default);
    Task<IntercompanyAgreementCommandResultDto> CreateProposalAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default);
    Task<IntercompanyAgreementCommandResultDto> SaveDraftAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default);
    Task<IntercompanyAgreementCommandResultDto> SubmitAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default);
    Task<IntercompanyAgreementCommandResultDto> ApproveAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default);
    Task<IntercompanyAgreementCommandResultDto> RejectAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default);
    Task<IntercompanyAgreementCommandResultDto> WithdrawAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default);
}

public interface IIntercompanyAgreementAccessAuthority
{
    Task<bool> CanAccessEntitiesAsync(IntercompanyAgreementCallContext context, IReadOnlyList<Guid> entityIds, CancellationToken ct = default);
}

public interface IIntercompanyAgreementEvidenceAuthority
{
    Task<IReadOnlyList<IntercompanyAgreementEvidenceDto>> ResolveAsync(IntercompanyAgreementCallContext context,
        Guid agreementId, Guid proposalId, IReadOnlyList<IntercompanyAgreementEvidenceDto> evidence, CancellationToken ct = default);
}

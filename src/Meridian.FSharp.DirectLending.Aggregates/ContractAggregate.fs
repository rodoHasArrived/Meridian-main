namespace Meridian.FSharp.DirectLending.Aggregates

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Meridian.Contracts.DirectLending
open Meridian.FSharp.DirectLendingInterop

module internal ContractAggregate =
    let private hashOptions = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let computeTermsHash (terms: DirectLendingTermsDto) =
        let json = JsonSerializer.Serialize(terms, hashOptions)
        let bytes = SHA256.HashData(Encoding.UTF8.GetBytes json)
        Convert.ToHexString bytes

    let createTermsVersion versionNumber terms sourceAction amendmentReason recordedAt =
        LoanTermsVersionDto(
            versionNumber,
            computeTermsHash terms,
            terms,
            sourceAction,
            amendmentReason,
            recordedAt)

    let createLoan (loanId: Guid) (facilityName: string) (borrower: BorrowerInfoDto) (effectiveDate: DateOnly) (terms: DirectLendingTermsDto) (recordedAt: DateTimeOffset) =
        let termsVersion = createTermsVersion 1 terms "LoanCreated" null recordedAt
        let contract =
            LoanContractDetailDto(
                loanId,
                facilityName.Trim(),
                borrower,
                LoanStatus.Draft,
                effectiveDate,
                Nullable(),
                Nullable(),
                1,
                terms,
                [| termsVersion |])

        let currentCommitment = terms.CommitmentAmount
        let servicing =
            LoanServicingStateDto(
                loanId,
                LoanStatus.Draft,
                currentCommitment,
                0m,
                DirectLendingInterop.CalculateAvailableToDraw(currentCommitment, 0m),
                OutstandingBalancesDto(0m, 0m, 0m, 0m, 0m),
                [||],
                null,
                Nullable(),
                Nullable(),
                0L,
                [||],
                [||])

        { Contract = contract
          Servicing = servicing
          TermsVersion = termsVersion }

    let amendTerms (contract: LoanContractDetailDto) (servicing: LoanServicingStateDto) terms amendmentReason recordedAt =
        let nextVersionNumber = contract.CurrentTermsVersion + 1
        let termsVersion = createTermsVersion nextVersionNumber terms "TermsAmended" amendmentReason recordedAt
        let versions =
            contract.TermsVersions
            |> Seq.append [ termsVersion ]
            |> Seq.sortByDescending _.VersionNumber
            |> Seq.toArray

        let updatedContract =
            LoanContractDetailDto(
                contract.LoanId,
                contract.FacilityName,
                contract.Borrower,
                contract.Status,
                contract.EffectiveDate,
                contract.ActivationDate,
                contract.CloseDate,
                nextVersionNumber,
                terms,
                versions)

        let updatedServicing =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                terms.CommitmentAmount,
                servicing.TotalDrawn,
                DirectLendingInterop.CalculateAvailableToDraw(terms.CommitmentAmount, servicing.TotalDrawn),
                servicing.Balances,
                servicing.DrawdownLots,
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                servicing.ServicingRevision,
                servicing.RevisionHistory,
                servicing.AccrualEntries)

        { Contract = updatedContract
          Servicing = updatedServicing
          TermsVersion = termsVersion }

    let activateLoan (contract: LoanContractDetailDto) (servicing: LoanServicingStateDto) activationDate =
        let updatedContract =
            LoanContractDetailDto(
                contract.LoanId,
                contract.FacilityName,
                contract.Borrower,
                LoanStatus.Active,
                contract.EffectiveDate,
                Nullable activationDate,
                contract.CloseDate,
                contract.CurrentTermsVersion,
                contract.CurrentTerms,
                contract.TermsVersions)

        let updatedServicing =
            LoanServicingStateDto(
                servicing.LoanId,
                LoanStatus.Active,
                servicing.CurrentCommitment,
                servicing.TotalDrawn,
                servicing.AvailableToDraw,
                servicing.Balances,
                servicing.DrawdownLots,
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                servicing.ServicingRevision,
                servicing.RevisionHistory,
                servicing.AccrualEntries)

        { Contract = updatedContract
          Servicing = updatedServicing }

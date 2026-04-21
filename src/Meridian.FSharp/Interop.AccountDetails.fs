namespace Meridian.FSharp.AccountDetailsInterop

open System
open System.Runtime.CompilerServices
open Meridian.FSharp.Domain

/// C# callable helpers for FundAccountDetails discriminated union and
/// FundStructure account query functions.
[<Sealed; Extension>]
type FundAccountDetailsInterop private () =

    /// Returns true when the details represent a custodian (DTC, CREST, Euroclear, etc.) account.
    static member IsCustodian(details: FundAccountDetails) =
        FundAccountDetailsOps.isCustodian details

    /// Returns true when the details represent a bank (cash/money-market) account.
    static member IsBank(details: FundAccountDetails) =
        FundAccountDetailsOps.isBank details

    /// Creates a CustodianAccountDetails-backed FundAccountDetails instance.
    static member CreateCustodian(
        subAccountNumber: string,
        dtcParticipantCode: string,
        crestMemberCode: string,
        euroclearAccountNumber: string,
        clearstreamAccountNumber: string,
        primebrokerGiveupCode: string,
        safekeepingLocation: string,
        serviceAgreementReference: string) : FundAccountDetails =
        FundAccountDetails.Custodian {
            SubAccountNumber          = Option.ofObj subAccountNumber
            DtcParticipantCode        = Option.ofObj dtcParticipantCode
            CrestMemberCode           = Option.ofObj crestMemberCode
            EuroclearAccountNumber    = Option.ofObj euroclearAccountNumber
            ClearstreamAccountNumber  = Option.ofObj clearstreamAccountNumber
            PrimebrokerGiveupCode     = Option.ofObj primebrokerGiveupCode
            SafekeepingLocation       = Option.ofObj safekeepingLocation
            ServiceAgreementReference = Option.ofObj serviceAgreementReference
        }

    /// Creates a BankAccountDetails-backed FundAccountDetails instance.
    static member CreateBank(
        accountNumber: string,
        bankName: string,
        branchName: string,
        iban: string,
        bicSwift: string,
        routingNumber: string,
        sortCode: string,
        intermediaryBankBic: string,
        intermediaryBankName: string,
        beneficiaryName: string,
        beneficiaryAddress: string) : FundAccountDetails =
        FundAccountDetails.Bank {
            AccountNumber        = accountNumber
            BankName             = bankName
            BranchName           = Option.ofObj branchName
            Iban                 = Option.ofObj iban
            BicSwift             = Option.ofObj bicSwift
            RoutingNumber        = Option.ofObj routingNumber
            SortCode             = Option.ofObj sortCode
            IntermediaryBankBic  = Option.ofObj intermediaryBankBic
            IntermediaryBankName = Option.ofObj intermediaryBankName
            BeneficiaryName      = Option.ofObj beneficiaryName
            BeneficiaryAddress   = Option.ofObj beneficiaryAddress
        }

    /// Returns the custodian sub-account number if present, otherwise null.
    static member GetCustodianSubAccountNumber(details: FundAccountDetails) : string =
        match FundAccountDetailsOps.tryGetCustodian details with
        | Some d -> d.SubAccountNumber |> Option.defaultValue null
        | None   -> null

    /// Returns the bank IBAN if present, otherwise null.
    static member GetBankIban(details: FundAccountDetails) : string =
        match FundAccountDetailsOps.tryGetBank details with
        | Some d -> d.Iban |> Option.defaultValue null
        | None   -> null

    /// Returns the bank BIC/SWIFT if present, otherwise null.
    static member GetBankBicSwift(details: FundAccountDetails) : string =
        match FundAccountDetailsOps.tryGetBank details with
        | Some d -> d.BicSwift |> Option.defaultValue null
        | None   -> null

namespace Meridian.FSharp.Domain

open System

type SecurityId = SecurityId of Guid

[<RequireQualifiedAccess>]
type IdentifierKind =
    | Ticker
    | Isin
    | Cusip
    | Sedol
    | Figi
    | ProviderSymbol of provider:string
    | InternalCode

type Identifier = {
    Kind: IdentifierKind
    Value: string
    IsPrimary: bool
    ValidFrom: DateTimeOffset
    ValidTo: DateTimeOffset option
}

[<RequireQualifiedAccess>]
module SecurityIdentifier =
    let normalizeValue (value: string) =
        if isNull value then String.Empty else value.Trim().ToUpperInvariant()

    let provider identifier =
        match identifier.Kind with
        | IdentifierKind.ProviderSymbol provider -> Some provider
        | _ -> None

    let kindName identifier =
        match identifier.Kind with
        | IdentifierKind.Ticker -> "Ticker"
        | IdentifierKind.Isin -> "Isin"
        | IdentifierKind.Cusip -> "Cusip"
        | IdentifierKind.Sedol -> "Sedol"
        | IdentifierKind.Figi -> "Figi"
        | IdentifierKind.ProviderSymbol _ -> "ProviderSymbol"
        | IdentifierKind.InternalCode -> "InternalCode"

    let isActiveAt asOf identifier =
        identifier.ValidFrom <= asOf
        && identifier.ValidTo |> Option.forall (fun validTo -> validTo > asOf)

    let sameIdentity left right =
        left.Kind = right.Kind
        && String.Equals(normalizeValue left.Value, normalizeValue right.Value, StringComparison.Ordinal)
        && String.Equals(
            left |> provider |> Option.defaultValue String.Empty |> normalizeValue,
            right |> provider |> Option.defaultValue String.Empty |> normalizeValue,
            StringComparison.Ordinal)

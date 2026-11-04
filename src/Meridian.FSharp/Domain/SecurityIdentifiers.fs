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
    | OccOptionSymbol
    | ProviderSymbol of provider:string
    | InternalCode
    /// Legal Entity Identifier (ISO 17442) — 20-char alphanumeric; required for OTC derivatives regulatory reporting.
    | Lei
    /// Refinitiv/LSEG PermID — stable cross-asset persistent identifier.
    | PermId
    /// Bloomberg Global Identifier (BBGID) — stable across corporate actions; distinct from ticker.
    | Bbgid
    /// Wertpapierkennnummer — German/Austrian exchange standard (6 alphanumeric chars).
    | Wkn
    /// Valoren — Swiss SIX exchange security number.
    | Valoren
    /// Meridian-stable ticker that survives corporate actions (Bloomberg PermTicker convention).
    | PermTicker
    /// Reuters Instrument Code — used by Refinitiv Eikon / LSEG feeds.
    | Ric
    /// SEC Central Index Key — identifies an EDGAR filer or issuer, not a standalone tradable security.
    | Cik
    /// IHS Markit LoanX ID — syndicated-loan identifier ("LX" + six digits).
    | LoanX
    /// CUSIP-format loan identifier (CLIP) for syndicated loan facilities and tranches.
    | Clip
    /// Listing identity: a ticker on a specific ISO 10383 market, written "TICKER:MIC" (e.g. "AAPL:XNAS").
    | ListingId

type Identifier = {
    Kind: IdentifierKind
    Value: string
    IsPrimary: bool
    ValidFrom: DateTimeOffset
    ValidTo: DateTimeOffset option
    /// Optional source/provider namespace retained as identifier metadata. Identifier identity
    /// continues to use the ProviderSymbol discriminant only, so adding source metadata to an
    /// exchange-standard identifier does not change duplicate or expiry semantics.
    Provider: string option
}

[<RequireQualifiedAccess>]
module SecurityIdentifier =
    let normalizeValue (value: string) =
        if isNull value then String.Empty else value.Trim().ToUpperInvariant()

    /// The Contracts identifier kind this F# kind names. The two enumerations declare the same
    /// cases; ProviderSymbol carries its namespace in the F# discriminant and maps to the bare
    /// Contracts kind. Contracts has no reference to this project, so delegating to it is the
    /// dependency-safe direction (no project-reference cycle).
    let toContractKind (kind: IdentifierKind) : Meridian.Contracts.SecurityMaster.SecurityIdentifierKind =
        match kind with
        | IdentifierKind.Ticker -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Ticker
        | IdentifierKind.Isin -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Isin
        | IdentifierKind.Cusip -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Cusip
        | IdentifierKind.Sedol -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Sedol
        | IdentifierKind.Figi -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Figi
        | IdentifierKind.OccOptionSymbol -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.OccOptionSymbol
        | IdentifierKind.ProviderSymbol _ -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.ProviderSymbol
        | IdentifierKind.InternalCode -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.InternalCode
        | IdentifierKind.Lei -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Lei
        | IdentifierKind.PermId -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.PermId
        | IdentifierKind.Bbgid -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Bbgid
        | IdentifierKind.Wkn -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Wkn
        | IdentifierKind.Valoren -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Valoren
        | IdentifierKind.PermTicker -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.PermTicker
        | IdentifierKind.Ric -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Ric
        | IdentifierKind.Cik -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Cik
        | IdentifierKind.LoanX -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.LoanX
        | IdentifierKind.Clip -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Clip
        | IdentifierKind.ListingId -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.ListingId

    /// Kind-aware value normalization. Delegates to the single authority,
    /// Meridian.Contracts.SecurityMaster.SecurityIdentifierNormalizer.NormalizeValue (which also
    /// mirrors migration 016's SQL backfill), so the F# duplicate/expiry checks and the C# lookup
    /// give one raw value one identity.
    let normalizeIdentifierValue (kind: IdentifierKind) (value: string) =
        Meridian.Contracts.SecurityMaster.SecurityIdentifierNormalizer.NormalizeValue(toContractKind kind, value)

    let provider identifier =
        match identifier.Kind with
        | IdentifierKind.ProviderSymbol provider -> Some provider
        | _ -> None

    /// ProviderSymbol carries its identity namespace in the discriminant. When the optional
    /// provider metadata is also populated, it may repeat that namespace but must not contradict
    /// it. Provider metadata on every other identifier kind remains independent provenance.
    let providerMetadataMatchesKind identifier =
        match identifier.Kind, identifier.Provider with
        | IdentifierKind.ProviderSymbol authoritativeProvider, Some metadataProvider ->
            String.Equals(
                normalizeValue authoritativeProvider,
                normalizeValue metadataProvider,
                StringComparison.Ordinal)
        | _ -> true

    let kindName identifier =
        match identifier.Kind with
        | IdentifierKind.Ticker -> "Ticker"
        | IdentifierKind.Isin -> "Isin"
        | IdentifierKind.Cusip -> "Cusip"
        | IdentifierKind.Sedol -> "Sedol"
        | IdentifierKind.Figi -> "Figi"
        | IdentifierKind.OccOptionSymbol -> "OccOptionSymbol"
        | IdentifierKind.ProviderSymbol _ -> "ProviderSymbol"
        | IdentifierKind.InternalCode -> "InternalCode"
        | IdentifierKind.Lei -> "Lei"
        | IdentifierKind.PermId -> "PermId"
        | IdentifierKind.Bbgid -> "Bbgid"
        | IdentifierKind.Wkn -> "Wkn"
        | IdentifierKind.Valoren -> "Valoren"
        | IdentifierKind.PermTicker -> "PermTicker"
        | IdentifierKind.Ric -> "Ric"
        | IdentifierKind.Cik -> "Cik"
        | IdentifierKind.LoanX -> "LoanX"
        | IdentifierKind.Clip -> "Clip"
        | IdentifierKind.ListingId -> "ListingId"

    let isActiveAt asOf identifier =
        identifier.ValidFrom <= asOf
        && identifier.ValidTo |> Option.forall (fun validTo -> validTo > asOf)

    let sameIdentity left right =
        left.Kind = right.Kind
        && String.Equals(
            normalizeIdentifierValue left.Kind left.Value,
            normalizeIdentifierValue right.Kind right.Value,
            StringComparison.Ordinal)
        && String.Equals(
            left |> provider |> Option.defaultValue String.Empty |> normalizeValue,
            right |> provider |> Option.defaultValue String.Empty |> normalizeValue,
            StringComparison.Ordinal)

    /// Format and check-digit validation through the C# authority
    /// (SecurityIdentifierNormalizer.TryValidateFormat). The value must already be in canonical
    /// form after trim/upper-case: these validators never accepted embedded punctuation, so a value
    /// the kind-aware normalizer would strip is still rejected rather than silently cleaned.
    let private validatesAs (kind: IdentifierKind) (value: string) =
        let basic = normalizeValue value
        basic.Length > 0
        && String.Equals(basic, normalizeIdentifierValue kind value, StringComparison.Ordinal)
        && fst (Meridian.Contracts.SecurityMaster.SecurityIdentifierNormalizer.TryValidateFormat(toContractKind kind, basic))

    /// Validates an ISIN (12 characters, Luhn mod-10 over the letter-expanded digits).
    /// Returns true when the check digit is valid.
    let validateIsin (isin: string) = validatesAs IdentifierKind.Isin isin

    /// Validates a CUSIP (Committee on Uniform Security Identification Procedures) check digit.
    /// Returns true when the check digit is valid. The C# normalizer strips the '*', '@' and '#'
    /// private-placement characters, so a CUSIP carrying them is rejected here.
    let validateCusip (cusip: string) = validatesAs IdentifierKind.Cusip cusip

    /// Validates an LEI (Legal Entity Identifier, ISO 17442) using ISO 7064 MOD 97-10 over the code
    /// in its written order. Returns true when the check digits are valid.
    let validateLei (lei: string) = validatesAs IdentifierKind.Lei lei

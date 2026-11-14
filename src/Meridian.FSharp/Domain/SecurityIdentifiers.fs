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
    /// Unique Transaction Identifier (ISO 23897) — LEI prefix plus unique value, at most 52 characters.
    | Uti
    /// Unique Product Identifier (ISO 4914) — shared by every contract on the same OTC derivative product.
    | Upi
    /// IHS Markit RED reference-entity code (RED6, six characters).
    | MarkitRedEntity
    /// IHS Markit RED pair code (RED9: entity code plus three-character obligation suffix).
    | MarkitRedPair

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

    /// The Contracts kind this F# kind corresponds to. Exhaustive on purpose: a new F# case cannot
    /// compile until it names its C# counterpart, and the C# normalizer stays the single authority.
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
        | IdentifierKind.Uti -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Uti
        | IdentifierKind.Upi -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.Upi
        | IdentifierKind.MarkitRedEntity -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.MarkitRedEntity
        | IdentifierKind.MarkitRedPair -> Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.MarkitRedPair

    /// Kind-aware value normalization. Delegates to
    /// Meridian.Contracts.SecurityMaster.SecurityIdentifierNormalizer.NormalizeValue so the F#
    /// duplicate/expiry checks, the C# lookup boundary and the stored normalized columns share one
    /// implementation and give one raw value one identity.
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

    /// The persisted kind name — the Contracts enum member name, so the stored string and the C#
    /// <c>SecurityIdentifierKind</c> parse cannot drift.
    let kindName identifier =
        (toContractKind identifier.Kind).ToString()

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

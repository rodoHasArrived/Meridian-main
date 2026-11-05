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

    /// Validates an ISIN using the Luhn mod-10 algorithm applied to a numeric-expanded form.
    /// Returns true when the check digit is valid.
    let validateIsin (isin: string) =
        let v = if isNull isin then String.Empty else isin.Trim().ToUpperInvariant()
        if v.Length <> 12 then false
        else
            // Expand letters to digits (A=10 … Z=35) then validate Luhn mod-10
            // Fail fast on any character that is not A-Z or 0-9
            let charValues =
                v
                |> Seq.map (fun c ->
                    if c >= 'A' && c <= 'Z' then
                        let n = int c - int 'A' + 10
                        Some [ n / 10; n % 10 ]
                    elif c >= '0' && c <= '9' then
                        Some [ int c - int '0' ]
                    else None)
                |> Seq.toArray
            if charValues |> Array.exists Option.isNone then false
            else
                let digits =
                    charValues
                    |> Array.choose id
                    |> Array.collect Array.ofList
                let len = digits.Length
                let mutable sum = 0
                let mutable doubleIt = false          // check digit (rightmost) is never doubled in Luhn
                for i = len - 1 downto 0 do
                    let mutable d = digits.[i]
                    if doubleIt then
                        d <- d * 2
                        if d > 9 then d <- d - 9
                    sum <- sum + d
                    doubleIt <- not doubleIt
                sum % 10 = 0

    /// Validates a CUSIP (Committee on Uniform Security Identification Procedures) check digit.
    /// Returns true when the check digit is valid.
    let validateCusip (cusip: string) =
        let v = if isNull cusip then String.Empty else cusip.Trim().ToUpperInvariant()
        let tryGetCusipValue c =
            if c >= '0' && c <= '9' then Some (int c - int '0')
            elif c >= 'A' && c <= 'Z' then Some (int c - int 'A' + 10)
            elif c = '*' then Some 36
            elif c = '@' then Some 37
            elif c = '#' then Some 38
            else None

        if v.Length <> 9 then false
        elif v.[8] < '0' || v.[8] > '9' then false
        else
            let digits =
                v
                |> Seq.take 8
                |> Seq.map tryGetCusipValue
                |> Seq.toArray

            if digits |> Array.exists Option.isNone then false
            else
                let sum =
                    digits
                    |> Array.map Option.get
                    |> Array.mapi (fun i d -> if i % 2 = 1 then d * 2 else d)
                    |> Array.sumBy (fun d -> d / 10 + d % 10)

                let check = (10 - (sum % 10)) % 10
                check = (int v.[8] - int '0')
    /// Validates an LEI (Legal Entity Identifier, ISO 17442) using MOD 97-10 (ISO 7064).
    /// Returns true when the check digits are valid.
    let validateLei (lei: string) =
        let v = if isNull lei then String.Empty else lei.Trim().ToUpperInvariant()
        if v.Length <> 20 then false
        else
            // Rearrange: move first 4 chars to end, then expand letters to digits
            let rearranged = v.[4..] + v.[..3]
            let numericParts =
                rearranged
                |> Seq.map (fun c ->
                    if c >= 'A' && c <= 'Z' then Some (string (int c - int 'A' + 10))
                    elif c >= '0' && c <= '9' then Some (string (int c - int '0'))
                    else None)
                |> Seq.toArray
            if numericParts |> Array.contains None then
                false
            else
                let numericStr =
                    numericParts
                    |> Array.choose id
                    |> String.concat ""
                // MOD 97 on large integer via chunked processing
                let mutable remainder = 0
                for chunk in numericStr |> Seq.chunkBySize 9 do
                    let n = System.Int64.Parse(string remainder + System.String(chunk))
                    remainder <- int (n % 97L)
                remainder = 1

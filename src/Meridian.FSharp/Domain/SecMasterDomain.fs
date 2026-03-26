/// Comprehensive Security Master domain model.
/// Self-contained module using abbreviated names suited for quant/financial modelling.
/// Does not replace the existing SecurityMaster.fs types; kept as a parallel representation
/// that can be used for bulk import, ETL pipelines, and advanced analytics scenarios.
namespace Meridian.FSharp.Domain.SecMasterDomain

open System

// ==========================
// Id
// ==========================

type SecId = SecId of string
type IssId = IssId of string
type LeId = LeId of string
type CptyId = CptyId of string
type ExchId = ExchId of string
type CorpActId = CorpActId of string
type OptChainId = OptChainId of string

// ==========================
// Code
// ==========================

type Ccy =
    | USD | EUR | GBP | JPY | CHF | CAD | AUD

type Ctry =
    | US | GB | DE | FR | JP | CH | CA | AU

type SecStat =
    | Active
    | Inactive
    | Matured
    | Expired
    | Delisted
    | PendingIssue

type ExtIdSch =
    | ISIN
    | CUSIP
    | SEDOL
    | FIGI
    | Bloomberg
    | ReutersRIC
    | LEI
    | MIC
    | Vendor of string

type BusDayConv =
    | Following
    | ModFollowing
    | Preceding
    | ModPreceding
    | Unadj

type DayCnt =
    | Act360
    | Act365
    | Thirty360

type SettleTyp =
    | Physical
    | Cash

type ExStyle =
    | European
    | American
    | Bermudan

type OptRight =
    | Call
    | Put

type AssetCls =
    | Equity
    | Debt
    | Fund
    | Fx
    | Commodity
    | Deriv
    | StructProd

type EqCat =
    | CommonEq
    | PtyEq
    | PrefEq
    | DepRecEq
    | ReitEq
    | TrackingEq
    | FundLikeEq

type DebtCat =
    | CorpBond
    | GovtBond
    | MuniBond
    | ConvBond
    | Note
    | CommPaper

type PrefDivCat =
    | FixedDiv
    | FloatDiv
    | FixToFloatDiv
    | ParticipDiv
    | OtherPrefDiv of string

type LeCat =
    | Corp
    | Govt
    | Sov
    | Bank
    | BrokerDealer
    | CCP
    | FundVeh
    | ExchOp
    | PtyShip
    | SPV
    | OtherLe of string

type IssCat =
    | CorpIss
    | SovIss
    | MuniIss
    | AgencyIss
    | FundIss
    | PtyIss
    | StructIss
    | OtherIss of string

type CptyRole =
    | Trading
    | ClrBroker
    | Custodian
    | PrimeBroker
    | SwapDealer
    | CalcAgent
    | PayAgent
    | CCPRole
    | OtherCptyRole of string

type NetAgrCat =
    | ISDA
    | GMRA
    | GMSLA
    | FutClrAgr
    | PrimeBrkAgr
    | OtherAgr of string

type VenueCat =
    | Exchange
    | MTF
    | OtcVenue
    | DarkPool
    | ClrHouse
    | Depository
    | OtherVenue of string

type DerivCat =
    | Future
    | Option
    | Swap

type CorpActCat =
    | CashDiv
    | StockDiv
    | SpecialDiv
    | Split
    | RevSplit
    | RightsIssue
    | SpinOff
    | Merger
    | Acquisition
    | SymbolChg
    | NameChg
    | Delist
    | BondCall
    | BondPut
    | MatRed
    | TenderOffer
    | OtherCorpAct of string

type CorpActStat =
    | Announced
    | Effective
    | Cancelled
    | Superseded

type CorpActTermCat =
    | Div
    | SplitAct
    | SymbolChgAct
    | MergerAct
    | BondRedAct
    | GenericAct

type SettleStyle =
    | AmSettle
    | PmSettle
    | UnknownSettle

type SecDefCat =
    | CashDef
    | AssetDef
    | DerivDef

type AssetDefCat =
    | EqAsset
    | DebtAsset
    | FundAsset
    | FxAsset
    | CmdtyAsset

type EqDefCat =
    | ComShr
    | MlpUnit
    | PrefShr
    | ConvPref

// ==========================
// Record
// ==========================

type ExtId =
    { Sch : ExtIdSch
      Val : string }

type SecIds =
    { SecId : SecId
      ExtIds : ExtId list
      PrimId : ExtId option }

type LeIds =
    { LeId : LeId
      ExtIds : ExtId list
      PrimId : ExtId option }

type Rating =
    { Agcy : string
      Rate : string
      Outlook : string option
      EffDt : DateOnly }

type Le =
    { Ids : LeIds
      Name : string
      ShortName : string option
      Cat : LeCat
      DomicileCtry : Ctry option
      IncorpCtry : Ctry option
      Web : string option
      IsActive : bool
      AsOf : DateTimeOffset
      Ver : int }

type Iss =
    { IssId : IssId
      LeId : LeId
      Cat : IssCat
      Sector : string option
      Industry : string option
      Ratings : Rating list
      DfltCcy : Ccy option
      IsActive : bool
      AsOf : DateTimeOffset
      Ver : int }

type NetAgr =
    { Cat : NetAgrCat
      Ref : string
      EffDt : DateOnly option
      TermDt : DateOnly option }

type Cpty =
    { CptyId : CptyId
      LeId : LeId
      Roles : CptyRole list
      Book : string option
      NetAgrs : NetAgr list
      EligCcys : Ccy list
      IsActive : bool
      AsOf : DateTimeOffset
      Ver : int }

type Session =
    { Name : string
      OpenTm : TimeSpan
      CloseTm : TimeSpan }

type Exch =
    { ExchId : ExchId
      Name : string
      Mic : string option
      OpMic : string option
      Cat : VenueCat
      Ctry : Ctry option
      Tz : string
      Ccy : Ccy option
      Cal : string option
      Sessions : Session list
      IsActive : bool
      ExtIds : ExtId list
      AsOf : DateTimeOffset
      Ver : int }

type Ref =
    { ShortName : string
      LongName : string option
      Desc : string option
      Ccy : Ccy option
      IssId : IssId option
      PrimExchId : ExchId option
      IssueCtry : Ctry option
      TrdCal : string option
      IssueDt : DateOnly option
      MatDt : DateOnly option
      SettleDays : int option
      SecStat : SecStat
      LotSize : decimal option
      TickSize : decimal option
      Tags : Set<string> }

type OwnTr =
    { HasVoting : bool option
      HasResidual : bool option
      HasDivElig : bool option
      OwnForm : string option }

type IncTr =
    { HasFixLikePay : bool option
      RateCat : string option
      StatedRate : decimal option
      PayFreq : int option
      DayCnt : DayCnt option }

type SenTr =
    { Rank : string option
      HasPrefOverCom : bool option
      IsJrToDebt : bool option }

type RedTr =
    { IsRedeemable : bool option
      IsCallable : bool option
      FirstCallDt : DateOnly option
      CallPx : decimal option
      HasPar : bool option
      ParVal : decimal option }

type ConvTr =
    { IsConvertible : bool option
      ToSecId : SecId option
      ConvRatio : decimal option
      ConvPx : decimal option
      ConvStartDt : DateOnly option
      ConvEndDt : DateOnly option }

type ListTr =
    { IsExchTraded : bool
      Symbol : string option
      IsListed : bool }

type EconTr =
    { OwnTr : OwnTr option
      IncTr : IncTr option
      SenTr : SenTr option
      RedTr : RedTr option
      ConvTr : ConvTr option
      ListTr : ListTr option }

type ComShrDef =
    { Tkr : string
      ShareCls : string option
      VoteCls : string option
      ShrOut : decimal option
      IsStapled : bool }

type MlpUnitDef =
    { Tkr : string
      PtyName : string option
      GpIssId : IssId option
      HasIdr : bool option
      UnitOut : decimal option }

type PrefShrDef =
    { Tkr : string
      Series : string option
      DivCat : PrefDivCat
      DivRate : decimal option
      IsCumul : bool option
      IsParticip : bool option
      IsPerp : bool option
      LiqPref : decimal option
      ParVal : decimal option
      IsRedeemable : bool option
      IsCallable : bool option }

type ConvPrefDef =
    { PrefShrDef : PrefShrDef
      ToSecId : SecId
      ConvRatio : decimal option
      ConvPx : decimal option
      MandConvDt : DateOnly option
      HasAntiDil : bool option }

type BondDef =
    { CpnRate : decimal option
      CpnFreq : int option
      DayCnt : DayCnt option
      SenRank : string option
      IsCallable : bool
      IsPuttable : bool }

type FundDef =
    { FundCat : string
      Bmk : string option
      IsEtf : bool }

type FxDef =
    { BaseCcy : Ccy
      QuoteCcy : Ccy }

type CmdtyDef =
    { Name : string
      Grade : string option
      Uom : string option }

type UnderRef =
    { UnderSecId : SecId
      Wt : decimal option }

type FutDef =
    { UnderRef : UnderRef
      ExpDt : DateOnly
      SettleTyp : SettleTyp
      CntrSize : decimal option
      LastTrdDt : DateOnly option }

type OptDef =
    { UnderRef : UnderRef
      OptRight : OptRight
      ExStyle : ExStyle
      Strike : decimal
      ExpDt : DateOnly
      SettleTyp : SettleTyp
      Mult : decimal option
      CntrMonth : string option
      OptChainId : OptChainId option }

type SwapLegDef =
    { Ccy : Ccy
      Notional : decimal option
      RateIdx : string option
      FixedRate : decimal option
      SpreadBps : decimal option
      DayCnt : DayCnt option
      PayFreq : int option
      BusDayConv : BusDayConv option }

type SwapDef =
    { EffDt : DateOnly
      TermDt : DateOnly
      PayLegDef : SwapLegDef
      RecLegDef : SwapLegDef
      UnderRefs : UnderRef list
      CptyId : CptyId option
      ClrVenueId : ExchId option }

type SecClass =
    { AssetCls : AssetCls
      Cat : string
      SubCat : string option }

type DivTerms =
    { GrossAmt : decimal option
      NetAmt : decimal option
      Ccy : Ccy
      RecDt : DateOnly option
      ExDt : DateOnly option
      PayDt : DateOnly option }

type SplitTerms =
    { Num : int
      Den : int
      ExDt : DateOnly }

type SymbolChgTerms =
    { OldTkr : string
      NewTkr : string
      EffDt : DateOnly }

type MergerTerms =
    { SurvivorIssId : IssId option
      SurvivorSecId : SecId option
      ExRatio : decimal option
      CashComp : decimal option
      Ccy : Ccy option
      EffDt : DateOnly }

type BondRedTerms =
    { RedPxPct : decimal option
      EffDt : DateOnly }

type CorpAct =
    { CorpActId : CorpActId
      Cat : CorpActCat
      Stat : CorpActStat
      AffectedSecIds : SecId list
      IssId : IssId option
      AnnDt : DateOnly option
      EffDt : DateOnly option
      Terms : CorpActTerms
      Notes : string option
      AsOf : DateTimeOffset
      Ver : int }

and CorpActTerms =
    | DivTerms of DivTerms
    | SplitTerms of SplitTerms
    | SymbolChgTerms of SymbolChgTerms
    | MergerTerms of MergerTerms
    | BondRedTerms of BondRedTerms
    | GenericTerms of string

type OptChainTmpl =
    { UnderSecId : SecId
      Root : string
      ExchId : ExchId option
      ExStyle : ExStyle
      SettleTyp : SettleTyp
      SettleStyle : SettleStyle
      Mult : decimal option
      Unit : decimal option
      Ccy : Ccy option }

type ListedExp =
    { ExpDt : DateOnly
      LastTrdDt : DateOnly option
      IsWeekly : bool
      IsMonthly : bool
      IsQuarterly : bool
      IsLeap : bool }

type StrikeLadder =
    { ExpDt : DateOnly
      Strikes : decimal list }

type OptSeriesRef =
    { SecId : SecId
      ExpDt : DateOnly
      Strike : decimal
      OptRight : OptRight }

type OptChain =
    { OptChainId : OptChainId
      Tmpl : OptChainTmpl
      ListedExps : ListedExp list
      StrikeLadders : StrikeLadder list
      OptSeriesRefs : OptSeriesRef list
      AsOf : DateTimeOffset
      Ver : int }

type SecIssLnk =
    { SecId : SecId
      IssId : IssId
      IsPrimary : bool }

type SecExchLnk =
    { SecId : SecId
      ExchId : ExchId
      Symbol : string option
      IsPrimaryList : bool }

type SecCptyLnk =
    { SecId : SecId
      CptyId : CptyId
      RelCat : string }

type CorpActSecLnk =
    { CorpActId : CorpActId
      SecId : SecId
      Role : string }

type Sec =
    { SecIds : SecIds
      SecClass : SecClass
      Ref : Ref
      EconTr : EconTr option
      SecDef : SecDef
      AsOf : DateTimeOffset
      Ver : int }

and SecDef =
    | CashSecDef
    | AssetSecDef of AssetSecDef
    | DerivSecDef of DerivSecDef

and AssetSecDef =
    | EqAssetDef of EqAssetDef
    | DebtAssetDef of BondDef
    | FundAssetDef of FundDef
    | FxAssetDef of FxDef
    | CmdtyAssetDef of CmdtyDef

and EqAssetDef =
    | ComShrEqDef of ComShrDef
    | MlpUnitEqDef of MlpUnitDef
    | PrefShrEqDef of PrefShrDef
    | ConvPrefEqDef of ConvPrefDef

and DerivSecDef =
    | FutSecDef of FutDef
    | OptSecDef of OptDef
    | SwapSecDef of SwapDef

type SecMaster =
    { Secs : Map<SecId, Sec>
      Les : Map<LeId, Le>
      Issuers : Map<IssId, Iss>
      Cptys : Map<CptyId, Cpty>
      Exchs : Map<ExchId, Exch>
      CorpActs : Map<CorpActId, CorpAct>
      OptChains : Map<OptChainId, OptChain>
      SecIssLnks : SecIssLnk list
      SecExchLnks : SecExchLnk list
      SecCptyLnks : SecCptyLnk list
      CorpActSecLnks : CorpActSecLnk list
      AsOf : DateTimeOffset
      Ver : int }

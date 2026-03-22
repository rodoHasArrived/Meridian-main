module Meridian.FSharp.Promotion.PromotionTypes

type PromotionDecision =
    | Eligible
    | Ineligible of reasons: string list
    | ManualReview of reasons: string list

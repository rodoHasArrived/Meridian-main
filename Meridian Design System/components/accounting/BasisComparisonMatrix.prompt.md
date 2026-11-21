One economic object across accounting and reporting bases — statutory, GAAP, tax, management, regulatory, local entity.

```jsx
<BasisComparisonMatrix
  object="CUSIP 912828ZK5"
  bases={[{id:"stat",label:"STAT",meta:"SSAP 26"},{id:"gaap",label:"GAAP",meta:"ASC 320"},{id:"tax",label:"Tax"},{id:"mgmt",label:"Management"}]}
  rows={[{label:"Carrying value",unit:"USD",values:{stat:9874221.14,gaap:10108332.06,tax:{value:9772118.4,method:"Tax basis",condition:"provisional"},mgmt:10042630}},
         {label:"Measurement",differenceKind:"Method",values:{stat:"Amortized cost",gaap:"Fair value",tax:"Tax basis",mgmt:"Economic"}}]}
  referenceBasis="stat"
  period="July 2026"
  onSelectCell={sel => inspect(sel)}
/>
```

**Why a matrix.** This is the Plate principle applied to accounting: comparable objects get the same geometry, order, units, and hierarchy, so the reader compares by position instead of hunting. Every basis is a column of identical shape; every measure is a row with one unit.

**The reference basis is named.** Differences are always measured against a stated basis — shown in the header, marked on its column, and repeated in the footer. A difference with an unnamed reference is not a difference.

**Difference is written, not colored.** Selection tint aside, every difference appears as words and numbers under the value: an amount, a percentage, a basis-point spread, or — for classification, method, and timing rows — a sentence naming both treatments. Color alone never carries the comparison. Set `differenceKind` to control that wording per row.

**Missing is not not-applicable.** An absent value renders `—`; a value that does not apply to a basis renders `n/a` under `condition: "na"`. The two are never merged, and `Not comparable` is its own result rather than a zero difference.

**Cells carry their provenance.** A cell may be a plain value or an object with `method`, `source`, `asOf`, `condition`, and `override` — all rendered beneath the number. `onSelectCell` gives you the row, basis, cell, and reference cell for an inspector; `onOpenTrace` and `onOpenEvidence` wire to `CalculationTrace` and `EvidencePreview`.

**Units and export.** Rows declare their own unit and are never compared across incompatible units. `notation="accounting"` renders negatives in parentheses. The table caption states the object, reference basis, and difference mode, so the exported or spoken form carries the same contract as the screen.

// Meridian — Basket Builder demo data. Plain script (window global), not a module.
(function () {
  const RATING_SCALE = ["AAA","AA+","AA","AA-","A+","A","A-","BBB+","BBB","BBB-","BB+","BB","BB-","B+","B","B-","CCC+","CCC","CCC-","CC","C","D"];
  function ratingIndex(label) { const i = RATING_SCALE.indexOf(label); return i === -1 ? 8 : i; }

  function summarizeBasketRows(rows) {
    if (!rows.length) {
      return { count: 0, mv: 0, dv01: 0, liquidityScore: 0, illiquidPct: 0, estCostBps: 0, modDuration: 0, ytw: 0, rating: "—" };
    }
    const mv = rows.reduce((s, r) => s + r.mv, 0);
    const w = (f) => rows.reduce((s, r) => s + f(r) * r.mv, 0) / mv;
    const dv01 = rows.reduce((s, r) => s + (r.mv * r.modDuration) / 10000, 0);
    const illiquidPct = (rows.filter((r) => r.illiquid).reduce((s, r) => s + r.mv, 0) / mv) * 100;
    const ratingIdx = Math.round(w((r) => ratingIndex(r.rating)));
    return {
      count: rows.length, mv, dv01,
      liquidityScore: w((r) => r.liquidityScore),
      illiquidPct,
      estCostBps: w((r) => r.estCostBps),
      modDuration: w((r) => r.modDuration),
      ytw: w((r) => r.ytw),
      rating: RATING_SCALE[Math.min(RATING_SCALE.length - 1, Math.max(0, ratingIdx))],
    };
  }

  function navSeries(seed, n, drift) {
    let v = 100; const out = [];
    for (let i = 0; i < n; i++) { v *= 1 + (Math.sin((i + seed) / 4.3) * 0.003 + drift); out.push(+v.toFixed(3)); }
    return out;
  }

  const igCreditRows = [
    { name: "JPMorgan Chase & Co. 5.75% Hybrid 2034", shortCode: "JPM HYBRID 34", isin: "US46647PDR40", cusip: "46647PDR4", direction: "Buy", faceValue: 7968000, mv: 8168472.09, accrued: 0.00, benchmark: "GV91282CNC1", indQuantity: 10000000, sector: "Financial Services", rating: "BBB+", modDuration: 8.2, ytw: 5.58, liquidityScore: 1.4, illiquid: false, estCostBps: 1.10 },
    { name: "Bank of America Corp. 5.60% Hybrid 2034", shortCode: "BAC HYBRID 34", isin: "US06051GLH03", cusip: "06051GLH0", direction: "Buy", faceValue: 7788000, mv: 7896396.81, accrued: 0.01, benchmark: "GV91282CNC1", indQuantity: 10000000, sector: "Financial Services", rating: "BBB+", modDuration: 8.0, ytw: 5.61, liquidityScore: 1.5, illiquid: false, estCostBps: 1.15 },
    { name: "Wells Fargo & Co. 5.50% Hybrid 2034", shortCode: "WFC HYBRID 34", isin: "US95000U3F87", cusip: "95000U3F8", direction: "Buy", faceValue: 6336000, mv: 6504152.05, accrued: 0.02, benchmark: "GV91282CNC1", indQuantity: 10000000, sector: "Financial Services", rating: "BBB", modDuration: 8.1, ytw: 5.66, liquidityScore: 1.6, illiquid: false, estCostBps: 1.22 },
    { name: "Morgan Stanley 5.40% Hybrid 2034", shortCode: "MS HYBRID 34", isin: "US61747YFG55", cusip: "61747YFG5", direction: "Buy", faceValue: 6300000, mv: 6427240.53, accrued: 0.02, benchmark: "GV91282CNC1", indQuantity: 10000000, sector: "Financial Services", rating: "BBB+", modDuration: 8.0, ytw: 5.52, liquidityScore: 1.5, illiquid: false, estCostBps: 1.18 },
    { name: "Citigroup Inc. 6.27% Hybrid 2033", shortCode: "C HYBRID 33", isin: "US172967PA37", cusip: "172967PA3", direction: "Buy", faceValue: 4752000, mv: 5095739.10, accrued: 0.01, benchmark: "GV91282CNC1", indQuantity: 10000000, sector: "Financial Services", rating: "BBB", modDuration: 7.6, ytw: 5.71, liquidityScore: 1.7, illiquid: false, estCostBps: 1.30 },
    { name: "Oracle Corporation 6.900% 2052", shortCode: "ORCL 6.9 52", isin: "US68389XCK95", cusip: "68389XCK9", direction: "Buy", faceValue: 2988000, mv: 3290853.67, accrued: 0.01, benchmark: "GV912810UG1", indQuantity: 10000000, sector: "Technology", rating: "A-", modDuration: 12.4, ytw: 5.60, liquidityScore: 1.3, illiquid: false, estCostBps: 0.95 },
    { name: "Comcast Corporation 5.350% 2053", shortCode: "CMCSA 5.35 53", isin: "US20030NEF44", cusip: "20030NEF4", direction: "Buy", faceValue: 3168000, mv: 2912516.26, accrued: 0.01, benchmark: "GV912810UG1", indQuantity: 10000000, sector: "Media & Telecom", rating: "A-", modDuration: 13.1, ytw: 5.62, liquidityScore: 1.4, illiquid: false, estCostBps: 1.02 },
    { name: "UnitedHealth Group Inc. 5.050% 2053", shortCode: "UNH 5.05 53", isin: "US91324PEW87", cusip: "91324PEW8", direction: "Sell", faceValue: 3156000, mv: 2783847.35, accrued: 0.01, benchmark: "GV912810UG1", indQuantity: 10000000, sector: "Healthcare", rating: "A", modDuration: 13.6, ytw: 5.44, liquidityScore: 1.2, illiquid: false, estCostBps: 0.88 },
    { name: "AT&T Inc. 3.500% 2053", shortCode: "T 3.5 53", isin: "US00206RKJ07", cusip: "00206RKJ0", direction: "Sell", faceValue: 4140000, mv: 2772673.92, accrued: 0.01, benchmark: "GV912810UG1", indQuantity: 10000000, sector: "Media & Telecom", rating: "BBB", modDuration: 14.8, ytw: 5.75, liquidityScore: 1.9, illiquid: true, estCostBps: 1.85 },
    { name: "Verizon Communications Inc. 3.875% 2052", shortCode: "VZ 3.875 52", isin: "US92343VGP38", cusip: "92343VGP3", direction: "Sell", faceValue: 3540000, mv: 2611163.86, accrued: 0.01, benchmark: "GV912810UG1", indQuantity: 10000000, sector: "Media & Telecom", rating: "BBB+", modDuration: 14.2, ytw: 5.68, liquidityScore: 1.8, illiquid: true, estCostBps: 1.72 },
    { name: "AbbVie Inc. 4.250% 2049", shortCode: "ABBV 4.25 49", isin: "US00287YCB35", cusip: "00287YCB3", direction: "Buy", faceValue: 2604000, mv: 2122759.68, accrued: 0.00, benchmark: "GV912810UG1", indQuantity: 10000000, sector: "Healthcare", rating: "A-", modDuration: 13.9, ytw: 5.50, liquidityScore: 1.3, illiquid: false, estCostBps: 0.92 },
  ];

  const ratesHedgeRows = [
    { name: "U.S. Treasury Note 4.25% 2029", shortCode: "UST 4.25 29", isin: "US91282CJL60", cusip: "91282CJL6", direction: "Buy", faceValue: 15000000, mv: 15420300.00, accrued: 0.18, benchmark: "GV91282CJL6", indQuantity: 15000000, sector: "Government", rating: "AAA", modDuration: 3.9, ytw: 4.18, liquidityScore: 1.05, illiquid: false, estCostBps: 0.20 },
    { name: "U.S. Treasury Note 4.00% 2034", shortCode: "UST 4.00 34", isin: "US91282CJT06", cusip: "91282CJT0", direction: "Buy", faceValue: 12000000, mv: 12180000.00, accrued: 0.11, benchmark: "GV91282CJT0", indQuantity: 12000000, sector: "Government", rating: "AAA", modDuration: 8.1, ytw: 4.24, liquidityScore: 1.08, illiquid: false, estCostBps: 0.24 },
    { name: "U.S. Treasury Bond 3.875% 2043", shortCode: "UST 3.875 43", isin: "US91282CKZ11", cusip: "91282CKZ1", direction: "Buy", faceValue: 9000000, mv: 8760000.00, accrued: 0.09, benchmark: "GV91282CKZ1", indQuantity: 9000000, sector: "Government", rating: "AAA", modDuration: 14.6, ytw: 4.41, liquidityScore: 1.12, illiquid: false, estCostBps: 0.31 },
    { name: "U.S. Treasury Note 4.625% 2031", shortCode: "UST 4.625 31", isin: "US91282CKM09", cusip: "91282CKM0", direction: "Buy", faceValue: 6000000, mv: 6145000.00, accrued: 0.14, benchmark: "GV91282CKM0", indQuantity: 6000000, sector: "Government", rating: "AAA", modDuration: 5.3, ytw: 4.20, liquidityScore: 1.06, illiquid: false, estCostBps: 0.18 },
  ];

  const hyOpportunisticRows = [
    { name: "Community Health Systems 8.00% 2029", shortCode: "CYH 8.00 29", isin: "US20338QAL27", cusip: "20338QAL2", direction: "Buy", faceValue: 3000000, mv: 2940000.00, accrued: 0.09, benchmark: "GV912810UG1", indQuantity: 3000000, sector: "Healthcare", rating: "B-", modDuration: 2.6, ytw: 9.85, liquidityScore: 2.4, illiquid: true, estCostBps: 3.10 },
    { name: "Carnival Corporation 7.625% 2032", shortCode: "CCL 7.625 32", isin: "US143658BM82", cusip: "143658BM8", direction: "Buy", faceValue: 2500000, mv: 2587500.00, accrued: 0.05, benchmark: "GV912810UG1", indQuantity: 2500000, sector: "Consumer Discretionary", rating: "BB", modDuration: 5.4, ytw: 7.42, liquidityScore: 1.9, illiquid: false, estCostBps: 2.05 },
    { name: "Occidental Petroleum 6.625% 2030", shortCode: "OXY 6.625 30", isin: "US674599CH76", cusip: "674599CH7", direction: "Sell", faceValue: 2000000, mv: 2065000.00, accrued: 0.04, benchmark: "GV912810UG1", indQuantity: 2000000, sector: "Energy", rating: "BB+", modDuration: 3.5, ytw: 6.60, liquidityScore: 1.7, illiquid: false, estCostBps: 1.55 },
    { name: "Bombardier Inc. 7.875% 2027", shortCode: "BBD 7.875 27", isin: "USC10602AH02", cusip: "C10602AH0", direction: "Buy", faceValue: 1500000, mv: 1556250.00, accrued: 0.11, benchmark: "GV912810UG1", indQuantity: 1500000, sector: "Industrials", rating: "B+", modDuration: 1.2, ytw: 7.85, liquidityScore: 2.6, illiquid: true, estCostBps: 3.60 },
  ];

  window.BASKETS = [
    { id: "ig-credit", name: "IG Credit Basket — Program Trade", category: "Credit", status: "Ready", currency: "USD", asOf: "26 Jun 2026", constituents: igCreditRows, navSeries: navSeries(1, 30, 0.0009) },
    { id: "rates-hedge", name: "Rates Duration Hedge", category: "Rates", status: "Ready", currency: "USD", asOf: "26 Jun 2026", constituents: ratesHedgeRows, navSeries: navSeries(2, 30, 0.0004) },
    { id: "hy-opportunistic", name: "HY Opportunistic Basket", category: "Credit", status: "Review", currency: "USD", asOf: "24 Jun 2026", constituents: hyOpportunisticRows, navSeries: navSeries(3, 30, 0.0015) },
  ];
  window.summarizeBasketRows = summarizeBasketRows;
})();

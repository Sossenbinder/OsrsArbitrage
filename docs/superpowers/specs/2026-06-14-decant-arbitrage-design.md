# Decant (dose) Arbitrage — Design

## Context
The tracker currently finds single-item spread flips. OSRS potions exist in 1/2/3/4-dose
variants of the same family, and bots routinely dump low-dose variants (often at night) 20–30%
below the 4-dose on a per-dose basis. You can **decant** any doses up to 4-dose for free at Bob
Barter (GE, members-only), then sell the liquid 4-dose. This is a distinct, cross-item
opportunity the tool should surface.

## Verified mechanics
- **Decanting is free** at Bob Barter (members). Consolidating low→4-dose needs no extra vials → zero cost.
- **All dose variants share ONE 4-hour buy limit** (a connected limit, measured in units). Buying
  low doses does **not** multiply throughput.

## Goal
Surface, per potion family, the chance to **buy the cheapest-per-dose variant, decant up, and sell
the 4-dose** — with the same money-safety rigor as flips (no stale/loss/one-sided/garbage rows).

## The metric
For a family with 4-dose instant-buy `sell₄` (high) and 2% tax `tax₄`:

```
perDoseProfit = (sell₄ − tax₄) / 4  −  min over variants v of ( low_v / dose_v )
```

- `low_v` = variant v's instant-sell (low) — the price you place a buy offer at (depressed when dumped).
- The cheapest-per-dose variant is the **source**. Surface only when the source is **not** the
  4-dose and `perDoseProfit > 0`.
- **Per source unit** (dose s): profit = `(s/4)(sell₄ − tax₄) − low_s`.
- **Cycle profit** = `min(sharedBuyLimit, recentSourceVolume) × profitPerSourceUnit` — bounded by
  the shared limit and recent traded volume, like flips.

## Components

### Core (new, unit-tested)
- **Family grouping**: parse item names matching `^(.*)\((?<d>[1-4])\)$` → (baseName, dose). Group
  by baseName. A family qualifies if it has a tradeable `(4)` plus ≥1 tradeable lower dose. The
  "≥2 dose variants" requirement filters the rare non-potion `(n)` item.
- **`DecantOpportunity`** record: `FamilyName`, `TargetItemId` (the 4-dose), `TargetSell`, `Tax`,
  `KeepAfterTax` (= sell₄ − tax₄), `SourceItemId`, `SourceName`, `SourceDose`, `SourceBuy`,
  `PerDoseProfit`, `BuyLimit` (shared), `ExpectedCycleProfit`, `SourceVolume5m`, `TargetVolume5m`,
  `SafetyScore`, `SafetyBreakdown`, `PriceAgeSeconds`, `RankScore`.
- **`DecantDetector(TimeProvider)`**: given the family's variant `ItemSnapshot`s + settings,
  computes the opportunity or null. Validity gates (mirror `OpportunityDetector`):
  - source `low` and target `high` both present & positive
  - both fresh — older side ≤ `MaxAgeSeconds`
  - source buy-side volume ≥ gate; target sell-side volume ≥ gate (genuinely two-sided across the trade)
  - `perDoseProfit > 0`; reject implausible per-dose margins (glitch sanity)
  - skip when the cheapest-per-dose variant *is* the 4-dose
  - Reuses `GrandExchangeTax` and `SafetyComponents` (liquidity from source-buy + target-sell
    volume, volatility/freshness/depth of the 4-dose), weighted via `SafetyScorer`.

### Server
- Reuse `MarketState` (already holds every item's latest prices, 5m buckets, and mapping).
- A `FamilyIndex` (built once from the mapping, refreshed with it) maps baseName → variant item IDs.
- `DetectionPipeline` computes decant opportunities alongside flips each 60s poll.
- `DashboardSnapshot` gains a second list: `IReadOnlyList<DecantOpportunity> Decants`, pushed with the flips.

### Client
- A **`Flips | Decants` segmented toggle** at the top (persisted in localStorage with the other prefs).
- Decant table columns: Potion (4-dose icon + family, GE-source link), Source (`buy {dose}-dose @ {low}`),
  per-dose comparison (source/dose vs sell₄/4), Sell 4-dose (keep-after-tax), **Profit / dose**,
  Position (sized to bankroll + shared limit), Profit if filled, Vol/5m, Safety (meter + card), Age.
- Reuses the safety meter/breakdown card, tooltips, sizing, and the sort/filter bar (filters apply
  to the decant fields; sort: Best / Profit-per-dose / Safety / Volume).

## Data flow
`/latest` poll → `MarketState` → `DetectionPipeline` (flips + decants) → `DashboardSnapshot`
(`Opportunities` + `Decants`) → SignalR push → client renders the active mode.

## Testing (TDD)
- Name parsing: `Prayer potion(2)` → ("Prayer potion", 2); non-matching names ignored.
- Family qualification: needs a (4) + a lower dose.
- Cheapest-per-dose selection across variants.
- Profit math: per-dose, per-source-unit, per-cycle, with tax on the 4-dose sale.
- Gate rejections: stale side, one-sided volume, source == 4-dose, perDoseProfit ≤ 0, glitch margin.
- `FakeTimeProvider` for freshness, mirroring `OpportunityDetectorTests`.

## Scope / non-goals (YAGNI)
- v1: **buy low dose → sell 4-dose only**. No down-decanting (buy 4 → sell low).
- Family detection by name heuristic (no curated potion list).
- Members assumed (already are). No separate decant-specific safety re-weighting beyond reusing components.

## Open questions
- None blocking. The name heuristic may occasionally group a non-potion `(n)` family; the
  "≥2 dose variants with a (4)" rule plus the volume/validity gates make a false family harmless
  (it just won't produce a profitable, liquid decant row). Revisit only if junk appears.

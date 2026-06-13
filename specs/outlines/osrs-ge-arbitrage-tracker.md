# Outline: OSRS Grand Exchange Arbitrage Tracker

## Context

Greenfield project. Goal: a personal ASP.NET Core + Blazor Server application that ingests the OSRS Wiki real-time pricing API, detects same-day flipping opportunities on liquid mid-tier items, scores their safety, and surfaces them in a live-updating UI. Optimized for *predictable* spread capture (instant-buy / instant-sell), not speculative long holds.

**Framing — this is risk-controlled flipping, not risk-free arbitrage.** The API's `high`/`low` come from *completed* RuneLite trades, not from a live order book we can fill against. Placing a buy offer near `low` queues us against every other trader (including users of the same public API), and the two legs (buy, then sell) are never simultaneous — there is always holding-time exposure where the margin can close. The "safety score" is therefore explicitly an estimate of the **probability of buying AND selling cleanly within the window**, not a guarantee that the displayed margin is captured.

## Scope

**In scope:**

- Polling the OSRS Wiki real-time prices API (`/latest`, `/5m`, `/1h`, `/mapping`) with proper `User-Agent` and respectful cadence.
- Arbitrage detection: tax-adjusted margin between `low` (instant-sell, your buy price) and `high` (instant-buy, your sell price), filtered by user-tunable thresholds.
- Buy-limit-aware profit projection: expected gp per 4-hour cycle = `min(limit, recentDemand) × marginPerUnit`.
- Safety scoring (interpreted as clean-fill-and-exit probability) combining four signals: two-sided volume, short-term volatility, spread persistence, price freshness.
- Bankroll-aware position sizing: user enters total capital; tool sizes each pick to its buy limit and the bankroll, and flags when the top picks exceed the 8 available GE slots.
- Real-time UI (Blazor Server + SignalR) showing a ranked, filterable list of current opportunities with drill-down to per-item history and score breakdown.
- Threshold notifications: desktop/browser push when a pick crosses a user-set safety + profit threshold.
- A scoring-validation feedback loop: since we can never observe our own fills, persist a forward-looking proxy outcome (did a profitable spread + two-sided volume persist over the following N buckets?) and compare it to the score at detection time.
- SQLite persistence of price snapshots and detected opportunities for trailing-window features and post-hoc review.

**Out of scope:**

- Speculative / multi-day "investment" bets on big-ticket items.
- Automated trading or any RuneLite plugin/in-game integration. The tool only *informs* — the user executes manually.
- Real-time tracking of which GE slots are actually occupied (we model the 8-slot *limit* for sizing/flagging, but the user tells us nothing about live account state).
- Multi-user / authentication. Single-user local app.
- F2P-only filtering or Deadman / leagues variants (the `/dmm` endpoint is ignored).

## Approach

### Solution layout

```
ArbitrageTracker.sln
├── src/
│   ├── ArbitrageTracker.Core/         # Domain, scoring, detection — no I/O
│   ├── ArbitrageTracker.Data/         # EF Core + SQLite, snapshot persistence
│   ├── ArbitrageTracker.Ingestion/    # HttpClient + IHostedService pollers
│   └── ArbitrageTracker.Web/          # ASP.NET Core host + Blazor Server UI + SignalR hub
└── tests/
    └── ArbitrageTracker.Core.Tests/
```

### Building blocks

1. **Ingestion layer (`ArbitrageTracker.Ingestion`)**
   - Typed `HttpClient` against `https://prices.runescape.wiki/api/v1/osrs` with mandatory descriptive `User-Agent` (includes contact email).
   - Three `BackgroundService` pollers on independent cadences: `LatestPoller` (60s), `FiveMinPoller` (5min), `HourlyPoller` (1h). One-shot `MappingLoader` on startup + daily refresh (buy limits, alch values, members flag).
   - Each poll writes a snapshot row + publishes a `PriceUpdate` event onto an in-process channel (`System.Threading.Channels`).

2. **Rolling state (`ArbitrageTracker.Core`)**
   - In-memory `MarketState` keyed by `itemId`: latest `low`/`high` + timestamps, last N `/5m` buckets (e.g. last 24 = 2h), last 24 `/1h` buckets.
   - Mapping table (id → name, buy limit, alch, members) loaded once.

3. **Detection + scoring (`ArbitrageTracker.Core`)**
   - Consumes the channel; on each `/latest` update, recomputes opportunity for affected items.
   - **Filters (gating):** `low > 0 && high > 0`; unit price ≤ configured cap (default 1M); margin after tax ≥ configured floor; expected cycle profit ≥ 50k; both-side recent volume non-zero; both prices fresh (< 30min since last trade).
   - **Profit math:** `tax = min(floor(0.02 × high), 5_000_000)` (skip for tax-exempt list); `netMargin = high − low − tax`; `expectedCycleProfit = min(item.buyLimit, recent5mLowVolume) × netMargin`.
   - **Safety score (0–100, weighted) — read as P(clean buy + clean sell within window):**
     - Two-sided liquidity (40%): geometric mean of `lowPriceVolume` and `highPriceVolume` over last hour, log-scaled. Geometric (not arithmetic) so a dead side tanks the score — that's the get-stuck-holding-inventory risk.
     - Short-term volatility (25%): inverse of stddev of avg prices over last 6h, normalized by mean.
     - Spread persistence (20%): fraction of last K `/5m` buckets where a profitable spread existed — filters one-off prints.
     - Freshness (15%): decay function of `now − max(lowTime, highTime)`.
   - Final ranking: `expectedCycleProfit × (safety / 100)`.

5. **Position sizing (`ArbitrageTracker.Core`)**
   - User sets total bankroll in settings. Per pick: `suggestedQty = min(buyLimit, floor(perSlotBudget / low))`, where `perSlotBudget` defaults to `bankroll / 8` (tunable concentration).
   - Surface `capitalNeeded = suggestedQty × low` and flag when the cumulative capital of the top-ranked picks exceeds the bankroll or when more than 8 picks clear the user's thresholds (the slot ceiling).

6. **Scoring-validation feedback loop (`ArbitrageTracker.Core` + `ArbitrageTracker.Data`)**
   - We never see our own fills, so we validate against a forward proxy: for each detected opportunity, a deferred job (N buckets later, e.g. 12 × `/5m` = 1h) records whether a profitable spread persisted and whether both-side volume continued.
   - Store `(scoreAtDetection, proxyOutcome)` on `OpportunitySnapshot`; a small calibration view plots score vs proxy-success rate so the weights can be tuned empirically rather than guessed. This is the only honest signal of whether the score predicts anything.

4. **Persistence (`ArbitrageTracker.Data`)**
   - SQLite via EF Core. Tables: `PriceSnapshot` (itemId, ts, low, high, lowTime, highTime), `BucketSnapshot5m` and `BucketSnapshot1h` (itemId, ts, avgHigh, avgLow, volHigh, volLow), `OpportunitySnapshot` (computed rows for audit/back-test), `ItemMapping`.
   - Retention: prune `PriceSnapshot` > 7d, buckets > 30d. Cheap on a personal-scale dataset.

5. **Web + UI (`ArbitrageTracker.Web`)**
   - Blazor Server with a single primary page: sortable/filterable opportunities grid (item, margin, expected cycle profit, safety, both-side volume, age).
   - `OpportunitiesHub` (SignalR) pushes recomputed rows on each detection cycle; Blazor components subscribe.
   - Detail drawer per item: sparkline of last 24h `/5m` average, score breakdown, current buy limit, link to OSRS Wiki.
   - Settings panel (server-side persisted in SQLite): capital cap, min margin %, min cycle profit, safety threshold, item allow/deny list.

### Data flow

```
Pollers ──► PriceUpdate channel ──► DetectionService ──► (SQLite write + SignalR push)
                  ▲                       │
                  └── MarketState ◄───────┘
```

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Frontend | Blazor Server | Single .NET process, native SignalR integration, fastest to ship for a personal real-time dashboard. |
| Persistence | SQLite + EF Core, with in-memory hot state | Zero ops, enough for trailing features and audit; in-memory keeps the detection hot path off the DB. |
| Tax model | 2% sell tax capped at 5,000,000 gp/item; skip if sell price < 50 gp; honour exempt-item list | Matches current GE rules (raised 1% → 2% on 2025-05-29). Cap math materially changes attractiveness above ~250M, irrelevant under our 1M unit cap. |
| Default unit-price cap | 1,000,000 gp/unit (UI-tunable) | User's explicit ceiling — bounds capital exposure and excludes the speculative tier. |
| Default min cycle profit | 50,000 gp per 4h buy-limit window (UI-tunable) | Excludes the "1 gp × 1500 limit" case the user explicitly called out as not worth it. |
| Default max time-to-fill | ~12h on each side, derived from recent volume vs buy limit | Keeps every opportunity comfortably inside the "same-day" rule. |
| Account model | Assume members, 8 GE slots | Standard flipping setup; F2P-only is out of scope. |
| Poll cadence | `/latest` 60s, `/5m` 5min, `/1h` 1h, `/mapping` 24h | Within API etiquette; matches the upstream update frequencies — polling faster yields no new data. |
| Safety score | Weighted blend: liquidity 40% / volatility 25% / persistence 20% / freshness 15%, interpreted as clean-fill probability | Each captures a distinct failure mode of a flip; weights bias toward "can I exit cleanly?" which is the dominant risk. Geometric mean on the two volumes so a one-sided market scores low. |
| Position sizing | Configurable bankroll, default `bankroll / 8` per slot; suggest qty, flag slot/capital overflow | The 8-slot limit and total capital determine which ranked picks are actually actionable; without it the list is theoretical. |
| Alerting | Live dashboard + threshold push notifications | You can't react to a flip you're not watching; threshold pings make it usable without staring at the grid. |
| Scoring validation | Forward proxy outcome (spread + volume persistence over next ~1h) stored against score | We can never observe our own fills; this is the only way to know if the score predicts anything. |
| User-Agent | `ArbitrageTracker/<version> (contact: <email>)` from config | Wiki blocks default agents; descriptive UA + contact is the explicit etiquette. |
| Transport | SignalR hub, push diffs only | Avoids re-sending the full opportunity list on every tick. |
| No execution / no in-game integration | Display-only | Eliminates ToS risk entirely; you remain the actor. |

## Trade-offs

- **Optimizing for:** predictable, low-variance same-day gains with capital-efficient sizing. Safety is weighted higher than headline margin so the top of the list is "boring and reliable" by design.
- **Accepting:** misses big speculative wins (by design); slightly stale data within each 60s window (acceptable given GE liquidity at our target tier); dependence on a single data source (RuneLite-fed Wiki API) — if it degrades, the tool degrades with it.
- **Accepting (fill risk):** the displayed margin is never guaranteed — same-source competition and non-simultaneous legs mean some surfaced opportunities won't fill at the modeled prices. We mitigate via the safety score and validate via the proxy loop, but we cannot eliminate it without observing real fills.
- **Complexity vs polish:** Blazor Server keeps the stack tight at the cost of being tied to a persistent connection — fine for a single-user local app, would be the first thing to revisit if this ever became multi-user.
- **In-memory vs DB hot path:** keeping detection in memory means a restart loses trailing windows for ~1h until buckets rebuild — acceptable trade for zero DB pressure on every tick.

## Open Questions

_None that must block plan time._ Items deferred to implementation, with default behaviour chosen:

- **Tax-exempt item list source:** the OSRS Wiki "Category:Items exempt from Grand Exchange tax" page is scrapeable but not part of the JSON API. Default: hand-curated seed list (bonds + common tools) in config, refresh manually. Revisit only if exemptions start materially affecting picks.
- **Exact volatility window (1h vs 6h):** default to 6h since it's more stable; tune empirically once real data is flowing.

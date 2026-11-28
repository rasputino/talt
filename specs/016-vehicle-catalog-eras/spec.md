# Vehicle catalog and eras

**Feature branch** `016-vehicle-catalog-eras`
**Created** 2026-11-28
**Status** Clarified

## Clarifications

### Session 2026-11-28

- Which rail models count toward the minimum of twelve? Count locomotives and self-propelled railcars. Wagons are additional catalog entries.
- What happens when renewal is due for a retired model and the player has chosen no successor? Keep the vehicle in service and notify the player once. After the player selects a successor, apply the replacement on the next depot visit.

- When does rail electrification unlock? In 1960, together with the Aurora, the first electric locomotive.
- When does a chosen replacement take effect? Each rule defaults to the next depot visit regardless of age. The player can instead defer that rule until each source vehicle reaches the end of its useful life.

- Can wagons be replaced by model? Yes. Replace matching parts in place and preserve consist order, the locomotive and carried cargo.

## User scenarios and testing

### US1 — Choose a model on sale (P1)

The depot catalog lets a player plan a purchase using the current year, cargo needs and budget.

1. Open the catalog in a model's first and last sale years. The model can be bought in both years.
2. Advance past its last sale year. New purchases cease and examples already in service retain their identity and orders.
3. Cross an introduction date. The player receives one localized introduction per new model. Resuming immediately after the introduction must not announce it again.

### US2 — Compare purchase specifications (P1)

Select a road vehicle, locomotive or wagon before buying. The purchase window shows its speed, capacity, supported cargo, price, yearly running cost, reliability and lifespan. Inapplicable attributes are clearly identified. Compare the displayed figures with the purchased vehicle.

### US3 — Renew and replace at depots (P2)

A player can enable renewal at end of life and choose a newer model as the replacement for an older one. Observe an eligible vehicle entering a depot with each setting enabled independently. A successful exchange retains its assigned service. An unaffordable or incompatible exchange leaves the vehicle, cargo, orders and company funds intact.

Set a replacement rule for two vehicles of the same source model, one young and one at the end of its useful life. With default timing, each is replaced on its next eligible depot visit. With deferred timing, only the older vehicle is replaced on that visit. The younger vehicle continues in service until its first eligible visit at or after the end of its useful life.

When a vehicle due for renewal reaches a depot after its model has retired, and no replacement rule applies, it continues its service with no purchase. The player receives one notice for that vehicle. Subsequent depot visits and save/load cycles do not repeat the notice. After the player chooses a compatible successor, the depot applies the rule on the next visit if funds permit.

For trains, each wagon and locomotive is eligible by its own model and purchase age. A wagon exchange preserves part order, the locomotive, freight destinations and line assignment. A train-level transaction validates the resulting length, capacity and net price before applying any exchange.

### US4 — Build infrastructure appropriate to the year (P2)

Inspect road types, bridge span limits and electrification immediately before and at an unlock year. Construction becomes possible at the same date the interface advertises. Existing infrastructure remains usable.

On the last day of 1959, both building electrified track and converting existing track to electric are unavailable. On the first day of 1960, both operations unlock and the Aurora goes on sale.

### US5 — Preview the selected vehicle (P3)

Select two different models in a purchase window. Each selection displays the corresponding voxel model without buying anything or changing company funds.

## Requirements

- FR-001. Define all model identities, original names, sale windows, statistics, visual and sound references in validated versioned content. Preserve existing road and rail model IDs.
- FR-002. Use the simulation calendar for purchases, introductions and infrastructure availability. Enforce sale windows for purchases initiated through commands as well as the purchase window.
- FR-003. Show the US2 specifications and US5 preview before confirming a purchase. Provide English and Spanish text.
- FR-004. Allow opt-in depot renewal and source-to-target replacement rules. Each replacement rule has a player-selectable timing setting, defaulting to the next depot visit with an option to wait until end of life. Save and replay must preserve the selected timing and its economic effects.
- FR-005. Define road and rail infrastructure unlocks and span limits in content, with the same rules for previews and construction.
- FR-006. Agree with feature 014 on stable model references, renewal settings and restoration of calendar-dependent state. Reordering catalog entries must preserve vehicle identity in saved games.
- FR-007. Keep line management and statistics in feature 017.

## Key entities

A model has a permanent ID independent of its display name, attributes and years of sale. An owned vehicle refers to its model after retirement. A replacement rule identifies the models involved. Infrastructure availability links construction options to calendar years.

## Success criteria

- SC-001. The catalog spans 1950–2050 inclusive and contains at least eight original road models and at least twelve original powered rail models (locomotives and self-propelled railcars combined), plus wagons.
- SC-002. A player can inspect all seven purchase attributes for every model and recognize the selected vehicle in the preview.
- SC-003. Replaying the same purchases, depot visits and calendar transitions yields identical fleet, company funds and availability.
- SC-004. Catalog and era processing stays within the project tick and frame budgets with zero steady-state hot-path allocations.

## Assumptions

The existing 1850 start and starter services remain playable.

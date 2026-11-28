# Persistence and replay contract for feature 014

Save model references by catalog kind and permanent string ID for road vehicles, rail parts and replacement rules. Resolve IDs when loading and report missing models before installing a session. Keep retired definitions available for lookup. Serialize company renewal settings, rule timing, purchase dates and per-unit renewal notice flags. Earlier saves default to disabled renewal, empty rules and clear flags. Derive construction availability and sale windows from the restored simulation date; emit introductions only when the calendar advances into a new year.

## Runtime bindings

Use each model's Id from RoadVehicleSystem.ModelAt(slot) or TrainSystem.ModelOf(part). The two catalogs resolve those IDs with FindModel. A company exposes RoadReplacements.Rules and RailReplacements.Rules in ordinal source-ID order. Each ReplacementRule carries Target, EndOfLifeOnly and Renew. An empty target with Renew enabled requests same-model renewal. A removed rule has no saved entry.

Restore valid historical rules even when the target has since retired. User-command validation checks the year of a new order and is unsuitable for restoring an existing rule. Within Core, restore each validated rule using ReplacementPolicy.Set. Save RoadVehicleBits.RenewalNotified and TrainPart.RenewalNotified with the corresponding unit. TrainPart.PurchaseDay is per part and independent of train age. Preserve the rail part order and existing freight metadata during restoration. Versioned migration must resolve legacy numeric indices using the original version's model order; writing new saves always uses the stable string ID.

## Acceptance fixture for the merged features

Save on 31 December 1959 with same-model renewal enabled and a deferred rule. Restore twice and cross midnight. Both runs must announce Aurora once, permit electrification on 1 January 1960 and produce the same fleet and balances after visiting the depot. Repeat with two matching wagons of different ages and with a retired unit already notified. Reordering model definitions must still resolve the same IDs.

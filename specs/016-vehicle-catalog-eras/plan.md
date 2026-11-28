# Implementation plan

Extend the existing road and rolling-stock catalogs. Keep model lookup and purchase validation in Core. The two depot windows share the presentation of lifespan, cargo, preview and company replacement settings. The shared rail planner enforces construction dates for both previews and commands.

## Constitution check

Core owns dates, rules and finance. Client owns localized presentation. Use integer ages, stable model IDs, replayable commands, allocation tests and a catalog benchmark. Existing original assets and packages suffice. Validate the non-GPU suite and the fingerprint gate. Record measured performance and product decisions. No new dependency.

## Analysis against requirements

FR-001 and FR-002 map to T002 and T005. FR-003 maps to T003 and T006. FR-004 maps to T004, including per-part timing, failed exchanges and deterministic command serialization. FR-005 shares the planner gate tested by T005. FR-006 maps to the model and renewal persistence contract in T001, with the combined save-file round trip dependent on feature 014. FR-007 excludes line management from the work packages. T007 and T008 cover the performance, determinism and manual acceptance criteria.

## Delivery sequence

First verify model sale boundaries and content coverage. Extend rail year checks in the planner. Apply replacement during depot service using existing inventory and finance operations. Add presentation and verify the two modes with the client. Save/load integration uses the contract in contracts/persistence.md.

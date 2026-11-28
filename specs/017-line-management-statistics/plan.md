# Implementation plan — Line management and statistics

Branch `017-line-management-statistics`. See [specification](spec.md).

## Design

Extend the existing C#/.NET 10 simulation and retained UI on all three supported platforms. Use shared LineRegistry identities. Record operating and transport events at their existing points of application. Accumulate actual fixed-point movement outside the parallel shared state and merge deterministically. Close dated periods in game-calendar order. Keep player views derived and read-only. The line editor owns interval configuration. The fleet directory locates vehicles by mode and stable ID.

## Constitution check

Core owns integral simulation state, replay commands and deterministic hashes. Client owns presentation, filtering, sorting and map projection. Use existing dependencies and original assets. Localize English and Spanish together. Test Core before or with code and register new state with the reflection guard. Measure collection cost and per-frame/tick allocations. Verify sequential/parallel equality, non-GPU suite, golden fingerprints, and demo on the client. The section 5 budgets and 10% reference regression limit apply.

## Source structure

- Vehicles and Rails attribute operating data and enforce first-stop intervals.
- Finance, Stations and Towns own their histories to preserve calendar and identity semantics.
- Client/Screens owns management, charts and overlay controls. Ui/Widgets supplies reusable virtual rows.
- Tests and benchmarks exercise ordinary entry points; 014 persistence fields are listed in the contract.

## Delivery order

US1 and US2 establish network and fleet directories. US3 extends dispatch. US4 and US5 can proceed independently once ownership and event boundaries are defined. Integrate, measure, verify and demonstrate after each runnable slice.

## Analysis before implementation

US1 covers FR-001/002, US2 covers FR-003, US3 covers FR-004, US4 covers FR-005, and US5 covers FR-006. All periodic state and current accumulators map to FR-007. The directory scale check, month/year boundary tests, parallel replay and benchmark tasks cover SC-001 through SC-004. The heatmap uses completed passages, whereas the line overlay predicts traversable legs. Each has its own test oracle. No new runtime dependencies.

## Monthly snapshot cost

The station month-end benchmark measures additional required work against the old counter rotation. Archive group membership and five parallel cargo columns once per month. The 5,000-station benchmark increased from 35 µs to 333.2 µs with zero managed allocation. This justified increase of 0.298 ms once every 28–31 game days remains inside the 2 ms station budget. Copying individual station records was measured and rejected because it took over 5 ms.
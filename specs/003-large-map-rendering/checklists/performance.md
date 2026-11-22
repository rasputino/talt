# Performance Requirements Checklist: Large-Map Rendering

**Purpose**: Check that the performance requirements are complete, measurable and consistent
**Created**: 2026-11-22
**Feature**: [spec.md](../spec.md)

- [x] CHK001 Is the frame-rate target quantified for every zoom level, including the whole-map view? [Clarity, SC-001, FR-015]
- [x] CHK002 Is "no stutter" quantified as a worst-frame limit (33 ms) and a measurement window? [Measurability, SC-001]
- [x] CHK003 Is the reference hardware and scenario defined for the measurements? [Assumptions]
- [x] CHK004 Is the memory budget stated with what it covers (world drawing on 4096)? [Clarity, FR-011]
- [x] CHK005 Is the edit latency defined from the change to the first frame showing it? [Clarity, SC-002]
- [x] CHK006 Are bursts of edits covered without long frames? [Coverage, Edge Cases, FR-008]
- [x] CHK007 Is the interaction with feature 001's fast-forward budget stated? [Consistency, SC-006]
- [x] CHK008 Is the startup time bounded and the fill-in behaviour specified? [Coverage, FR-012, SC-007]
- [x] CHK009 Are smaller maps required not to regress? [Coverage, SC-004]
- [x] CHK010 Is there a way to observe the numbers (statistics) without external tools? [Measurability, FR-010]
- [x] CHK011 Is a benchmark required for the meshing hot path, per the constitution? [Plan, research R10]
- [x] CHK012 Is the per-frame allocation rule for the draw path stated? [Plan, Constraints]

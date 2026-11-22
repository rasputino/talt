# Rendering Correctness Checklist: Large-Map Rendering

**Purpose**: Check that the visual-correctness requirements are complete and testable
**Created**: 2026-11-22
**Feature**: [spec.md](../spec.md)

- [x] CHK001 Is "looks the same" defined in testable terms (same visible faces, colours, AO, fog)? [Measurability, FR-001, FR-002, SC-005]
- [x] CHK002 Is the treatment of the world's bottom and map edges specified? [Coverage, FR-006]
- [x] CHK003 Are cracks between detail levels explicitly forbidden? [Coverage, FR-005]
- [x] CHK004 Is the allowed simplification error quantified? [Clarity, FR-004]
- [x] CHK005 Are roads required to stay visible and continuous at every level of detail? [Coverage, FR-004, Clarifications]
- [x] CHK006 Is correctness of edits on area borders specified? [Coverage, US2 scenario 2]
- [x] CHK007 Is the behaviour for edits in off-screen or simplified areas specified? [Edge Cases]
- [x] CHK008 Is what is shown while areas are not ready yet specified? [Coverage, FR-012, research R5 fallback]
- [x] CHK009 Is fog behaviour at the farthest zoom specified? [Consistency, FR-015]
- [x] CHK010 Is popping of detail changes addressed? [Coverage, US3 scenario 3]
- [x] CHK011 Are visual upgrades explicitly out of scope? [Scope, Assumptions]

# Tasks — Line management and statistics

- [X] T001 Specify and clarify the five stories and align docs/VISION.md.
- [X] T002 Resolve design, calendar boundaries, storage and the 014 contract.
- [X] T003 [US1] Test and implement attributed line accounts and actual load-distance for both modes.
- [X] T004 [US1] Provide a sortable, filterable virtual line directory in Client/Screens.
- [X] T005 [US2] Implement a virtual fleet directory with independent filters, stable activation and map navigation.
- [X] T006 [US3] Test, serialize and expose independent modal dispatch clocks in the line editor.
- [X] T007 [P] [US4] Test and implement dated histories in Finance, Stations and Towns and their charts.
- [X] T008 [P] [US5] Test and implement segment passage counts, routed line overlays, catchments and heatmap.
- [X] T009 Localize all user-visible controls and verify key parity.
- [X] T010 Update the reflection guard, allocation tests, benchmarks and architecture measurements.
- [X] T011 Build, run the non-GPU suite, replay fingerprints and assess regressions.
- [X] T012 Launch the client and verify the demonstration with Pablo.
- [X] Meet the 4 ms average full road tick budget at 50,000 buses. The final official benchmark measured 3.921 ms after caching directed lane geometry and successor indices. The six long-window checks produced the same simulation fingerprint.

US1 and US2 share the viewport widget. US3 depends on the shared line identity. US4 and US5 use separate reporting entities and can proceed in parallel with directory work. Each story has an independent acceptance scenario in spec.md.

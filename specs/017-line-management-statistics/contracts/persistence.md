# Save/load contract for feature 014

Restore authoritative values before the next tick. Identify entities by stable IDs and segments by their two endpoints and mode. Retain the game calendar of every bucket, whether it has a sample, the current period and chronological order. A historical zero is valid.

- For each stable line ID persist the current and previous calendar year's operating profit, passenger and freight boardings, occupied unit-distance and offered capacity-distance, plus whether the previous year exists. Road vehicles accumulate actual fixed-point service distance until their load, line or year changes. A save may serialize the posted line totals and each vehicle's unposted distance keyed by stable vehicle ID. Equivalently, save the full current line totals including the open journeys and restore vehicles with zero unposted distance. Never restore both full totals and nonzero distances. Fingerprints use the full totals in either representation. The read cache can be rebuilt.
- Persist a line's target interval in game days (zero disables control), its last dispatch tick for road and rail independently (−1 means none), and both modes' existing first-stop/loading state. A dispatch is the release from first-stop dwell once the vehicle is ready. Road occupancy, signal and track reservations still determine physical movement. Disabling control clears both clocks.
- Restore the company ledger and aligned delivery and valuation series for 24 months and all years including open periods. The open period resumes accumulating.
- Store twelve closed observations per station/cargo and town, the period ordinals and validity since founding, and all open counters. A station snapshot retains waiting, picked up, delivered, served status and rating for its historical group. Preserve the historical membership or expand each observation by stable station ID when writing the save. Town snapshots store generated and transported passengers and mail plus rating.
- Store current and last complete month traffic, including the date of a complete month with zero traffic. Direction-independent segment keys distinguish road and rail. Materialize the canonical counts from the directed road lanes (including the endpoint valid for that period) and sparse passages before saving. Restore the two sparse maps and restart per-lane counters at zero.
- Rebuild fleet indices, route previews, catchment shapes, heat colours and localized text from loaded data. Older saves lacking samples start with unavailable history and ordinary unregulated dispatch.

Acceptance for 014: reload mid-month, during first-stop hold and just before a year boundary. The subsequent departures, charts, traffic counts and simulation fingerprint must match uninterrupted play.

## Replay command

The setLineInterval command has integer fields companyId, lineId and days. The company must own the line. Days must be nonnegative; zero clears both modal clocks. A saved game retains the resulting clocks and existing vehicle dwell state independently of the command recorder.

# Design research

| Decision | Rationale | Alternative considered |
| --- | --- | --- |
| Attribute money and boardings where the simulation applies them | Preserves transfer-leg credit and survives reassignment or sale | Summing current fleet profits rewrites history |
| Accumulate actual fixed-point movement | Captures partial traversals, changing loads and train capacities | Departure samples ignore leg length |
| Use stable vehicle identities and a bounded row pool | Opening and scrolling costs do not scale in widget count | Existing ListView creates a button for every record |
| Separate road and rail departure clocks | Mixed lines share LineRegistry while each mode regulates its own queue | One clock would hold buses behind trains |
| Follow the ledger calendar and rotate station and town samples at their native month-end boundaries | The incoming date is day one of the following month | Polling the HUD can miss boundaries during fast forward |
| Count passages at the actual road and train head crossings | Dwell time and consist length cannot inflate usage | Sampled positions measure occupancy |
| Query infrastructure with an independent presentation route search | Displaying a route must preserve simulation search budgets and fingerprints | Straight connections conceal disconnected legs |

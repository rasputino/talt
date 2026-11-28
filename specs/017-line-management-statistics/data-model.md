# Statistics and dispatch state

| Owner / identity | Values | Lifetime |
| --- | --- | --- |
| Company and calendar period | Income, expenses, final delivery units by cargo, company value | 24 months including open month and every game year |
| Line ID and year | Operating profit, boardings by passenger/freight, occupied and offered unit-distance | Current and previous year |
| Road vehicle ID | Unposted fixed-point distance travelled in service with the same load and line | Posted before a load change, reassignment, sale or year end. Read queries include the open distance. |
| Line ID | Optional target interval and last departure tick independently for road and rail | Life of line |
| Station ID, cargo and month | Waiting stock, pickups, final deliveries and rating | Last 12 closed months |
| Town ID and month | Passenger/mail generated and transported, town rating | Last 12 closed months |
| Physical segment, mode and calendar month | Vehicle passage count | Open and last closed month |
| Fleet row | Mode and stable ID, operating state, independent age/profit flags | Derived from active vehicles |

Aggregate integer numerators and denominators before dividing. Missing observations remain distinguishable from zero. The load factor has no value until offered capacity-distance is positive. A financial flow is summed across periods while a valuation is a closing stock.

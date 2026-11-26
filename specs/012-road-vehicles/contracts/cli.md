# Contract: command-line options

Client and headless:

- `--seed-vehicles <n>`: after `--seed-stations`, submits recorded commands that turn on infinite money, build depots,
  create lines between seeded stations and buy n vehicles (debug, performance).
- `--check-overlaps`: runs `CheckInvariants` after every tick; on failure prints the tick and the message and exits
  with code 1 (headless) or pauses the game with a message (client).
- Headless only: `--road-grid <spacing>` lays the debug road grid (not a command: such runs do not replay).

Headless summary adds: `road-vehicles`, `lines`, `depots`, `vehicle-deliveries`, `vehicle-tiles` (distance driven),
`route-searches`, `vehicle-states` (count per state) and `second-half-avg-tick-ms` (the average tick of the run's second
half, once seeded vehicles have left their depots).

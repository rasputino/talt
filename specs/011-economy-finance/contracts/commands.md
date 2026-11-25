# Contract: commands

## changeLoan

```json
{ "type": "changeLoan", "companyId": 0, "mode": "borrow" }
```

`mode`: `borrow` (one step), `repay` (one step, or the remaining loan when smaller), `borrowMax` (as many
steps as fit under the maximum), `repayMax` (as many steps as cash covers, ending with the remainder when
it is smaller than a step).

Reason codes follow `ConstructionReasons` (kebab case; client text `construction.reason.<code>`).

| Reason | When |
|---|---|
| `no-map` | game without a map (no finance) |
| `company-not-found` | unknown company |
| `max-loan` | borrow with loan ≥ maximum |
| `no-loan` | repay with loan 0 |
| `not-enough-cash` | repay with cash below the amount to repay (or below 1 step for `repayMax`) |
| `game-over` | any command after game over (checked by the simulation for every command) |

Effect: cash and loan change by the same amount; nothing is booked in the ledger.

## enableInfiniteMoney (research R17)

```json
{ "type": "enableInfiniteMoney", "companyId": 0 }
```

Turns infinite money on for the company, for the rest of the game (no command turns it off). Reasons:
`no-map`, `company-not-found`, `already-infinite`. While on, `changeLoan` is refused with `infinite-money` and
construction never with `not-enough-money`.

## Construction commands (006/007/010)

Unchanged JSON. The cost checked and paid is the planned cost × cost level (inflation), booked in
`construction`; undo refunds book a positive `construction` amount.

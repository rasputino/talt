# Contract: command-line options

Client (`Talt.Client`) and headless (`tools/Talt.Headless run`):

| Option | Effect |
|---|---|
| `--sandbox` | `FinanceSettings.Sandbox = true` (no bankruptcy) |
| `--inflation` | `FinanceSettings.Inflation = true` |
| `--infinite-money` | `FinanceSettings.InfiniteMoney = true` (from the start; cannot be turned off) |
| `--station-service-pays` | `FinanceSettings.DebugServicePays = true` (needs `--station-service <n>` to collect anything) |

Headless `run` prints, before the fingerprint, for games with a map (same `key: value` style as the
other summary lines):

```
cash: 512340
loan: 0/500000
company-value: 530120
income: 21300
expenses: -9000
deliveries-paid: 37
bankrupt-months: 0
```

(invariant culture, internal units). The client's new-game screen has three toggles: sandbox, inflation
and infinite money.

# Fuel engine (`SimRacing/FuelEngine`)

The fuel estimation engine behind the iRacing Overlay's fuel figures (Sim Racing Tools → iRacing Overlay: the Fuel widget, the Fuel tab, the fuel alerts). Part of the app (namespace `GoodieBag.SimRacing.FuelEngine`) but self-contained: no UI, no iRacing SDK, no other Goodie Bag types; keep it that way. The adapter (`SimRacing/Race/FuelModel.cs`) turns telemetry frames into its inputs; `FuelPlanner` (`Fuel.cs`) turns its outputs into the plan the overlay shows.

## Parts

- `FuelLapTracker` — completed-lap fuel samples from telemetry frames. Starts over on a new car / track / session key, a lap-counter jump or reset, a stale gap (> 3 s) or an abrupt fuel drop; flags pit-road, refuel, caution, formation and incident laps instead of using them; ignores a repeated frame.
- `FuelConsumptionEstimator` — the last N eligible laps: median / MAD outlier rejection (from 5 laps), exponential recency weights (half-life 6 laps), weighted mean and spread, effective sample size, a Theil–Sen trend (shown, not extrapolated), a one-sided per-lap prediction bound, and a reliability level (Unavailable / Learning / Low / Moderate / High).
- `FuelSectorSampler` + `FuelSectorProfileEstimator` — the fuel level at fixed points round the lap (interpolated between frames, across the line too) on clean laps, and from them the fuel the rest of the current lap needs. No estimate until every point ahead has 3 clean laps.
- `FuelStrategyCalculator` — expected fuel to the finish, a statistical reserve (lap-to-lap variance + the mean's uncertainty, at the safety mode's level, at least 1 / 1.5 / 2 % of the expected burn for drift over a stint), the driver's own reserve, projected fuel at the finish, shortfall and the add rounded up to the pit increment (capped by the tank).
- `RaceDistanceEstimator` — lap-limited distance; timed races: the leader's checkered flag at their first crossing once the clock has run out, then the player's first crossing after it (a forecast until iRacing's white / checkered flags settle it).

Inputs and outputs are litres. iRacing's `FuelLevel` is litres or kWh depending on the car: the adapter refuses a kWh car rather than feeding kWh in as litres.

## Tests

- `tests/GoodieBag.Tests` (`FuelEngineTests`) — this engine, one case per row. Run by `devtools\test.cmd`.
- `tests/GoodieBag.Tests` (`RaceTelemetryTests`) — the adapter and the planner.
- `devtools\fuel-replay.cmd [folder]` — replays recorded iRacing sessions (`.ibt`, default `Documents\iRacing\telemetry`) through the app's decoder, the adapter and this engine, and scores every prediction against laps it hadn't seen (`devtools\shots\fuel-replay.txt`).

## Validation so far (2026-10-10)

45 recorded sessions on one PC (mostly the Porsche 911 Cup (992) at Watkins Glen, some Mazda MX-5 at Spa, one GT3 lap at the Nordschleife): 192 clean laps.

| Check | Result |
|---|---|
| Next lap's fuel (165 laps) | mean absolute error 0.020 L (1.0 %), bias +0.0004 L; within the per-lap conservative bound 93 % (model target 95 %) |
| 5-lap stints (77) / 10-lap stints (38) | expected + Balanced reserve fell short 9 % / 10.5 % (incl. stints starting after one clean lap, when there is no reserve yet) |
| Rest of the lap from half-way (134) | lap profile error 0.017 L vs 0.055 L for "half a lap × the rate" |

The stint windows overlap and the data is mostly one car and track, so these figures are a sanity check, not a calibration. The reserve floor was chosen on this same data. Races (cautions, traffic, multiclass, timed finishes) and other cars, hybrids among them, still need checking against recorded sessions.

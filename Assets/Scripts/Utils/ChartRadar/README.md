# Chart Radar runtime contract

`ChartRadarService` is an optional, Unity-UI-independent calculation boundary.
Selection code should pass the already parsed `SimaiChart`; this module does not
discover songs, apply audio offsets, or parse chart text again.

## Calling and cancellation

- `Analyze(chart, token)` runs synchronously and cooperatively observes the token.
- `AnalyzeAsync(chart, token)` moves the Unity-free calculation off the caller
  thread. Cancellation returns a snapshot with `Status="cancelled"` and
  `IsCancelled=true`; it does not fault the task.
- The caller still owns per-song/difficulty caching and must discard a snapshot
  that no longer matches the current selection.

## Output contract

- `RawValues` and `Scores` always contain `DimensionOrder`; unavailable entries
  are `null`, so callers must not assume a value exists.
- Seven raw dimensions are always used by the regression model. UI axis selection
  happens only after scoring and cannot change model input.
- The seven mapped radar dimensions use the frozen mapping profile. The
  `fitted_constant` score is deliberately identity-mapped and is not on the same
  0-250 scale; do not draw every `Scores` entry as equivalent radar axes.
- `partial` preserves successful raw values but has no fitted constant or mapped
  scores. Regression/scoring failure also preserves completed raw analysis and
  reports an error-stage message.

## Resource boundaries

- Charts above 30,000 adapted events are unavailable.
- Sweep has separate budgets for attacks, retained state history, materialized
  candidate history, candidate count, family connection checks, and selection
  states. Exceeding one fails only Sweep and produces a `partial` result.
- Sweep conflict selection uses connected components and an explicit stack; it
  never relies on one recursive call per candidate.

## Fixed parameters

Feature constants, regression center/scale/intercept/coefficients, and scorer
anchors are compile-time model data. Changing a feature formula or input order
requires refitting and replacing the regression constants together. Display-only
axis changes do not.

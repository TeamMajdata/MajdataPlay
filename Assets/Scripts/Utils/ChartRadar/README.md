# Chart Radar Play integration

The radar implementation lives in the `Assets/Plugins/MajRadar` submodule. This
folder contains only the Play-facing boundary:

- `ChartRadarService` owns one reusable `RadarRuntime` and returns a lightweight
  `ChartRadarSnapshot` suitable for UI caches.
- `PlayExtendedSlideBarCountProvider` supplies extended `K` Slide geometry with
  Play's existing `SlideCodeParser` and `SlideDataBuilder`.

Callers should retain one service instead of constructing one for every chart:

```csharp
private readonly ChartRadarService _chartRadarService = new();

var snapshot = await _chartRadarService.AnalyzeAsync(chart, cancellationToken);
```

Selection code continues to own cancellation, generation checks, logging, and
per-song/difficulty caching. The service does not discover songs, apply audio
offsets, or update Unity UI.

All feature logic, the 40,000-event resource boundary, regression parameters,
and the 250-point mapping profile are versioned in the MajRadar submodule. Do
not install the MajRadar NuGet package into this Unity project as well.

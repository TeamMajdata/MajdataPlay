# Chart Radar Play integration

The radar implementation lives in the `Assets/Plugins/MajRadar` submodule. This
folder contains only the Play-facing boundary:

- The static `ChartRadarService` owns one reusable `RadarRuntime` and returns a
  lightweight `ChartRadarSnapshot` suitable for UI caches.
- `PlayExtendedSlideBarCountProvider` supplies extended `K` Slide geometry with
  Play's existing `SlideCodeParser` and `SlideDataBuilder`.

Callers invoke the facade directly and do not hold a service instance:

```csharp
var snapshot = await ChartRadarService.AnalyzeAsync(chart, cancellationToken);

var noteScore = snapshot.GetScore(RadarOutputDimension.Note);
var trickyRaw = snapshot.GetRawValue(RadarOutputDimension.SlideTricky);
```

String-keyed access remains compatible for existing and dynamic UI code:

```csharp
var sameNoteScore = snapshot.Scores["note"];
```

Selection code continues to own cancellation, generation checks, logging, and
per-song/difficulty caching. The service does not discover songs, apply audio
offsets, or update Unity UI.

All feature logic, the 40,000-event resource boundary, regression parameters,
and the 250-point mapping profile are versioned in the MajRadar submodule. Do
not install the MajRadar NuGet package into this Unity project as well.

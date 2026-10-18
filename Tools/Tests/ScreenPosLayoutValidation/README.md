# Screen position layout validation

Run from the repository root with .NET SDK 9 or newer:

```powershell
dotnet run --project Tools/Tests/ScreenPosLayoutValidation/ScreenPosLayoutValidation.csproj
```

The harness links the production `ScreenPosCanvasLayoutInput` and
`ScreenPosCanvasLayout` sources from `Assets/Scripts/Misc/ScreenPos`. These are
the pure calculations that `ScreenPosCanvasLayoutProvider` runs on
`GlobalCanvas` and that `ScreenPosCanvasLayoutApplier` applies to every scene
canvas. `UnityStubs.cs` provides minimal stand-ins for `Vector2`, `Rect` and
`Mathf`; no NuGet packages are required.

Checks cover the reference resolution, canvas size and screen center for 9:16,
taller and wider screens; the `Main_Display` and `Sub_Display` positions with the
display transform disabled, at the default settings, with main display scaling
anchored at the screen center and with sub display offset and scale; the
`Sub_Cover`, `Sub_Cover_Bottom` and `Sub_Cover_Top` areas, including collapsing
to zero size instead of going negative; anchored position conversion for the
anchors used by `CanvasTemplate` and for stretched anchors; cover pivot
mirroring for negative scales; and value equality of the layout input.

Passing does not verify actual Unity integration: finding the parts by name,
writing `RectTransform`/`CanvasScaler` values, execution order between the
provider and the appliers, or the one-frame delay before `CanvasScaler` applies a
new reference resolution. Check those in Play Mode.

The harness also links the production `RectCornerPositionUpdater` with narrow
settings, provider and rectangle stand-ins. It checks initial settling, a
screen-only resize, provider-only invalidation, repeated layout changes, delayed
manual-transform updates and temporarily unavailable screen dimensions. A stable
10,000-frame loop must perform no corner reads and allocate zero managed bytes.
This covers the updater's invalidation and scheduling; actual CanvasScaler timing,
camera projection, world corners and device hit testing still require Play Mode.

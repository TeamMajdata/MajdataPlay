# Atlas layout validation

From the repository root, with .NET SDK 9 or newer installed:

```powershell
dotnet run --project Tools/Tests/Modules/AtlasLayoutValidation/AtlasLayoutValidation.csproj
dotnet run --project Tools/Tests/Modules/AtlasLayoutValidation/AtlasLayoutValidation.csproj -- <skin directory>
```

The project links the production `AtlasLayout.cs` used by `CustomSkin`'s `AtlasBuilder`
and needs no Unity installation or NuGet packages. Build output goes to `Temp/AtlasLayoutValidation`.

It checks:

- Every atlas image of the given skin (default: `Assets/StreamingAssets/Skins/default`,
  excluding the independently loaded `SubBackgourd.png` and `now_loading.png`) fits at full
  size and the atlas is smaller than the previous 8192 by 4096 `PackTextures` result. The
  atlas size, base-level size and pixel utilization are printed.
- 60 seeded random sets (1-300 images up to 1300 pixels) under 2048, 4096 and 8192 limits:
  slots lie inside the atlas, keep two pixels of padding between each other, use the
  uniformly scaled size, and the atlas is 4-aligned and within the limit.
- An image above the limit scales the whole set uniformly to the limit, preserving
  aspect ratio; an oversubscribed set shrinks until it fits.
- Identical input produces identical layouts, and invalid arguments are rejected.

Success prints `ATLAS_LAYOUT_VALIDATION_PASSED` and exits with code 0.

This validates the layout arithmetic only. Texture allocation, decoding, pixel copies and
memory use are covered by `Tools/Tests/Modules/CustomSkinMemoryValidation` and
`Tools/Tests/Modules/CustomSkinAtlasValidation`.

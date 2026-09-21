# Component spacing and text fitting

- Splits reserve icon width only when that row has an icon and icon display is enabled. Changing or scrolling to an iconless split immediately removes the indent.
- Shared info/text labels measure using the active font before laying out, clamp available widths to zero, and keep name/value columns separate. Labels with no space do not draw. Horizontal clipping contains glyph overhang, outlines and shadows; normal GDI+ ellipsis shortening remains enabled.
- World Record reserves space using the actual icon geometry, including aspect ratio, side, placement and offsets. Centered text beside an icon receives a bounded text band. Offsets reduce text space instead of pushing text over the icon. This covers vertical, two-row and horizontal layouts.
- World Record settings now have a Text shortening selector: automatic, full sentence, full label, WR label, WR sentence, time-and-runner, or time only. Explicit choices use ellipsis if still too wide. Old layouts default to Automatic. The setting participates in XML persistence and the settings hash.

The Text component has no built-in icon. Its shared layout is constrained to its assigned content area, including any content insets, so text cannot spill into adjacent content. This change does not add an icon setting to Text.

## Verification

On Windows with .NET 8 SDK / .NET Framework 4.8.1 targeting assemblies:

```powershell
dotnet build tools/ComponentLayoutVerification/ComponentLayoutVerification.csproj -c Release
.\artifacts\bin\ComponentLayoutVerification\release\ComponentLayoutVerification.exe C:\path\component-layout-proof.png
if ($LASTEXITCODE -ne 0) { throw 'Component layout regression failed' }
```

The runner creates in-memory run data and solid-color test icons; it does not fetch leaderboards. It exercises 720 World Record layout combinations (icon aspect ratios, one/two rows, centered/noncentered, both sides, both placements, offsets, narrow/wide widths), horizontal layouts, split icon changes, Text font overrides, negative/zero widths, clipping restoration, shortening strings, settings XML round-trips, fallback for missing/invalid settings, and the actual dropdown. It creates a hidden test form only to render the settings control for inspection.

Verified locally: all four affected assemblies built with zero warnings/errors; 23,382 assertions passed. The prior Roboto Black rendering regression also passes. No public core API entries were removed.

## Install / rollback

Close LiveSplit. Back up LiveSplit.Core.dll/.pdb and Components/LiveSplit.Splits, LiveSplit.Text, and LiveSplit.WorldRecord DLLs/PDBs. Replace those eight files together from the patch package. Leave layouts, splits, settings, and other components in place. To roll back, close LiveSplit and restore the same files from the backup.

Open the World Record component settings and choose Text shortening. The settings page scrolls when needed so precision and offset controls remain accessible.

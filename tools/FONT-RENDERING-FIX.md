# Outlined font overlap repair

When GDI+ converts a font to a GraphicsPath, overlapping contours are valid ink.
Alternate fill mode cancels that ink and exposes internal outline strokes. Use
nonzero winding for the text and its shadows, and clip the outline to the
exterior of the filled glyph region. Counters remain transparent. Restoring the
Graphics state preserves the caller's clip and transform. Resetting a path after
a sharp shadow resets its fill mode, so the foreground sets Winding again.

This fixes existing fonts without requiring replacement fonts. The Roboto pack
builder also removes overlaps from static faces for use in other GDI+ renderers.
Variable fonts are excluded: their variation deltas cannot survive arbitrary
contour edits. The supplied archive's 54 static faces cover its three widths,
nine weights, and upright/italic styles.

## Build and verify on Windows

Build the core with a .NET 8 SDK and .NET Framework 4.8.1 targeting assemblies:

```powershell
dotnet build src/LiveSplit.Core/LiveSplit.Core.csproj -c Release
python -m pip install fonttools==4.65.0 skia-pathops==0.9.2
python tools/build_livesplit_unlinked_pack.py C:\path\Roboto --output C:\path\Roboto-LSFix
```

Compile the standalone regression checker next to the Release DLL. This example
uses the Windows .NET Framework C# compiler and absolute Windows paths:

```powershell
$bin = (Resolve-Path bin\release).Path
$src = (Resolve-Path tools\VerifyTextRendering.cs).Path
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe "/out:$bin\VerifyTextRendering.exe" "/r:$bin\LiveSplit.Core.dll" /r:System.Drawing.dll $src
& "$bin\VerifyTextRendering.exe" C:\path\Roboto\static C:\path\Roboto-LSFix C:\path\font-proof.png
if ($LASTEXITCODE -ne 0) { throw 'Rendering regression failed' }
```

The checker covers overlapping contours, real counters, clip restoration, opaque
and translucent fills, cached and direct glyph interiors, and sharp/blurred
shadows. It verifies every original and repaired static face and produces a
before/after PNG. Edge antialiasing may differ between direct rendering and the
transparent bitmap cache, so cache comparisons target fully covered interiors.
A single font path runs just that face; this reproduces the old DLL's failure.

Verified on Windows with the supplied Roboto archive: Release core build passed
with zero warnings/errors; 108 face runs passed with 98,835 assertions. The old
release DLL fails on an internal black seam in Roboto Black. Public API comparison
against the desktop v1.1.3 core found the same 4,527 exported API entries.

The font builder fails on glyph processing errors, preserves horizontal metrics
and character mappings, removes stale hint programs, and writes consistent names
and Regular/Italic style flags. Every weight has a separate family, with GDI face
names limited to 31 characters. Original font files are never overwritten.

To use the repaired fonts, install the TTF files and select Roboto LSFix,
Roboto Cond LSFix, or Roboto SemiC LSFix families. Select the weight in the family
name instead of enabling synthetic Bold. Preserve OFL.txt when redistributing.

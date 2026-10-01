# External split icons

The fork supports `<GameIcon path="C:\Images\game.png" />` and `<Icon path="C:\Images\segment.png" />` in LiveSplit split files. Relative input paths resolve beside the split file; saving normalizes them to absolute paths. Run clones retain the external references. Existing embedded icons still work.

This optional Windows conversion/verification tool extracts embedded icons to PNG files, writes a **new** split file, and verifies loading, saving, cloning, and unchanged non-icon XML. Build the solution first, then this project. Run from the application's build folder so the native `livesplit_core.dll` dependency is available (or add its architecture-specific folder to PATH).

```
dotnet build tools/ExternalMedia/ExternalMedia.csproj -c Release
ExternalMedia.exe "C:\Splits\original.lss" "C:\Splits\converted.lss" "C:\Splits\converted.media"
```

Use a new output filename and an empty media directory; the tool writes `game.png`, numbered segment PNGs, and `.roundtrip`/`.clone` verification files. Keep the original file as a backup. The tool does not install DLLs or overwrite the input split file.

Layout backgrounds use `BackgroundImagePath`; Title, Text, and World Record custom pictures use `CustomIconPath`. These are file references and require the corresponding media to remain available. Existing embedded pictures are not automatically converted. External-media support requires this fork; other versions may ignore the references.

# LiveSplit N64 Input Viewer

This component displays a Nintendo 64 controller in a LiveSplit layout and reads live packets from a console N64 serial adapter. Choose the adapter's COM port in the component settings. The component uses the sm64Spy/RetroSpy serial settings (115200 baud, RTS handshake, DTR enabled) and supports its 32-byte and 4-byte N64 packets.

The controller art comes from standard sm64Spy/RetroSpy N64 skin folders. Set the skins folder in component settings, then choose a skin and (when provided by its XML) a background variant. Variants such as uppercase/lowercase lettering and stick-line options appear in a separate dropdown. Each skin folder should contain `skin.xml` and the image files referenced by that XML. Layout settings store the skin folder path, selected skin, and selected variant; they do not embed the images in the layout. The chosen skin is automatically aspect-fitted and centered inside the component area. **Make black skin background transparent** and **Make magenta skin background transparent** color-key exact black and magenta pixels, respectively, in the skin's background layer in the LiveSplit view; either or both options can be enabled. Button and stick artwork remains intact.

For a `<stick>` element, `xrange` and `yrange` set the movement scale in pixels. Optional `maxcardinal` and `maxdiagonal` attributes define the cardinal and diagonal vertices of an octagonal input gate in N64 units (normalized against 128). Input is scaled radially against the slanted octagon edges, so it does not switch abruptly between square cardinal and diagonal caps. `diagonaldeadzone` (also in N64 units) clears tiny cross-axis drift. `centerx`, `centery`, `boundarypoints`, and `boundarymargin` optionally constrain the marker to a convex gate in the background artwork while retaining as much of the requested movement as fits. These attributes are omitted by default, so existing skins keep their prior movement behavior.

Display height, zoom (25–300%), horizontal and vertical position (-100% to 100%), input render delay (0–5000 ms), transparent background, and connection-status label are configurable. At the defaults (100% zoom, 0% offsets, and 0 ms delay), the skin is centered and fitted. Render delay displays the most recently received controller state from the selected number of milliseconds earlier.

The **OBS-only output** option keeps the viewer's normal layout space but leaves it blank in LiveSplit, and starts a local transparent overlay feed. The browser feed is rendered at the selected skin's native canvas dimensions while the layout size remains controlled by **Display height**. The component settings show the native dimensions to use for the OBS **Browser Source**. It pushes changed frames over a local WebSocket and renders asynchronously, targeting up to 60 frames per second; stick movement is interpolated between adapter samples while button changes remain immediate. This uses a separate source, because a normal Window Capture or Display Capture sees the same pixels shown on the desktop. To add the component, choose **N64 Input Viewer** in the Information component list.

## Build

```powershell
dotnet build .\src\LiveSplit\LiveSplit.csproj -c Release
```

The app build copies `LiveSplit.N64Input.dll` to `Components`.

## License and source attribution

The N64 packet decoder and controller-state classes are adapted from [a-habboush/sm64Spy](https://github.com/a-habboush/sm64Spy), tag `v0.2` (commit `cd00b8927ed3bc61b18fd3353aa2bdeb80fc5882`), itself built on RetroSpy. The included adapted source and component are distributed under GPL-3.0; see [LICENSE-GPL-3.0](LICENSE-GPL-3.0). The component's serial reader, skin loader, WinForms settings, and LiveSplit rendering integration are new code.

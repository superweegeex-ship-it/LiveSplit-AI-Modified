# LiveSplit Spotify component

This Spotify component is based on the work of **vergesta**. This fork carries a modified version with presentation controls and LiveSplit connection shortcuts. Credit for the original Spotify component belongs to vergesta.

## Build

From the repository root, build the app as usual:

```powershell
dotnet build .\src\LiveSplit\LiveSplit.csproj -c Release
```

The build places `LiveSplit.Spotify.dll` in the output `Components` directory.

## Configure Spotify

Create a Spotify developer app and register this redirect URI exactly:

```text
http://127.0.0.1:43821/callback/
```

Add **Spotify Now Playing** to the layout and enter the app's Client ID in its settings. Use **Connect Spotify** there, from LiveSplit's right-click menu, or from a configured global hotkey. No client secret is needed; authorization uses PKCE.

## Appearance

The component starts transparent. Settings allow customizing title and artist templates, fonts, sizes, colors, and progress bar colors. Use `{title}` and `{artist}` in the text fields; an empty field hides that row.

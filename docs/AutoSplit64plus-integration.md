# AutoSplit64+ integration

LiveSplit starts its `LiveSplit` named-pipe command server automatically. AutoSplit64+ uses that pipe to read the current split index and send timer commands, so it does not need a separate LiveSplit Server setting.

When `AutoSplit64plus.exe` is available in an `AutoSplit64plus` folder beside LiveSplit, in a sibling `AutoSplit64plus` folder, or in the Desktop `AutoSplit64plus` folder, LiveSplit adds **Launch AutoSplit64+** to its right-click menu. The launcher uses the AutoSplit64+ installation's folder as its working directory, preserving its routes, templates, and configuration. It reports when that same installation is already running instead of opening a second copy.

AutoSplit64+ remains a separate program because it provides its own capture editor and route tools. The LiveSplit menu entry is a launch convenience; timer communication uses the named pipe. AutoSplit64+ requires a game capture source and reset templates. Its setup guide recommends setting the LiveSplit run's **Start Timer at** value to `1.36` seconds for its reset detection. For OBS capture, install the AS64+ Frame Grabber filter and place it before the other filters on the capture source.

This integration was checked against AutoSplit64+ v0.3.1 by Davi Be92, forked from AutoSplit64. See the [upstream project](https://github.com/DaviBe92/AutoSplit64plus) for its license, releases, setup guide, and capture requirements.

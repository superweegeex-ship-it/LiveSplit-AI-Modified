# Racetime.gg Integration for LiveSplit

## Usage

This precompiled version already contains everything you need to use https://racetime.gg/ with LiveSplit, simply download and run: https://github.com/steto-scope/LiveSplit/releases/

## Building LiveSplit and the plugin

The Racetime plugin requires a small modification on the LiveSplit sources in order to be integrated into the application. These changes aren't included in the official LiveSplit repository (yet). If you want to compile LiveSplit by yourself, use the 'raceprovider'-branch of this fork: https://github.com/steto-scope/LiveSplit
To install the plugin, simply place the compiled *.dll-files into the Components/ directory and start LiveSplit. Please note that the Updater of this LiveSplit fork is currently deactivated. It's recommended to use it for testing purposes only.

To tailor the plugin to your instance of Racetime, you will need to adjust the server endpoints, ports and protocols in resources.resx to your specific values.
# LoupixDeck.Plugin.WLED

LoupixDeck plugin that controls [WLED](https://kno.wled.ge/) LED controllers over the
[JSON API](https://kno.wled.ge/interfaces/json-api/).

## Devices

Enter devices under **Settings → Plugins → WLED → Manual hosts** (comma-separated IPs or host
names, optionally with `:port`). Optionally enable **mDNS discovery** (off by default) to find
devices automatically via `_wled._tcp`. Buttons reference a device by its MAC address, so they keep working when its IP
changes. The device state is polled (default every 3 s) to keep buttons up to date.

## Commands

Pick a command in the button editor under **WLED → &lt;device&gt;**.

| Command | Target | Function |
| --- | --- | --- |
| `Wled.Power` | Key | Toggle / on / off; button states Off / On / Offline, each with its own icon and caption |
| `Wled.PowerOn`, `Wled.PowerOff` | Key | Turn on / off |
| `Wled.SetBrightness` | Key | Brightness in percent |
| `Wled.Preset` | Key | Activate a preset |
| `Wled.PresetFolder` | Key | Folder with all presets and effects of the device |
| `Wled.Effect`, `Wled.Palette` | Key | Set an effect / palette |
| `Wled.EffectNext`, `Wled.EffectPrevious`, `Wled.EffectRandom` | Key | Next / previous / random effect |
| `Wled.PaletteNext`, `Wled.PalettePrevious`, `Wled.PaletteRandom` | Key | Next / previous / random palette |
| `Wled.BrightnessDial` | Dial | Brightness; press toggles power |
| `Wled.EffectDial`, `Wled.PaletteDial` | Dial | Select effect / palette; press picks a random one |
| `Wled.SpeedDial`, `Wled.IntensityDial` | Dial | Effect speed / intensity; press resets to 128 |

Effect and palette changes apply to all selected segments.

LoupixDeck switches button states by command name, not per device. With several WLED devices,
all power buttons show the state of the device that changed last.

## Build

```bash
dotnet build -c Release
```

## Try it locally

```bash
loupix install          # packs the plugin and copies it into the LoupixDeck plugins folder
```

Restart LoupixDeck afterwards. `loupix install --debug` targets the plugins folder of a Debug build.

## Release

`loupix pack` writes the Plugin Store files to `dist/`. For a store release, bump `version` in
`plugin.json`, then publish a GitHub release tagged `v<version>`; the workflow in
`.github/workflows/release.yml` builds and attaches the files.

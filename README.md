# Environments

Presets for Windows that close everything you don't need and launch what you do.
"Game" keeps Discord, Telegram, Steam and the game, closes the rest and starts Discord if it isn't running.

## Features
- Built-in presets: Игра, Стрим / запись, Учёба / НМТ, Код, 3D / Blender, Чилл, Чистый лист (Ctrl+Alt+1…6, 0).
- Per preset: what to keep (apps or groups), what to launch if not running, close mode, hotkey, colour.
- Close modes: gentle (WM_CLOSE only), smart (also kill apps that hid to the tray, never touch windows asking to save), force.
- "New desktop" presets: create a separate virtual desktop, switch to it and launch the preset's apps there, closing nothing.
- Preview before applying, "undo" relaunches whatever the last preset closed.
- Protected list: shell, system, drivers, overlays, terminals and Claude are never closed.
- Tray menu, global hotkeys, autostart, preset import/export.

## Build
Requires the .NET 9 SDK.

```
dotnet publish src/Environments -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

Design assets (icons, tokens) live in `design/`. After changing an SVG there, run `node scripts/gen-icons.mjs` to regenerate `UI/IconData.cs`.

## Command line
```
Environments.exe --list
Environments.exe --dry-run "Игра"
Environments.exe --apply "Игра" [--limit notepad,mspaint]
Environments.exe --undo
Environments.exe --hidden        # start in the tray (used by autostart)
```
`--limit` restricts closing and launching to the named processes, which is handy for testing.

Settings live in `%APPDATA%\Environments\config.json` (override with `ENVIRONMENTS_DATA`).
Design brief: [DESIGN.md](DESIGN.md).
